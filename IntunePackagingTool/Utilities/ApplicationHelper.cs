using System.IO;
using System.Linq;

namespace IntunePackagingTool.Utilities;

public static class ApplicationHelper
{
    public static (string Name, string Version) ParseApplicationInfo(string applicationPath)
    {
        var versionFolder = Path.GetFileName(applicationPath);
        var appFolder = Path.GetFileName(Path.GetDirectoryName(applicationPath));

        var parts = appFolder.Split('_');
        string appName;

        if (parts.Length >= 2)
        {
            // Use everything after the first underscore
            appName = string.Join("_", parts.Skip(1));
        }
        else
        {
            appName = appFolder;
        }

        return (appName, versionFolder);
    }
}