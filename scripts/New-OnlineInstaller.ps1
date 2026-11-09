param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$MsixUri,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$Publisher = 'CN=PdbLens'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Expected four-part version X.Y.Z.BUILD, got: $Version"
}
$parts = $Version.Split('.')
if ($parts.Count -ne 4 -or @($parts | Where-Object { [uint64]$_ -gt 65535 }).Count -gt 0) {
    throw "Version components must be between 0 and 65535: $Version"
}
if ($MsixUri -notmatch '^https://') {
    throw "MSIX URI must use HTTPS: $MsixUri"
}

$escapedVersion = [Security.SecurityElement]::Escape($Version)
$escapedMsixUri = [Security.SecurityElement]::Escape($MsixUri)
$escapedPublisher = [Security.SecurityElement]::Escape($Publisher)

$output = [IO.Path]::GetFullPath($OutputPath)
$directory = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $directory | Out-Null

$content = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2017/2"
    Version="$escapedVersion"
    Uri="$escapedMsixUri">
  <MainPackage
      Name="0cch.PdbLens"
      Publisher="$escapedPublisher"
      Version="$escapedVersion"
      ProcessorArchitecture="x64"
      Uri="$escapedMsixUri" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="0" />
  </UpdateSettings>
</AppInstaller>
"@

[IO.File]::WriteAllText($output, $content, [Text.UTF8Encoding]::new($false))
Write-Host "Online installer created: $output"
