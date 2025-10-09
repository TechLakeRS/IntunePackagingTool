# Dark Mode Implementation

## Overview
Dark mode has been successfully implemented in the NBB Intune Packaging Tool. The implementation includes:
- **Light Theme** - Default professional light theme
- **Dark Theme** - Modern dark theme with improved contrast
- **System Theme** - Automatically follows Windows system theme preference

## Features

### Theme Toggle Button
- Located in the top-right corner of the main window header
- Shows current theme state with icons:
  - 🌙 Dark (when in Light mode, click to switch to Dark)
  - ☀️ Light (when in Dark mode, click to switch to Light)
  - 🖥️ System (when in System mode, follows Windows settings)

### Persistent Settings
- Theme preference is saved to Windows Registry
- Location: `HKEY_CURRENT_USER\SOFTWARE\NBB\IntunePackagingTool`
- Automatically restored on application startup

### Dynamic Theme Resources
All UI elements use dynamic resource binding for colors:
- Backgrounds, text, borders automatically adapt
- Smooth visual transitions between themes
- Consistent color scheme throughout the application

## Architecture

### Core Components

1. **ThemeService.cs** (`Services/ThemeService.cs`)
   - Singleton service managing theme state
   - Handles theme switching logic
   - Monitors system theme changes
   - Persists settings to registry

2. **Theme Resources**
   - `Themes/LightTheme.xaml` - Light theme color definitions
   - `Themes/DarkTheme.xaml` - Dark theme color definitions
   - Uses WPF DynamicResource for runtime switching

3. **Integration Points**
   - `App.xaml.cs` - Initializes theme on startup
   - `MainWindow.xaml.cs` - Theme toggle button handler
   - `MainWindow.xaml` - UI elements using dynamic resources

## Color Palette

### Light Theme
- Background: `#F5F7FA`
- Surface: `#FFFFFF`
- Primary: `#2196F3`
- Text Primary: `#2C3E50`
- Text Secondary: `#7F8C8D`

### Dark Theme
- Background: `#1E1E1E`
- Surface: `#2D2D30`
- Primary: `#007ACC`
- Text Primary: `#E4E4E4`
- Text Secondary: `#969696`

## Usage

### For Users
1. Click the theme toggle button in the header
2. Theme cycles through: Light → Dark → System → Light
3. Changes apply immediately
4. Setting persists between sessions

### For Developers

#### Adding New Themed Elements
```xml
<!-- Use DynamicResource for theme-aware colors -->
<Border Background="{DynamicResource CardBackgroundBrush}"
        BorderBrush="{DynamicResource BorderBrush}">
    <TextBlock Foreground="{DynamicResource TextPrimaryBrush}"/>
</Border>
```

#### Available Theme Brushes
- **Backgrounds**: `BackgroundBrush`, `SurfaceBrush`, `CardBackgroundBrush`
- **Text**: `TextPrimaryBrush`, `TextSecondaryBrush`, `TextDisabledBrush`
- **Borders**: `BorderBrush`, `BorderLightBrush`, `DividerBrush`
- **Status**: `SuccessBrush`, `WarningBrush`, `ErrorBrush`, `InfoBrush`
- **Buttons**: `ButtonBackgroundBrush`, `ButtonHoverBrush`, `ButtonPressedBrush`

#### Programmatic Theme Change
```csharp
// Get current theme
var currentTheme = ThemeService.Instance.CurrentTheme;

// Set specific theme
ThemeService.Instance.CurrentTheme = AppTheme.Dark;

// Toggle to next theme
ThemeService.Instance.ToggleTheme();

// Listen for theme changes
ThemeService.Instance.ThemeChanged += (sender, theme) => {
    // React to theme change
};
```

## Testing Checklist

✅ Theme toggle button works correctly
✅ Theme persists after application restart
✅ System theme detection works on Windows 10/11
✅ All UI elements update when theme changes
✅ No visual glitches during theme transition
✅ Registry settings are created and updated
✅ Memory is properly managed (no leaks)

## Future Enhancements

Potential improvements for future versions:
1. **Custom Theme Editor** - Allow users to customize colors
2. **High Contrast Mode** - Support Windows high contrast themes
3. **Theme Scheduling** - Automatic switching based on time of day
4. **Per-Page Themes** - Different themes for different sections
5. **Animation Transitions** - Smooth fade between themes

## Troubleshooting

### Theme Not Persisting
- Check registry permissions at `HKEY_CURRENT_USER\SOFTWARE\NBB`
- Verify antivirus isn't blocking registry writes

### System Theme Not Working
- Ensure Windows 10 version 1809 or later
- Check Windows Settings > Personalization > Colors
- Verify "Choose your app mode" is set

### Visual Issues
- Clear WPF resource cache by restarting application
- Check for custom Windows themes that might interfere
- Verify all XAML files are using DynamicResource bindings

---

*Implementation Date: January 2025*
*Version: 1.0.0*