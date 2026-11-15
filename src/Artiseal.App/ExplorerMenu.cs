using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace Artiseal.App;

/// <summary>
/// The "Verify with Artiseal" entry on every file's right-click menu. It is written machine-wide, because
/// some Windows 11 builds (26300 at least) leave per-user entries out of the menu. Writing it needs
/// admin rights, so a non-admin app relaunches itself elevated for that one step.
/// </summary>
public static class ExplorerMenu
{
    private const string KeyPath = @"Software\Classes\*\shell\Artiseal";

    public static bool IsInstalled()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KeyPath + @"\command");

        return key?.GetValue(null) is string command && command.Contains(ExePath(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds or removes the entry, asking Windows for admin rights when needed; false if refused.</summary>
    public static bool Set(bool installed)
    {
        if (IsAdmin())
        {
            if (installed)
            {
                Install();
            }
            else
            {
                Uninstall();
            }

            return true;
        }

        try
        {
            using Process? elevated = Process.Start(new ProcessStartInfo(ExePath(), installed ? "--install-menu" : "--remove-menu")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            elevated?.WaitForExit();

            return elevated?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The person said no to the admin prompt.
            return false;
        }
    }

    public static void Install()
    {
        string exe = ExePath();

        using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath);
        key.SetValue(null, "Verify with Artiseal");
        key.SetValue("Icon", $"\"{exe}\",0");

        using RegistryKey command = key.CreateSubKey("command");
        command.SetValue(null, $"\"{exe}\" \"%1\"");

        // Earlier versions wrote a per-user entry; it is dropped so only one remains.
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
    }

    public static void Uninstall()
    {
        Registry.LocalMachine.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
    }

    private static bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static string ExePath() => Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
}
