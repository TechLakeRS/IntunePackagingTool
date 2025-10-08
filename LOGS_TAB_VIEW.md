# Logs Tab View Enhancement

## Summary
Reorganized the Logs & Diagnostics view to use separate tabs for different log sources instead of a single combined view.

---

## New Tab Structure

### 🔷 Tab 1: Intune Logs
**Purpose:** Intune Management Extension service logs
**Path:** `C:\ProgramData\Microsoft\IntuneManagementExtension\Logs`

**Contains:**
- IntuneManagementExtension.log
- IntuneManagementExtension-*.log (rotated logs)
- AgentExecutor.log
- Other Intune service logs

**Use Case:** Troubleshoot Intune service issues, app sync problems, download failures

---

### 🖥️ Tab 2: System App Logs
**Purpose:** System-context application installations (NBB/PSADT)
**Path:** `C:\NBB\Logs\Software_Installations`

**Contains:**
- Application-specific installation logs
- PSADT detailed deployment logs
- System-level installations

**Use Case:** Troubleshoot system app installations, detect script errors, review installation steps

**Example Logs:**
```
7-Zip_24.08_PSAppDeployToolkit_Install_2025-01-08_15-30-45.log
GoogleChrome_Install_2025-01-08_14-20-12.log
AdobeReader_Uninstall_2025-01-07_10-15-30.log
```

---

### 👤 Tab 3: User App Logs
**Purpose:** User-context application installations (NBB/PSADT)
**Path:** `%LocalAppData%\NBB\Logs\Software_Installations`

**Contains:**
- Per-user application installation logs
- User-level installations
- User-specific PSADT logs

**Use Case:** Troubleshoot user app installations, review user-specific deployment issues

**Note:** ⚠️ Only available for local computer. Remote computers show:
> ℹ️ User logs not available for remote computers
>
> User-based logs can only be viewed on the local computer

---

### 📋 Tab 4: All Logs
**Purpose:** Combined view of all log sources
**Shows:** All logs from Intune, System, and User sources with "Source" column

**Use Case:**
- Overview of all activity
- Cross-reference between Intune and application logs
- Find logs when unsure of source

---

## UI Improvements

### Cleaner Layout
- **Before:** Single grid with "Source" column
- **After:** Separate tabs for each log type
- **Benefit:** Easier to focus on specific log types

### Better Organization
Each tab shows only relevant logs:
- No cluttered Source column (except "All Logs" tab)
- Wider space for file names
- Context-specific viewing

### Smart Empty States
- **User App Logs tab on remote computers:**
  - Shows informative message instead of empty grid
  - Explains why user logs aren't available
  - Prevents confusion

### Enhanced Status Bar
Shows detailed counts after loading:
```
Loaded from COMPUTER-123: 5 Intune log(s), 12 system app log(s), 3 user app log(s)
```

---

## Tab Visibility Logic

### Local Computer:
✅ **Intune Logs** - Shows Intune logs if found
✅ **System App Logs** - Shows system NBB logs if found
✅ **User App Logs** - Shows user NBB logs if found
✅ **All Logs** - Shows all available logs

### Remote Computer:
✅ **Intune Logs** - Shows remote Intune logs via UNC path
✅ **System App Logs** - Shows remote system NBB logs via UNC path
⚠️ **User App Logs** - Shows empty state message (not accessible remotely)
✅ **All Logs** - Shows available logs (Intune + System only)

---

## Technical Details

### Data Binding
```csharp
// Separate ObservableCollections for each tab
private ObservableCollection<LogFileInfo> _allLogs;
private ObservableCollection<LogFileInfo> _intuneLogs;
private ObservableCollection<LogFileInfo> _systemLogs;
private ObservableCollection<LogFileInfo> _userLogs;

// Bound to DataGrids
AllLogsGrid.ItemsSource = _allLogs;
IntuneLogsGrid.ItemsSource = _intuneLogs;
SystemLogsGrid.ItemsSource = _systemLogs;
UserLogsGrid.ItemsSource = _userLogs;
```

