using Microsoft.Win32;

namespace Artiseal.App;

/// <summary>
/// The per-user "Check with Artiseal" entry on every file's right-click menu. No admin rights needed;
/// on Windows 11 it sits under "Show more options".
/// </summary>
public static class ExplorerMenu
{
    private const string KeyPath = @"Software\Classes\*\shell\Artiseal";

    public static bool IsInstalled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath + @"\command");

        return key?.GetValue(null) is string command && command.Contains(ExePath(), StringComparison.OrdinalIgnoreCase);
    }

    public static void Install()
    {
        string exe = ExePath();

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(null, "Check with Artiseal");
        key.SetValue("Icon", $"\"{exe}\",0");

        using RegistryKey command = key.CreateSubKey("command");
        command.SetValue(null, $"\"{exe}\" \"%1\"");
    }

    public static void Uninstall() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);

    private static string ExePath() => Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
}
