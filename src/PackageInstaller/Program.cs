using System;
using System.Collections.Generic;
using System.Linq;
using NDesk.Options;
using System.IO;
using System.Reflection;

namespace HedgehogDevelopment.PackageInstaller
{
	/// <summary>
	/// Installer command line utility. Uses NDesk.Options to parse the command line. For more information, please see
	/// http://www.ndesk.org/Options. 
	/// </summary>
	internal class Program
	{
		private static int _verbosity;
		private static string SitecoreConnectorDll { get; set; }
		private static string SitecoreConnectorAsmx { get; set; }

		private static void Main(string[] args)
		{
			#region Declare options and installer variables

			// Installer variables
			string packagePath = null;
			string sitecoreWebUrl = null;
			string sitecoreDeployFolder = null;
			bool showHelp = args.Length == 0;
			bool removePackageInstaller = false;
		    int timeout = 10*60*1000;

			// Options declaration
			OptionSet options = new OptionSet()
			{
				{
					"p|packagePath=",
					"The {PACKAGE PATH} is the path to the package. The package must be located in a folder reachable by the web server.\n",
					v => packagePath = v
				},
				{
					"u|sitecoreUrl=", "The {SITECORE URL} is the url to the root of the Sitecore server.\n",
					v => sitecoreWebUrl = v
				},
				{
					"f|sitecoreDeployFolder=", "The {SITECORE DEPLOY FOLDER} is the UNC path to the Sitecore web root.\n",
					v => sitecoreDeployFolder = v
				},
				{
					"v", "Increase debug message verbosity.\n",
					v => { if (v != null) ++_verbosity; }
				},
				{
					"h|help", "Show this message and exit.",
					v => showHelp = v != null
				},
				{
					"c|cleanup", "Remove package installer when done",
					v => removePackageInstaller = v != null
				},
			    {
			        "t|timeout=", "Package installer timeout (in seconds)",
			        v => {
			            int t;
			            if (int.TryParse(v, out t))
			                timeout = t * 1000;
			        }
			    }
			};

			#endregion

			// Parse options - exit on error
			try
			{
				options.Parse(args);
			}
			catch (OptionException e)
			{
				ShowError(e.Message);
				Environment.Exit(100);
			}

			// Display help if one is requested or no parameters are provided
			if (showHelp)
			{
				ShowHelp(options);
				return;
			}

			#region Validate and process parameters

			bool parameterMissing = false;

			if (string.IsNullOrEmpty(packagePath))
			{
				ShowError("Package Path is required.");
				parameterMissing = true;
			}

			if (string.IsNullOrEmpty(sitecoreWebUrl))
			{
				ShowError("Sitecore Web URL ie required.");
				parameterMissing = true;
			}

			if (string.IsNullOrEmpty(sitecoreDeployFolder))
			{
				ShowError("Sitecore Deploy folder is required.");
				parameterMissing = true;
			}

			if (parameterMissing) 
				return;

			if (!Directory.Exists(sitecoreDeployFolder))
			{
				ShowError(string.Format("Sitecore Deploy Folder {0} not found.", sitecoreDeployFolder));
				return;
			}

			try
			{
				Debug("Initializing update package installation: {0}", packagePath);
				if (sitecoreDeployFolder.LastIndexOf(@"\", StringComparison.Ordinal) != sitecoreDeployFolder.Length - 1)
				{
					sitecoreDeployFolder = sitecoreDeployFolder + @"\";
				}

				if (sitecoreWebUrl.LastIndexOf(@"/", StringComparison.Ordinal) != sitecoreWebUrl.Length - 1)
				{
					sitecoreWebUrl = sitecoreWebUrl + @"/";
				}

				// Install Sitecore connector
				if (DeploySitecoreConnector(sitecoreDeployFolder))
				{
					using (TdsPackageInstaller.TdsPackageInstaller service = new TdsPackageInstaller.TdsPackageInstaller())
					{
						service.Url = string.Concat(sitecoreWebUrl, Properties.Settings.Default.SitecoreConnectorFolder,
							"/TdsPackageInstaller.asmx");
						service.Timeout = timeout;

						Debug("Initializing package installation ..");
						Debug("   Service URL {0}, timeout {1}s", service.Url, service.Timeout/1000);

						service.InstallPackage(packagePath);

						Debug("Update package installed successfully.");
					}
				}
				else
				{
					Console.WriteLine("Sitecore connector deployment failed.");

					Environment.Exit(101);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Exception: {0}({1})\n{2}", ex.Message, ex.GetType().Name, ex.StackTrace);

				if (ex.InnerException != null)
				{
					Console.WriteLine("\n\nInnerException: {0}({1})\n{2}", ex.InnerException.Message,
						ex.InnerException.GetType().Name, ex.InnerException.StackTrace);
				}

				Environment.Exit(102);
			}
			finally
			{
				if (removePackageInstaller)
				{
					// Remove Sitecore connection
					RemoveSitecoreConnector();
				}
			}

			#endregion
		}

		/// <summary>
		/// Displays the help message
		/// </summary>
		/// <param name="opts"></param>
		private static void ShowHelp(OptionSet opts)
		{
			Console.WriteLine("Usage: packageinstaller [OPTIONS]");
			Console.WriteLine("Installs a sitecore package.");
			Console.WriteLine();
			Console.WriteLine("Example:");
			Console.WriteLine(
				@"-v -sitecoreUrl ""http://mysite.com/"" -sitecoreDeployFolder ""C:\inetpub\wwwroot\mysite\Website"" -packagePath ""C:\Package1.update""");
			Console.WriteLine();
			Console.WriteLine("Options:");

			opts.WriteOptionDescriptions(Console.Out);
		}

		/// <summary>
		/// Displays an error message
		/// </summary>
		/// <param name="message"></param>
		private static void ShowError(string message)
		{
			Console.Write("Error: ");
			Console.WriteLine(message);
			Console.WriteLine("Try `packageinstaller --help' for more information.");
		}

		/// <summary>
		/// Deploys the 
		/// </summary>
		/// <param name="sitecoreDeployFolder"></param>
		/// <returns></returns>
		private static bool DeploySitecoreConnector(string sitecoreDeployFolder)
		{
			Debug("Initializing Sitecore connector at {0}...", sitecoreDeployFolder);

			string sourceFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			FileInfo serviceLibrary = new FileInfo(sourceFolder + @"\HedgehogDevelopment.TDS.PackageInstallerService.dll");
			FileInfo serviceFile = new FileInfo(sourceFolder + @"\Includes\TdsPackageInstaller.asmx");

			if (!serviceLibrary.Exists)
			{
				ShowError("Cannot find file " + serviceLibrary);

				return false;
			}

			if (!serviceFile.Exists)
			{
				ShowError("Cannot find file " + serviceFile);

				return false;
			}

			if (!Directory.Exists(sitecoreDeployFolder + Properties.Settings.Default.SitecoreConnectorFolder))
			{
				Directory.CreateDirectory(sitecoreDeployFolder + Properties.Settings.Default.SitecoreConnectorFolder);
			}

			SitecoreConnectorDll = sitecoreDeployFolder + @"bin\" + serviceLibrary.Name;
			SitecoreConnectorAsmx = sitecoreDeployFolder + Properties.Settings.Default.SitecoreConnectorFolder + @"\" +
			                        serviceFile.Name;

			bool updated = CopyIfChanged(serviceLibrary, new FileInfo(SitecoreConnectorDll));
			updated |= CopyIfChanged(serviceFile, new FileInfo(SitecoreConnectorAsmx));

			Debug(updated ? "Sitecore connector deployed successfully." : "Sitecore connector already deployed.");
			return true;
		}

		private static bool CopyIfChanged(FileInfo source, FileInfo target)
		{
			if (!source.Exists)
				return false;

			if (!target.Exists)
			{
				File.Copy(source.FullName, target.FullName);
				File.SetAttributes(target.FullName, FileAttributes.Normal);
				return true;
			}

			if (source.Length != target.Length || source.LastWriteTime > target.LastWriteTime)
			{
				File.Copy(source.FullName, target.FullName, true);
				File.SetAttributes(target.FullName, FileAttributes.Normal);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Removes the sitecore connector from the site
		/// </summary>
		private static void RemoveSitecoreConnector()
		{
			if (!string.IsNullOrEmpty(SitecoreConnectorDll) && !string.IsNullOrEmpty(SitecoreConnectorAsmx))
			{
				File.SetAttributes(SitecoreConnectorDll, FileAttributes.Normal);
				File.SetAttributes(SitecoreConnectorAsmx, FileAttributes.Normal);

				File.Delete(SitecoreConnectorDll);
				File.Delete(SitecoreConnectorAsmx);

				Debug("Sitecore connector removed successfully.");
			}
		}

		/// <summary>
		/// Writes a debug message to the console
		/// </summary>
		/// <param name="format"></param>
		/// <param name="args"></param>
		private static void Debug(string format, params object[] args)
		{
			if (_verbosity > 0)
			{
				Console.Write("[{0}] ", DateTime.Now.ToString("hh:mm:ss"));
				Console.WriteLine(format, args);
			}
		}
	}
}