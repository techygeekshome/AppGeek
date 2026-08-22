// AppGeek — scan, update and install Windows applications.
// Copyright (C) 2026 TechyGeeksHome
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.

using System.Windows;
using System.Windows.Threading;
using AppGeek.Services;
using AppGeek.ViewModels;

namespace AppGeek;

public partial class App : Application
{
    public static ShellViewModel Shell { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Fatal error", args.ExceptionObject as Exception);

        Log.Info($"AppGeek starting · elevated={Elevation.IsElevated} · user={Elevation.CurrentUserName}");

        Shell = new ShellViewModel();

        var window = new MainWindow { DataContext = Shell };
        MainWindow = window;
        window.Show();

        // A first scan on launch means the dashboard is never empty when the user looks at it.
        if (Shell.Settings.Current.FirstRunComplete)
            _ = Shell.ScanAsync();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);

        MessageBox.Show(
            "Something went wrong inside AppGeek.\n\n" + e.Exception.Message +
            "\n\nThe details have been written to the log.",
            "AppGeek", MessageBoxButton.OK, MessageBoxImage.Error);

        // Keep the app alive: a failed scan or a bad registry key should not close it.
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("AppGeek closing.");
        base.OnExit(e);
    }
}
