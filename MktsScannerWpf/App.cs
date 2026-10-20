using System;
using System.Windows;

namespace MktsScannerWpf;

public static class App
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        app.Run(new MainWindow());
    }
}
