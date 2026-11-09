param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Run,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$nativeBuild = Join-Path $root 'build/native'

cmake -S $root -B $nativeBuild -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE -ne 0) { throw 'CMake configuration failed.' }

cmake --build $nativeBuild --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }

ctest --test-dir $nativeBuild -C $Configuration --output-on-failure
if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }

dotnet restore (Join-Path $root 'PdbLens.sln')
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }

dotnet build (Join-Path $root 'PdbLens.sln') -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Managed build failed.' }

dotnet test (Join-Path $root 'tests/PdbLens.Core.Tests/PdbLens.Core.Tests.csproj') `
    -c $Configuration --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Managed tests failed.' }

$app = Join-Path $root "src/PdbLens.App/bin/x64/$Configuration/net9.0-windows10.0.26100.0/win-x64/PdbLens.App.exe"
Write-Host "Build completed: $app"

if ($Package) {
    dotnet build (Join-Path $root 'src/PdbLens.App/PdbLens.App.csproj') `
        -c Release -p:Platform=x64 -p:PdbLensPackaged=true `
        -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
    Write-Host "MSIX package created under: src/PdbLens.App/AppPackages"
}

if ($Run) {
    Start-Process -FilePath $app
}
