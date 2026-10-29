$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$windowsDir = Split-Path -Parent $installerDir
$csprojPath = Join-Path $windowsDir "WritingTools.csproj"
$installerCs = Join-Path $installerDir "InstallerProgram.cs"
$appIco = Join-Path $windowsDir "app.ico"
$publishDir = Join-Path $windowsDir "bin\installer-staging"
$payloadZip = Join-Path $installerDir "payload.zip"
$outputDir = Join-Path $installerDir "output"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"

if (-not (Test-Path $csc)) {
    throw "Could not find .NET Framework compiler at $csc"
}

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "  Building Local Writing Tools Windows Installer     " -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan

# Clean previous staging files
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# 1. Publish self-contained Windows app (retains Microsoft Authenticode signatures on all .NET 8 DLLs)
Write-Host "[1/4] Publishing self-contained Local Writing Tools (win-x64)..." -ForegroundColor Yellow
dotnet publish $csprojPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishDir --nologo -v quiet
Get-ChildItem $publishDir -Filter "*.pdb" -Recurse | Remove-Item -Force

# 2. Build lightweight Uninstall.exe (~25 KB, without payload.zip) directly into staging folder
Write-Host "[2/4] Compiling lightweight Uninstall.exe..." -ForegroundColor Yellow
$uninstallExePath = Join-Path $publishDir "Uninstall.exe"
& $csc /nologo /target:winexe /optimize+ /platform:x64 `
    /out:$uninstallExePath `
    /win32icon:$appIco `
    /resource:"$appIco,LocalWritingTools.app.ico" `
    /r:"$netDir\System.dll" `
    /r:"$netDir\System.Drawing.dll" `
    /r:"$netDir\System.Windows.Forms.dll" `
    /r:"$netDir\System.IO.Compression.dll" `
    /r:"$netDir\System.IO.Compression.FileSystem.dll" `
    $installerCs

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $uninstallExePath)) {
    throw "Failed to compile Uninstall.exe"
}

# 3. Create compressed payload.zip
Write-Host "[3/4] Compressing application payload into payload.zip..." -ForegroundColor Yellow
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipSizeMb = [Math]::Round((Get-Item $payloadZip).Length / 1MB, 2)
Write-Host "      Payload archive size: $zipSizeMb MB" -ForegroundColor Gray

# Remove staging folder now that payload.zip is created
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

# 4. Build final single-file LocalWritingTools-Setup.exe with payload.zip embedded
Write-Host "[4/4] Compiling standalone LocalWritingTools-Setup.exe..." -ForegroundColor Yellow
$finalSetupExe = Join-Path $outputDir "LocalWritingTools-Setup.exe"
if (Test-Path $finalSetupExe) { Remove-Item $finalSetupExe -Force }

& $csc /nologo /target:winexe /optimize+ /platform:x64 `
    /out:$finalSetupExe `
    /win32icon:$appIco `
    /resource:"$appIco,LocalWritingTools.app.ico" `
    /resource:"$payloadZip,LocalWritingTools.payload.zip" `
    /r:"$netDir\System.dll" `
    /r:"$netDir\System.Drawing.dll" `
    /r:"$netDir\System.Windows.Forms.dll" `
    /r:"$netDir\System.IO.Compression.dll" `
    /r:"$netDir\System.IO.Compression.FileSystem.dll" `
    $installerCs

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $finalSetupExe)) {
    throw "Failed to compile LocalWritingTools-Setup.exe"
}

# Clean temporary payload.zip
Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue

$finalSizeMb = [Math]::Round((Get-Item $finalSetupExe).Length / 1MB, 2)
Write-Host "=====================================================" -ForegroundColor Green
Write-Host "[OK] Installer built successfully!" -ForegroundColor Green
Write-Host "     Output: $finalSetupExe ($finalSizeMb MB)" -ForegroundColor Green
Write-Host "=====================================================" -ForegroundColor Green
