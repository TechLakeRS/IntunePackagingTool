[CmdletBinding()]
Param(
    [Parameter(Mandatory=$false)]
    [String]$LogFilesFolder = "C:\ProgramData\Microsoft\IntuneManagementExtension\Logs"
)

function Show-IntuneLogViewer {
    param(
        [string]$LogPath
    )
    
    Write-Host "Processing log file: $LogPath" -ForegroundColor Green
    
    # Create list for log entries
    $LogEntryList = [System.Collections.Generic.List[PSObject]]@()
    
    # Read log file
    $Log = Get-Content -Path $LogPath
    $LineNumber = 1
    
    # Regex patterns for log parsing
    $SingleLineRegex = '^\<\!\[LOG\[(.*)]LOG\].*\<time="([0-9]{1,2}):([0-9]{1,2}):([0-9]{1,2}).([0-9]{1,})".*date="([0-9]{1,2})-([0-9]{1,2})-([0-9]{4})" component="(.*?)" context="(.*?)" type="(.*?)" thread="(.*?)" file="(.*?)">$'
    
    foreach ($CurrentLogEntry in $Log) {
        if($CurrentLogEntry -Match $SingleLineRegex) {
            $LogMessage = $Matches[1].Trim()
            $Hour = $Matches[2]
            $Minute = $Matches[3]
            $Second = $Matches[4]
            $Month = $Matches[6]
            $Day = $Matches[7]
            $Year = $Matches[8]
            $Component = $Matches[9]
            $Type = $Matches[11]
            $Thread = $Matches[12]
            
            # Format DateTime for display
            if($Month -like "?") { $Month = "0$Month" }
            if($Day -like "?") { $Day = "0$Day" }
            $DateTime = "$Year-$Month-$Day $($Hour):$($Minute):$($Second)"
            
            $LogEntryList.add([PSCustomObject]@{
                'Line' = $LineNumber
                'DateTime' = $DateTime
                'Component' = $Component
                'Type' = $Type
                'Thread' = $Thread
                'Message' = $LogMessage
            })
        }
        else {
            # Handle non-standard log entries
            $LogEntryList.add([PSCustomObject]@{
                'Line' = $LineNumber
                'DateTime' = ''
                'Component' = ''
                'Type' = ''
                'Thread' = ''
                'Message' = $CurrentLogEntry
            })
        }
        $LineNumber++
    }
    
    # Show in GridView
    $LogEntryList | Out-GridView -Title "Log Viewer: $(Split-Path $LogPath -Leaf)" -OutputMode Multiple
}

# Main script
Write-Host "Intune Log File Viewer" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan

# Get log files
$LogFiles = Get-ChildItem -Path $LogFilesFolder -Filter *.log -ErrorAction SilentlyContinue | 
    Where-Object { 
        $_.Name -match '(IntuneManagementExtension|AgentExecutor|AppWorkload|AppActionProcessor|ClientHealth|DeviceHealthMonitoring|HealthScripts|Sensor|Win32App|Reset-Appx)'
    } |
    Select-Object @{Name='FileName';Expression={$_.Name}},
                  @{Name='Size';Expression={"{0:N2} MB" -f ($_.Length / 1MB)}},
                  @{Name='LastModified';Expression={$_.LastWriteTime}},
                  @{Name='FullPath';Expression={$_.FullName}}

if (-not $LogFiles) {
    Write-Host "No log files found in: $LogFilesFolder" -ForegroundColor Yellow
    exit
}

# Show file selection dialog
$SelectedFile = $LogFiles | Out-GridView -Title "Select a log file to view" -OutputMode Single

if ($SelectedFile) {
    Show-IntuneLogViewer -LogPath $SelectedFile.FullPath
} else {
    Write-Host "No file selected" -ForegroundColor Yellow
}