param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/manual')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

$manifestPath = Join-Path $root 'src/PdbLens.App/Package.appxmanifest'
[xml]$manifest = Get-Content -LiteralPath $manifestPath
$version = $manifest.Package.Identity.Version

function Invoke-Checked([scriptblock]$action, [string]$message) {
    & $action
    if ($LASTEXITCODE -ne 0) { throw $message }
}

Push-Location $root
try {
    .\build.ps1 -Configuration Release -Package -PackageKind Full

    $builtMsix = Get-ChildItem (Join-Path $root 'src/PdbLens.App/AppPackages') -Recurse -Filter *.msix |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $builtMsix) { throw 'MSIX output not found.' }

    $offlineMsixPath = Join-Path $output "PdbLens-v$version-x64-offline.msix"
    Copy-Item -LiteralPath $builtMsix.FullName -Destination $offlineMsixPath -Force
    & (Join-Path $root 'scripts/Test-PackageContents.ps1') `
        -MsixPath $offlineMsixPath -ExpectedVersion $version -PackageKind Full

    .\build.ps1 -Configuration Release -Package -PackageKind Lean
    $leanBuiltMsix = Get-ChildItem (Join-Path $root 'src/PdbLens.App/AppPackages') -Recurse -Filter *.msix |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $leanBuiltMsix) { throw 'Lean MSIX output not found.' }

    $leanMsixPath = Join-Path $output "PdbLens-v$version-x64-online.msix"
    Copy-Item -LiteralPath $leanBuiltMsix.FullName -Destination $leanMsixPath -Force
    & (Join-Path $root 'scripts/Test-PackageContents.ps1') `
        -MsixPath $leanMsixPath -ExpectedVersion $version -PackageKind Lean

    $portablePath = Join-Path $output 'portable'
    if (Test-Path -LiteralPath $portablePath) {
        Get-ChildItem -LiteralPath $portablePath -Force | Remove-Item -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $portablePath | Out-Null

    Invoke-Checked {
        dotnet publish src/PdbLens.App/PdbLens.App.csproj -c Release -r win-x64 `
            --self-contained true --no-restore -o $portablePath
    } 'Portable publish failed.'

    Get-ChildItem -LiteralPath $portablePath -Filter *.pdb -File | Remove-Item -Force
    Get-ChildItem -LiteralPath $portablePath -Filter *.xml -File | Remove-Item -Force

    & (Join-Path $root 'scripts/Test-PackageContents.ps1') `
        -MsixPath $offlineMsixPath -PortableDirectory $portablePath -PackageKind Full

    $portableZip = Join-Path $output "PdbLens-v$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $portablePath '*') -DestinationPath $portableZip -Force

    $symbolsPath = Join-Path $output 'symbols'
    New-Item -ItemType Directory -Force -Path $symbolsPath | Out-Null
    Copy-Item (Join-Path $root 'build/native/Release/PdbLens.pdb') $symbolsPath -Force
    Copy-Item (Join-Path $root 'build/native/Release/PdbLens.Native.pdb') $symbolsPath -Force
    Copy-Item (Join-Path $root 'src/PdbLens.Core/bin/Release/net10.0-windows10.0.26100.0/PdbLens.Core.pdb') $symbolsPath -Force
    Copy-Item (Join-Path $root 'src/PdbLens.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/PdbLens.App.pdb') $symbolsPath -Force
    $symbolsZip = Join-Path $output "PdbLens-v$version-symbols.zip"
    Compress-Archive -Path (Join-Path $symbolsPath '*') -DestinationPath $symbolsZip -Force

    $artifacts = @($offlineMsixPath, $leanMsixPath, $portableZip, $symbolsZip)
    $hashLines = foreach ($artifact in $artifacts) {
        $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
        "$hash  $(Split-Path -Leaf $artifact)"
    }
    $hashLines | Set-Content (Join-Path $output "PdbLens-v$version-SHA256SUMS.txt") -Encoding ascii

    Get-ChildItem -LiteralPath $output -File | Select-Object Name, Length
}
finally {
    Pop-Location
}
