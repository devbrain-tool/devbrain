# DevBrain installer for Windows - downloads latest release, installs to ~/.devbrain/bin/
$ErrorActionPreference = "Stop"

$Repo = "devbrain-tool/devbrain"
$InstallDir = "$env:USERPROFILE\.devbrain\bin"

# Detect architecture
$Arch = if ([Environment]::Is64BitOperatingSystem) {
    if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" }
} else {
    Write-Error "32-bit Windows is not supported"; exit 1
}

Write-Host "Detected platform: $Arch"

# Get latest release
if (-not $env:VERSION) {
    $Release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest"
    $Version = $Release.tag_name
} else {
    $Version = $env:VERSION
}

Write-Host "Installing DevBrain $Version..."

# Download
$Url = "https://github.com/$Repo/releases/download/$Version/devbrain-$Arch.zip"
$TmpFile = [System.IO.Path]::GetTempFileName() + ".zip"
Write-Host "Downloading from $Url"
Invoke-WebRequest -Uri $Url -OutFile $TmpFile

# Extract
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Expand-Archive -Path $TmpFile -DestinationPath $InstallDir -Force
Remove-Item $TmpFile

# Add to PATH
$UserPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($UserPath -notlike "*\.devbrain\bin*") {
    [Environment]::SetEnvironmentVariable("PATH", "$InstallDir;$UserPath", "User")
    Write-Host "Added $InstallDir to user PATH"
}

Write-Host ""
Write-Host "DevBrain $Version installed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "  Location: $InstallDir"
Write-Host "  Binaries: devbrain.exe (CLI), devbrain-daemon.exe (daemon)"
Write-Host ""
Write-Host "Quick start:"
Write-Host "  devbrain start       # Start the daemon"
Write-Host "  devbrain status      # Check health"
Write-Host "  devbrain briefing    # View morning briefing"
Write-Host "  devbrain dashboard   # Open web UI"
Write-Host ""
Write-Host "Restart your terminal to pick up the PATH change."
