param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$project = (Get-ChildItem -Path $PSScriptRoot -Filter "*.csproj" | Select-Object -First 1).FullName
$publishDir = Join-Path $PSScriptRoot "artifacts\publish"
$compactDir = Join-Path $PSScriptRoot "artifacts\compact"
$outputDir = Join-Path $PSScriptRoot "artifacts"
$archive = Join-Path $outputDir "GuiFileLAN-win-x64.zip"
$compactArchive = Join-Path $outputDir "GuiFileLAN-Compact-win-x64.zip"

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Application build failed." }

dotnet publish $project -c $Configuration -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false -o $compactDir
if ($LASTEXITCODE -ne 0) { throw "Compact application build failed." }

$compiler = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($compiler) {
    & $compiler (Join-Path $PSScriptRoot "installer.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer creation failed." }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archive -Force
    Compress-Archive -Path (Join-Path $compactDir "*") -DestinationPath $compactArchive -Force
    Write-Host "Self-contained installer and compact package created in the artifacts folder."
} else {
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archive -Force
    Compress-Archive -Path (Join-Path $compactDir "*") -DestinationPath $compactArchive -Force
    Write-Warning "Inno Setup is not installed. Self-contained and compact ZIP packages were created instead."
}
