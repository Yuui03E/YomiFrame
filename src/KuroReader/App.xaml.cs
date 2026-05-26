using System.Windows;

namespace KuroReader;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Process command-line args: if a file path is passed, open it
        if (e.Args.Length > 0)
        {
            var filePath = string.Join(" ", e.Args);
            if (System.IO.File.Exists(filePath))
            {
                // Store for MainWindow to pick up
                Properties["StartupFile"] = filePath;
            }
        }
    }
}
