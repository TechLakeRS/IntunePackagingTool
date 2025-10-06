# 🚀 Intune Packaging Tool

<div align="center">

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D4?style=for-the-badge&logo=windows)
![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)
![Intune](https://img.shields.io/badge/Microsoft-Intune-00A4EF?style=for-the-badge&logo=microsoft)

**A powerful desktop application for creating, packaging, and deploying Win32 applications to Microsoft Intune**

[Features](#-features) • [Installation](#-installation) • [Quick Start](#-quick-start) • [Documentation](#-documentation)

</div>

---

## 📋 Table of Contents

- [Overview](#-overview)
- [Features](#-features)
- [Prerequisites](#-prerequisites)
- [Installation](#-installation)
- [Configuration](#-configuration)
- [Quick Start](#-quick-start)
- [Usage Guide](#-usage-guide)
- [Advanced Features](#-advanced-features)
- [Troubleshooting](#-troubleshooting)
- [Contributing](#-contributing)
- [License](#-license)

---

## 🎯 Overview

The **Intune Packaging Tool** streamlines the entire process of preparing, packaging, and deploying Win32 applications to Microsoft Intune. Built for IT administrators and deployment engineers, it combines automation with a user-friendly interface to eliminate manual tasks and reduce deployment time.

### What It Does

✅ **Automates** PSADT (PowerShell App Deployment Toolkit) package creation
✅ **Extracts** metadata from MSI, EXE, and existing PSADT packages
✅ **Creates** .intunewin packages ready for Intune
✅ **Uploads** directly to Microsoft Intune via Graph API
✅ **Manages** detection rules, assignments, and app categories
✅ **Tests** deployments remotely before production
✅ **Generates** WDAC catalogs for application whitelisting

---

## ✨ Features

### 📦 Package Creation

<table>
<tr>
<td width="50%">

#### 🎨 Smart Metadata Extraction
- Auto-detects MSI product codes & versions
- Extracts EXE file version information
- Parses existing PSADT scripts
- Intelligent filename-based fallback

</td>
<td width="50%">

#### 🛠️ PSADT Integration
- 50+ pre-configured deployment options
- Install/Uninstall templates
- User vs System context
- Custom script injection

</td>
</tr>
<tr>
<td>

#### 🔍 Detection Rules
- MSI product code detection
- File/folder existence checks
- Registry key validation
- Version comparison logic

</td>
<td>

#### 📤 Direct Intune Upload
- Microsoft Graph API integration
- Certificate-based authentication
- Automatic group assignments
- Category management

</td>
</tr>
</table>

### 🎛️ Management Features

| Feature | Description |
|---------|-------------|
| 📊 **Application Browser** | View, search, and filter all Intune applications |
| 👥 **Group Management** | Manage device groups and assignments |
| 📈 **Installation Reports** | Track deployment status across devices |
| 🔐 **WDAC Catalog Creation** | Generate security catalogs for code signing |
| 🧪 **Remote Testing** | Deploy and test on remote devices via PowerShell |
| 📝 **Diagnostics** | Collect Intune Management Extension logs |

---

## 📋 Prerequisites

### System Requirements

- **OS:** Windows 10/11 or Windows Server 2019+
- **.NET:** .NET 9.0 Runtime
- **RAM:** 4GB minimum (8GB recommended)
- **Disk:** 500MB free space

### Required Permissions

#### Azure AD App Registration
You need an **Azure AD App Registration** with:

```
API Permissions (Application):
├── DeviceManagementApps.ReadWrite.All
├── DeviceManagementManagedDevices.Read.All
├── Group.ReadWrite.All
└── Directory.Read.All
```

#### Certificate Authentication
- Client certificate (`.pfx`) installed in `CurrentUser\My` or `LocalMachine\My`
- Certificate uploaded to Azure AD App Registration

### Optional Tools

- **IntuneWinAppUtil.exe** - For creating .intunewin packages (automatically used if available)
- **Code Signing Certificate** - For signing WDAC catalogs
- **PSExec** - For Hyper-V based WDAC generation

---

## 🔧 Installation

### Option 1: Download Release (Recommended)

1. Download the latest release from [Releases](https://github.com/TechLakeRS/IntunePackagingTool/releases)
2. Extract to `C:\Program Files\IntunePackagingTool\`
3. Run `IntunePackagingTool.exe`

### Option 2: Build from Source

```bash
# Clone the repository
git clone https://github.com/TechLakeRS/IntunePackagingTool.git
cd IntunePackagingTool

# Restore dependencies
dotnet restore

# Build the project
dotnet build --configuration Release

# Run the application
dotnet run --project IntunePackagingTool
```

---

## ⚙️ Configuration

### First-Time Setup

1. **Copy the settings template:**
   ```bash
   copy appsettings.Template.json IntunePackagingTool\appsettings.json
   ```

2. **Edit `appsettings.json` with your credentials:**

```json
{
  "Authentication": {
    "TenantId": "your-tenant-id-guid",
    "ClientId": "your-app-registration-client-id",
    "CertificateThumbprint": "YOUR-CERT-THUMBPRINT-HERE"
  },
  "NetworkPaths": {
    "IntuneApplications": "\\\\server\\share\\IntuneApplications",
    "IntuneWinAppUtil": "\\\\server\\share\\Tools\\IntuneWinAppUtil.exe",
    "PSADTTemplate": "\\\\server\\share\\PSADT\\Template"
  },
  "CodeSigning": {
    "CertificateName": "Your Code Signing Cert",
    "CertificateThumbprint": "YOUR-SIGNING-CERT-THUMBPRINT",
    "TimestampServer": "http://timestamp.digicert.com"
  }
}
```

3. **Install your authentication certificate:**
   - Double-click the `.pfx` file
   - Import to `Current User > Personal > Certificates`
   - Note the thumbprint (copy from certificate details)

4. **Configure network paths** (optional):
   - Update paths to match your environment
   - Use UNC paths for network shares

---

## 🚀 Quick Start

### Creating Your First Package

#### Step 1: Launch the Application
```bash
.\IntunePackagingTool.exe
```

#### Step 2: Create New Package

1. Click **"Create Application"** tab
2. Click **"Browse"** and select your installer (`.msi` or `.exe`)
3. Metadata is **automatically extracted** ✨

   ```
   Application Name:  [Auto-filled]
   Manufacturer:      [Auto-filled]
   Version:           [Auto-filled]
   ```

4. *(Optional)* Click **"Configure PSADT Options"** for advanced deployment settings
5. Click **"Generate Package"**

#### Step 3: Upload to Intune

1. Navigate to **"Upload Existing"** tab
2. Select your generated package folder
3. Click **"Open Upload Wizard"**
4. Review and configure:
   - ✅ Detection rules (auto-configured for MSI)
   - ✅ Installation commands
   - ✅ App information
5. Click **"Upload to Intune"**

**That's it!** 🎉 Your application is now in Intune.

---

## 📖 Usage Guide

### Creating Packages

#### For MSI Installers

```
1. Browse to .msi file
2. Tool auto-extracts:
   ✓ Product Name
   ✓ Manufacturer
   ✓ Version
   ✓ Product Code (for detection)
3. Generate Package
4. Upload to Intune
```

**Detection Rule:** Automatically uses MSI Product Code ✅

#### For EXE Installers

```
1. Browse to .exe file
2. Tool extracts file version info
3. Configure install/uninstall commands:
   - Install: setup.exe /S
   - Uninstall: uninstall.exe /S
4. Add manual detection rule:
   - File: %ProgramFiles%\AppName\app.exe
   - Version: 1.0.0 or higher
5. Generate Package
```

#### For Existing PSADT Packages

```
1. Select "Upload Existing" tab
2. Browse to PSADT folder containing:
   - Deploy-Application.ps1
   - Files directory
3. Tool extracts metadata from script
4. Upload directly to Intune
```

---

### Managing Applications

#### View All Applications

1. Navigate to **"View Applications"** tab
2. Applications load automatically from Intune
3. **Search** by name or publisher
4. **Filter** by category
5. Click any app to view:
   - 📊 Installation statistics
   - 🔍 Detection rules
   - 👥 Group assignments
   - 📝 App details

#### Application Details

Click **"View Details"** to see:

<table>
<tr>
<td width="33%">

**📋 Information**
- Name & Version
- Publisher
- Description
- Install Context
- File Size

</td>
<td width="33%">

**🎯 Detection**
- Detection Rules
- MSI Product Code
- File/Registry Checks
- Version Logic

</td>
<td width="33%">

**👥 Assignments**
- Required Groups
- Available Groups
- Uninstall Groups
- Installation Stats

</td>
</tr>
</table>

---

### Group Management

#### Add Devices to Groups

1. Click on an application
2. Click **"Group Assignments"**
3. Select target group
4. Click **"Add Device"**
5. Enter device name
6. Device is added to group ✅

#### View Group Members

1. Select a group
2. Click **"View Members"**
3. See all devices:
   - Device Name
   - User
   - OS Version
   - Last Sync
   - Compliance Status

---

## 🔥 Advanced Features

### PSADT Cheatsheet Options

Configure advanced deployment behaviors:

#### 🛡️ **Installation Options**
- ✅ Close running processes automatically
- ✅ Defer installations (X times)
- ✅ Require disk space check
- ✅ Block execution on battery
- ✅ Allow deployment from terminal server

#### 🎨 **UI Customization**
- Custom branding images
- Company logo
- Installation messages
- Progress dialogs

#### 📝 **Logging**
- Verbose logging
- Custom log location
- Append to existing logs

#### 🔄 **Advanced**
- Run as user context
- Install for all users
- Custom exit codes
- Pre/Post install scripts

---

### WDAC Catalog Creation

**Windows Defender Application Control (WDAC)** catalogs whitelist your applications.

#### Using Hyper-V Method

1. Navigate to **"Tools" → "WDAC Catalog"**
2. Select your package folder
3. Configure Hyper-V settings:
   ```
   Host: HyperV-Host-01
   VM: WDAC-Builder-VM
   Snapshot: Clean-Snapshot
   ```
4. Click **"Create Catalog"**
5. Tool will:
   - ✅ Copy files to VM
   - ✅ Generate catalog
   - ✅ Sign with certificate
   - ✅ Return catalog file

#### Manual Method

1. Select package folder
2. Click **"Create Catalog (Local)"**
3. Review generated `.cat` file
4. Sign with code signing certificate

---

### Remote Testing

Test deployments before production:

1. Go to **"Tools" → "Remote Testing"**
2. Enter target computer name
3. Select package to deploy
4. Choose deployment type:
   - 🔹 Install
   - 🔹 Uninstall
   - 🔹 Repair
5. Click **"Deploy"**
6. Monitor real-time progress
7. Review installation logs

---

### Diagnostics & Logs

#### Collect Intune Logs

1. Navigate to **"Tools" → "Diagnostics"**
2. Enter device name
3. Click **"Collect Logs"**
4. Tool fetches:
   - 📄 IntuneManagementExtension.log
   - 📄 AgentExecutor.log
   - 📄 Application installation logs
5. View/Export logs

#### View Application Status

```
✅ Installed: 245 devices
⚠️ Failed: 3 devices
⏳ Pending: 12 devices
❌ Not Installed: 45 devices
```

Click any status to see device details.

---

## 🐛 Troubleshooting

### Common Issues

#### ❌ Authentication Failed

**Error:** `Certificate with thumbprint XXX not found`

**Solution:**
1. Verify certificate is installed:
   ```powershell
   Get-ChildItem Cert:\CurrentUser\My | Select-Object Thumbprint, Subject
   ```
2. Copy exact thumbprint to `appsettings.json`
3. Remove all spaces from thumbprint

---

#### ❌ Upload Failed

**Error:** `Failed to create application: Forbidden`

**Solution:**
1. Check Azure AD App permissions:
   - DeviceManagementApps.ReadWrite.All
   - Grant admin consent
2. Verify certificate is uploaded to App Registration
3. Check token expiry

---

#### ❌ Package Generation Failed

**Error:** `IntuneWinAppUtil.exe not found`

**Solution:**
1. Download IntuneWinAppUtil from Microsoft
2. Update `NetworkPaths.IntuneWinAppUtil` in settings
3. Or place in application directory

---

#### ❌ Detection Rule Not Working

**Problem:** App shows as "Not Installed" even after installation

**Solution:**
1. Verify detection rule:
   - For MSI: Use Product Code (auto-detected)
   - For EXE: Check file path exists after install
2. Test detection manually on target device:
   ```powershell
   # For MSI
   Get-WmiObject -Class Win32_Product | Where-Object {$_.IdentifyingNumber -eq "{PRODUCT-CODE}"}

   # For File
   Test-Path "C:\Program Files\AppName\app.exe"
   ```

---

## 🏗️ Architecture

```
IntunePackagingTool/
├── 📁 Helpers/              # Helper classes for reusable logic
│   ├── PackageCreationHelper.cs
│   └── MetadataExtractor.cs
├── 📁 Services/             # Business logic services
│   ├── IntuneService.cs     # Graph API integration
│   ├── PSADTGenerator.cs    # PSADT package creation
│   ├── IntuneUploadService.cs
│   └── MsiInfoService.cs
├── 📁 Models/               # Data models
├── 📁 Views/                # UI views and dialogs
├── 📁 Utilities/            # Utility classes
└── 📁 Scripts/              # PowerShell scripts
```

---

## 🤝 Contributing

Contributions are welcome! Here's how you can help:

### 🐛 Found a Bug?
1. Check [Issues](https://github.com/TechLakeRS/IntunePackagingTool/issues) first
2. Create a new issue with:
   - Description
   - Steps to reproduce
   - Expected vs actual behavior
   - Screenshots (if applicable)

### 💡 Have a Feature Idea?
1. Open a [Feature Request](https://github.com/TechLakeRS/IntunePackagingTool/issues/new)
2. Describe the use case
3. Explain how it helps

### 🔧 Want to Contribute Code?
1. Fork the repository
2. Create a feature branch:
   ```bash
   git checkout -b feature/amazing-feature
   ```
3. Make your changes
4. Follow existing code style
5. Test thoroughly
6. Commit with clear message:
   ```bash
   git commit -m "Add amazing feature for X"
   ```
7. Push and create a Pull Request

---

## 📜 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- **Microsoft Graph API** - For Intune integration
- **PowerShell App Deployment Toolkit (PSADT)** - For deployment framework
- **IntuneWinAppUtil** - For .intunewin package creation

---

## 📞 Support

- 📧 **Email:** [Create an Issue](https://github.com/TechLakeRS/IntunePackagingTool/issues)
- 📖 **Documentation:** [Wiki](https://github.com/TechLakeRS/IntunePackagingTool/wiki)
- 💬 **Discussions:** [GitHub Discussions](https://github.com/TechLakeRS/IntunePackagingTool/discussions)

---

## 🌟 Star History

[![Star History Chart](https://api.star-history.com/svg?repos=TechLakeRS/IntunePackagingTool&type=Date)](https://star-history.com/#TechLakeRS/IntunePackagingTool&Date)

---

<div align="center">

**Made with ❤️ by IT Professionals, for IT Professionals**

⭐ **Star this repo if you find it helpful!** ⭐

[Report Bug](https://github.com/TechLakeRS/IntunePackagingTool/issues) • [Request Feature](https://github.com/TechLakeRS/IntunePackagingTool/issues) • [Documentation](https://github.com/TechLakeRS/IntunePackagingTool/wiki)

</div>
