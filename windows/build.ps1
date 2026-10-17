# WritingTools Windows Build Script
$ErrorActionPreference = "Stop"

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  Building WritingTools for Windows (NPU)     " -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

# 1. Check or locate .NET 8 SDK
$dotnetCmd = "dotnet"
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    $userDotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
    if (Test-Path $userDotnet) {
        $dotnetCmd = $userDotnet
        $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
        $env:PATH = "$env:USERPROFILE\.dotnet;" + $env:PATH
    } else {
        Write-Host "Installing .NET 8 SDK to $env:USERPROFILE\.dotnet..." -ForegroundColor Yellow
        $scriptPath = "$env:TEMP\dotnet-install.ps1"
        Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1" -OutFile $scriptPath
        powershell -ExecutionPolicy Bypass -File $scriptPath -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"
        $dotnetCmd = "$env:USERPROFILE\.dotnet\dotnet.exe"
        $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
        $env:PATH = "$env:USERPROFILE\.dotnet;" + $env:PATH
    }
}

Write-Host "Using .NET SDK: $(& $dotnetCmd --version)" -ForegroundColor Green

# 2. Build Debug & Run Tests
Write-Host "`nRunning automated verification tests..." -ForegroundColor Cyan
& $dotnetCmd run --project "$PSScriptRoot\tests\WritingTools.Tests.csproj"

# 3. Publish Self-Contained Standalone Executable
Write-Host "`nPublishing Standalone Executable..." -ForegroundColor Cyan
& $dotnetCmd publish "$PSScriptRoot\WritingTools.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$PSScriptRoot\publish-standalone"

# 4. Publish Framework-Dependent Executable
Write-Host "`nPublishing Framework-Dependent Executable..." -ForegroundColor Cyan
& $dotnetCmd publish "$PSScriptRoot\WritingTools.csproj" `
    -c Release `
    -o "$PSScriptRoot\publish"

Write-Host "`n==============================================" -ForegroundColor Green
Write-Host "  Build Succeeded!                             " -ForegroundColor Green
Write-Host "  Standalone: $PSScriptRoot\publish-standalone\WritingTools.exe" -ForegroundColor Green
Write-Host "  Framework:  $PSScriptRoot\publish\WritingTools.exe" -ForegroundColor Green
Write-Host "==============================================" -ForegroundColor Green
