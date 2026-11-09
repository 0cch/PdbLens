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
$parts = $Version.Split('.')
if ($parts.Count -ne 4) {
    throw "Expected four-part version X.Y.Z.BUILD, got: $Version"
}

$output = [IO.Path]::GetFullPath($OutputPath)
$directory = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $directory | Out-Null

$content = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2017/2"
    Version="$Version"
    Uri="$MsixUri">
  <MainPackage
      Name="0cch.PdbLens"
      Publisher="$Publisher"
      Version="$Version"
      ProcessorArchitecture="x64"
      Uri="$MsixUri" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="0" />
  </UpdateSettings>
</AppInstaller>
"@

[IO.File]::WriteAllText($output, $content, [Text.UTF8Encoding]::new($false))
Write-Host "Online installer created: $output"
