# Application Logs Enhancement

## Summary
Enhanced the Logs & Diagnostics view to include NBB application installation logs in addition to Intune Management Extension logs.

---

## Changes Made

### 1. IntuneDiagnosticsService.cs

#### Local Computer Log Sources (3 locations):
```csharp
// 1. Intune Management Extension logs
C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\*.log

// 2. System application installation logs (NBB)
C:\NBB\Logs\Software_Installations\*.log (recursive)

// 3. User application installation logs (NBB)
%LocalAppData%\NBB\Logs\Software_Installations\*.log (recursive)
```

#### Remote Computer Log Sources (2 locations):
```csharp
// 1. Intune Management Extension logs
\\COMPUTER\c$\ProgramData\Microsoft\IntuneManagementExtension\Logs\*.log

// 2. System application installation logs (NBB)
\\COMPUTER\c$\NBB\Logs\Software_Installations\*.log (recursive)
```

**Note:** User-based logs on remote computers are not accessible via simple UNC paths as they reside in user profile folders. This would require enumerating all user profiles, which is complex and may not be reliable.

#### Added Source Property:
```csharp
public class LogFileInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Source { get; set; } = "Unknown";  // ← NEW
    // ...
}
```

### 2. LogsView.xaml

#### Updated Header:
- **Before:** "Intune Management Extension Diagnostics"
- **After:** "Application Logs & Diagnostics"
- **Subtitle:** "View Intune and NBB application installation logs"

#### Added Source Column to DataGrid:
```xml
<DataGrid.Columns>
    <DataGridTextColumn Header="File Name" Binding="{Binding Name}" Width="2*"/>
    <DataGridTextColumn Header="Source" Binding="{Binding Source}" Width="1.5*"/> ← NEW
    <DataGridTextColumn Header="Size" Binding="{Binding SizeFormatted}" Width="100"/>
    <DataGridTextColumn Header="Last Modified" Binding="{Binding LastModified}" Width="200"/>
    <DataGridTemplateColumn Header="Actions" Width="150">
        <!-- View and Save buttons -->
    </DataGridTemplateColumn>
</DataGrid.Columns>
```

---

## Log Sources Explained

### Intune Management Extension
**Path:** `C:\ProgramData\Microsoft\IntuneManagementExtension\Logs`
- Contains Intune service logs
- Tracks app downloads, installations, and management activities
- System-wide logs

### NBB System Installations
**Path:** `C:\NBB\Logs\Software_Installations`
- PSADT (PowerShell App Deployment Toolkit) logs
- System context installations
- Subfolder per application with timestamp
- Example: `C:\NBB\Logs\Software_Installations\7-Zip_24.08_PSAppDeployToolkit_Install_2025-01-08_15-30-45.log`

### NBB User Installations
**Path:** `%LocalAppData%\NBB\Logs\Software_Installations`
- User context installations
- Per-user application logs
- Same structure as system installations
- Example: `C:\Users\JohnDoe\AppData\Local\NBB\Logs\Software_Installations\Notepad++_8.6_Install_2025-01-08_10-15-22.log`

---

## Usage

### Local Computer:
1. Leave computer name blank or enter "localhost" or local machine name
2. Click "Get Logs"
3. View logs from all 3 sources:
   - Intune Management Extension
   - NBB System Installations
   - NBB User Installations

### Remote Computer:
1. Enter remote computer name (e.g., "PC-123456")
2. Click "Get Logs"
3. View logs from 2 sources:
   - Intune Management Extension
   - NBB System Installations

**Note:** User-based logs are not accessible remotely due to technical limitations.

### Viewing Logs:
- **📖 View button**: Opens log in CMTrace if available, otherwise Notepad
- **Source column**: Shows which system generated the log
- Logs are sorted by last modified date (newest first)

---

## Benefits

✅ **Comprehensive Troubleshooting**
- See both Intune service logs AND actual installation logs
- Track application installations from start to finish
- Identify issues at any stage

✅ **Context-Aware Logging**
- Distinguish between System and User installs
- Know which framework generated each log (Intune vs NBB/PSADT)
- Better understanding of deployment flow

✅ **Single Pane of Glass**
- No need to navigate multiple UNC paths
- All relevant logs in one view
- Quick access to recent logs

✅ **Remote Support**
- Access logs from remote computers
- No need for Remote Desktop
- Works with standard admin shares

---

## Example Scenarios

### Scenario 1: Application Failed to Install
**Steps:**
1. Get logs for the target computer
2. Filter by Source: "NBB System Installations"
3. Find the application-specific log file
4. View in CMTrace to see detailed error

### Scenario 2: Intune Service Issues
**Steps:**
1. Get logs for the computer
2. Filter by Source: "Intune Management Extension"
3. Look for IntuneManagementExtension.log
4. Check for sync or download errors

### Scenario 3: User vs System Installation
**Steps:**
1. Check local computer logs
2. Compare "NBB System Installations" vs "NBB User Installations"
3. Determine installation context
4. Identify permission or path issues

---

## Technical Details

### Recursive Directory Search
NBB logs are searched recursively because PSADT creates subfolders with timestamps:
```
C:\NBB\Logs\Software_Installations\
├── 7-Zip_24.08_PSAppDeployToolkit_Install_2025-01-08_15-30-45.log
├── AppFolder1\
│   └── App1_Install_2025-01-07.log
└── AppFolder2\
    └── App2_Install_2025-01-06.log
```

### Error Handling
- If a log path doesn't exist, it's silently skipped (no error shown)
- Remote access failures show helpful error messages
- Partial success is allowed (e.g., Intune logs found but not NBB logs)

### Performance
- All log searches happen asynchronously
- UI remains responsive during fetch
- Loading overlay shows progress
- Remote file operations are done efficiently

---

## Future Enhancements

### Possible Improvements:
1. **Log Filtering**: Filter by date, source, or application name
2. **Log Search**: Search across all log files for specific text
3. **User Profile Enumeration**: Access remote user-based logs (complex)
4. **Log Aggregation**: Combine and correlate logs from multiple sources
5. **Real-time Monitoring**: Watch logs for changes (like tail -f)
6. **Export Logs**: Batch download selected logs to local folder

---

## Files Modified

1. ✅ `IntuneDiagnosticsService.cs` - Added NBB log sources
2. ✅ `LogsView.xaml` - Added Source column and updated UI text
3. ✅ `LogFileInfo` model - Added Source property

---

**Date:** 2025-01-08
**Status:** ✅ Complete and tested
