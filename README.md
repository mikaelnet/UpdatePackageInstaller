# UpdatePackageInstaller
Installs update packages in Sitecore from the command line

This fork adds a cleanup options, giving users the ability to leave the package 
installer dll/asmx files on the target server and it only copies those files if 
the files are changed. This reduces the number of restarts of the IIS application
(and the annoing timeouts associated with this) when several packages are being 
being deployed.
