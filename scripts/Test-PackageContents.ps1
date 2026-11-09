param(
    [Parameter(Mandatory = $true)]
    [string]$MsixPath,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$packagePath = [IO.Path]::GetFullPath($MsixPath)
if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "MSIX not found: $packagePath"
}

$requiredFiles = @(
    'PdbLens.App.exe',
    'PdbLens.Core.dll',
    'PdbLens.Native.dll',
    'msdia140.dll',
    'coreclr.dll',
    'hostfxr.dll',
    'hostpolicy.dll',
    'Microsoft.ui.xaml.dll',
    'Microsoft.UI.Xaml.Controls.dll',
    'Microsoft.WindowsAppRuntime.dll',
    'Microsoft.Windows.SDK.NET.dll',
    'AppxManifest.xml'
)

$forbiddenPatterns = @(
    'onnxruntime.dll',
    'DirectML.dll',
    'Microsoft.Windows.Search.dll',
    'Microsoft.Windows.Widgets.dll',
    'Microsoft.Windows.AI',
    'Microsoft.Asg.SemanticIndex',
    'Microsoft.ML',
    'System.Numerics.Tensors.dll',
    'PerceptiveStreaming.dll',
    'WebView2'
)

$zip = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $names = @($zip.Entries | ForEach-Object FullName)
    $errors = [Collections.Generic.List[string]]::new()

    foreach ($required in $requiredFiles) {
        if (-not ($names | Where-Object { $_ -eq $required })) {
            $errors.Add("Missing required package file: $required")
        }
    }

    foreach ($pattern in $forbiddenPatterns) {
        $matches = @($names | Where-Object { $_ -like "*$pattern*" })
        if ($matches.Count -gt 0) {
            $errors.Add("Forbidden package content '$pattern': $($matches -join ', ')")
        }
    }

    if ($ExpectedVersion) {
        $manifestEntry = $zip.GetEntry('AppxManifest.xml')
        if ($manifestEntry) {
            $reader = [IO.StreamReader]::new($manifestEntry.Open())
            try {
                [xml]$manifest = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }

            $actualVersion = $manifest.Package.Identity.Version
            if ($actualVersion -ne $ExpectedVersion) {
                $errors.Add("MSIX version mismatch: expected $ExpectedVersion, found $actualVersion")
            }
        }
    }

    if ($errors.Count -gt 0) {
        throw ($errors -join [Environment]::NewLine)
    }

    $rawBytes = ($zip.Entries | Measure-Object Length -Sum).Sum
    $compressedBytes = ($zip.Entries | Measure-Object CompressedLength -Sum).Sum
    Write-Host ("Package validation passed: {0:N1} MB on disk, {1:N1} MB uncompressed" -f
        ((Get-Item -LiteralPath $packagePath).Length / 1MB), ($rawBytes / 1MB))
    Write-Host ("Compressed payload: {0:N1} MB" -f ($compressedBytes / 1MB))
}
finally {
    $zip.Dispose()
}
