using System.Windows;

namespace Artiseal.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Installer switches: add or remove the right-click entry without opening the window.
        if (e.Args is ["--install-menu"])
        {
            ExplorerMenu.Install();
            Shutdown();

            return;
        }
        if (e.Args is ["--remove-menu"])
        {
            ExplorerMenu.Uninstall();
            Shutdown();

            return;
        }

        new MainWindow(e.Args.FirstOrDefault()).Show();
    }
}
