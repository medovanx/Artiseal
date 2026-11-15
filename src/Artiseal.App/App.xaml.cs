using System.Windows;

namespace Artiseal.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Installer switches: add or remove the right-click entry without opening the window.
        // They ask for admin rights themselves when needed; exit code 1 means it wasn't changed.
        if (e.Args is ["--install-menu"] or ["--remove-menu"])
        {
            bool done;

            try
            {
                done = ExplorerMenu.Set(e.Args[0] == "--install-menu");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                done = false;
            }
            Shutdown(done ? 0 : 1);

            return;
        }

        new MainWindow(e.Args.FirstOrDefault()).Show();
    }
}
