# RemoteDeployment.ps1
# Simplified remote deployment script for WPF Packaging Tool

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$TargetComputer,
    
    [Parameter(Mandatory=$true)]
    [string]$SourcePath,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet("Install", "Uninstall", "Repair")]
    [string]$DeploymentType = "Install",
    
    [Parameter(Mandatory=$false)]
    [switch]$CleanupAfterDeploy
)

$LocalTempPath = "C:\Temp\AppDeploy"
$PsExecPath = "C:\Windows\System32\PsExec.exe"

# Simple status messages for WPF output
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Remote Application Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Target: $TargetComputer"
Write-Host "Source: $SourcePath"
Write-Host "Type: $DeploymentType"
Write-Host ""

try {
    # Step 1: Check connectivity
    Write-Host "Checking connectivity to $TargetComputer..." -ForegroundColor Yellow
    if (-not (Test-Connection -ComputerName $TargetComputer -Count 1 -Quiet)) {
        throw "$TargetComputer is not reachable"
    }
    Write-Host "✓ $TargetComputer is online" -ForegroundColor Green

    # Step 2: Verify source files - check for PS1 since we'll call it directly
    Write-Host "Verifying source path..." -ForegroundColor Yellow
    if (-not (Test-Path $SourcePath)) {
        throw "Source path not found: $SourcePath"
    }
    
    # Check for Deploy-Application.ps1 in the Application subfolder
    $deployScript = Join-Path (Join-Path $SourcePath "Application") "Deploy-Application.ps1"
    if (-not (Test-Path $deployScript)) {
        throw "Deploy-Application.ps1 not found at: $deployScript"
    }
    Write-Host "✓ Source path verified" -ForegroundColor Green

    # Step 3: Create temp directory on remote computer
    Write-Host "Creating temp directory on target..." -ForegroundColor Yellow
    & $PsExecPath \\$TargetComputer -accepteula -s cmd.exe /c "mkdir `"$LocalTempPath`" 2>nul" 2>&1 | Out-Null
    Write-Host "✓ Temp directory ready: $LocalTempPath" -ForegroundColor Green

    # Step 4: Copy files to remote computer
    Write-Host "Copying deployment files to target..." -ForegroundColor Yellow
    $targetUNC = "\\$TargetComputer\$($LocalTempPath.Replace(':', '$'))"
    
    # Create directory if needed
    if (-not (Test-Path $targetUNC)) {
        New-Item -Path $targetUNC -ItemType Directory -Force | Out-Null
    }
    
    # Copy all files (including Application folder structure)
    Copy-Item -Path "$SourcePath\*" -Destination $targetUNC -Recurse -Force -ErrorAction Stop
    Write-Host "✓ Files copied successfully" -ForegroundColor Green

    # Step 5: Execute deployment via PsExec - exe is in Application subfolder
    Write-Host ""
    Write-Host "Executing deployment on target machine..." -ForegroundColor Yellow
    Write-Host "Command: Application\Deploy-Application.exe -DeploymentType $DeploymentType" -ForegroundColor Cyan
    Write-Host ""
    
    # Run deployment through PsExec -> cmd -> Deploy-Application.exe (in Application subfolder)
    $deployCmd = "powershell.exe -ExecutionPolicy Bypass -NoProfile -File `"$LocalTempPath\Application\Deploy-Application.ps1`" -DeploymentType $DeploymentType -AllowRebootPassThru"
    $output = & $PsExecPath \\$TargetComputer -accepteula -s cmd.exe /c $deployCmd 2>&1
    
    # Display output
    foreach ($line in $output) {
        if ($line) {
            Write-Host $line
        }
    }
    
    $exitCode = $LASTEXITCODE
    Write-Host ""
    Write-Host "Exit code: $exitCode"
    
    # Step 6: Cleanup if requested
    if ($CleanupAfterDeploy) {
        Write-Host ""
        Write-Host "Cleaning up temporary files..." -ForegroundColor Yellow
        & $PsExecPath \\$TargetComputer -accepteula -s -h cmd.exe /c "rmdir /s /q `"$LocalTempPath`"" 2>&1 | Out-Null
        Write-Host "✓ Cleanup completed" -ForegroundColor Green
    }
    
    # Step 7: Report results
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    
    if ($exitCode -eq 0) {
        Write-Host "✓ Deployment completed successfully!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Cyan
        exit 0
    }
    elseif ($exitCode -eq 3010 -or $exitCode -eq 1641) {
        Write-Host "✓ Deployment completed - Reboot required" -ForegroundColor Yellow
        Write-Host "========================================" -ForegroundColor Cyan
        exit 3010
    }
    else {
        Write-Host "✗ Deployment failed with exit code: $exitCode" -ForegroundColor Red
        Write-Host "========================================" -ForegroundColor Cyan
        exit $exitCode
    }
}
catch {
    Write-Host ""
    Write-Host "✗ ERROR: $_" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Cyan
    exit 1
}