### Log Categorization
```csharp
// Categorize logs by Source property
foreach (var file in result.LogFiles)
{
    _allLogs.Add(file); // Always add to "All Logs"

    if (file.Source == "Intune Management Extension")
        _intuneLogs.Add(file);
    else if (file.Source == "NBB System Installations")
        _systemLogs.Add(file);
    else if (file.Source == "NBB User Installations")
        _userLogs.Add(file);
}
```

### Empty State Management
```csharp
// Show empty state for User logs on remote computers
if (!_isLocalComputer && _userLogs.Count == 0)
{
    UserLogsEmptyState.Visibility = Visibility.Visible;
    UserLogsGrid.Visibility = Visibility.Collapsed;
}
```

---

## User Experience Flow

### Scenario 1: Troubleshooting App Installation Failure

1. Enter computer name and click "Get Logs"
2. Go to **🖥️ System App Logs** tab
3. Sort by "Last Modified" to find recent logs
4. Find the failed application log
5. Click **📖 View** to open in CMTrace
6. Review error details

### Scenario 2: Checking Intune Sync Issues

1. Load logs for target computer
2. Go to **🔷 Intune Logs** tab
3. Open IntuneManagementExtension.log
4. Search for sync errors or failures
5. Cross-reference with app logs if needed

### Scenario 3: Comparing System vs User Installation

1. Load logs for local computer
2. Check **🖥️ System App Logs** for system installations
3. Check **👤 User App Logs** for user installations
4. Compare timestamps and results
5. Identify context-specific issues

### Scenario 4: Quick Overview

1. Load logs
2. Go to **📋 All Logs** tab
3. See all logs sorted by date
4. Use "Source" column to identify log type
5. Double-click to view

---

## Column Configuration

### Individual Tabs (Intune/System/User):
| Column | Width | Description |
|--------|-------|-------------|
| File Name | 2* | Log file name |
| Size | 120px | Formatted file size |
| Last Modified | 180px | Date/time last modified |
| Actions | 120px | View button |

### All Logs Tab:
| Column | Width | Description |
|--------|-------|-------------|
| File Name | 2* | Log file name |
| **Source** | 1.5* | Log source type |
| Size | 100px | Formatted file size |
| Last Modified | 180px | Date/time last modified |
| Actions | 120px | View button |

---

## Benefits

### ✅ Improved Organization
- Clear separation of log types
- No visual clutter from Source column in individual tabs
- Faster navigation to relevant logs

### ✅ Better User Experience
- Intuitive tab names with icons
- Context-appropriate information
- Clear empty states

### ✅ Flexibility
- Can view by category or all together
- Easy switching between views
- Preserves all original functionality

### ✅ Professional Look
- Modern tabbed interface
- Consistent with application design
- Clean and organized

---

## Files Modified

1. ✅ **LogsView.xaml**
   - Changed from single DataGrid to 4 tabbed DataGrids
   - Added empty state for User logs on remote computers
   - Removed Source column from individual tabs (kept in All Logs)

2. ✅ **LogsView.xaml.cs**
   - Added separate ObservableCollections for each tab
   - Updated LoadLogs to categorize logs by source
   - Added logic to show/hide User logs empty state
   - Enhanced status bar to show counts per category

---

## Future Enhancements

### Possible Additions:
1. **Badge counts on tabs**: Show number of logs in each tab header
2. **Tab-specific filtering**: Filter by date range or file name within each tab
3. **Quick search**: Search box to filter logs across all tabs
4. **Export per tab**: Export logs from specific tab only
5. **Color coding**: Different colors for errors/warnings in log names
6. **Recently viewed**: Show recently opened logs at top

---

**Date:** 2025-01-08
**Status:** ✅ Complete - Ready for testing
