$ErrorActionPreference = 'Stop'

# AppGeek ships an Inno Setup installer. The package downloads it from the GitHub release for the
# matching tag and verifies it against a SHA-256 checksum rather than embedding the binary. Because
# nothing is embedded, this package must NOT contain a tools\VERIFICATION.txt - that file is only
# for packages that ship a binary inside the nupkg, and including one is what the USP 8.0.0
# submission was rejected for.
$packageArgs = @{
  packageName    = 'appgeek'
  fileType       = 'exe'
  url            = 'https://github.com/techygeekshome/AppGeek/releases/download/v1.1.4/AppGeekSetup.exe'
  checksum       = 'a39d5fedb9edb3176fd97e1d6966e32dab7f5f33e876ec5091441613e2ff4bc3'
  checksumType   = 'sha256'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
