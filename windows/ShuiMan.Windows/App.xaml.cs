using System.IO;
using System.Windows;

namespace ShuiMan.Windows;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) => { MessageBox.Show(a.Exception.Message, "水漫", MessageBoxButton.OK, MessageBoxImage.Error); a.Handled = true; };
        string? dataDir = null; string? book = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--data-dir" && i + 1 < e.Args.Length) dataDir = e.Args[++i];
            else if (File.Exists(e.Args[i]) || Directory.Exists(e.Args[i])) book = e.Args[i];
        }
        try { new MainWindow(dataDir, book).Show(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "水漫启动失败", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
}
