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

        var backgroundScan = e.Args.Any(a =>
            string.Equals(a, Services.ScanSchedule.ScanArgument, StringComparison.OrdinalIgnoreCase));

        Log.Info($"AppGeek starting · elevated={Elevation.IsElevated} · user={Elevation.CurrentUserName}" +
                 (backgroundScan ? " · background scan" : ""));

        Shell = new ShellViewModel();

        if (backgroundScan)
        {
            // Nothing is on screen yet, and the toast is a window in its own right. Without
            // this, WPF would shut the process down the moment that toast closed — including
            // out from under a user who was about to click it.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunBackgroundScanAsync();
            return;
        }

        var window = new MainWindow { DataContext = Shell };
        MainWindow = window;
        window.Show();

        // A first scan on launch means the dashboard is never empty when the user looks at it.
        if (Shell.Settings.Current.FirstRunComplete)
            _ = Shell.ScanAsync();
    }

    /// <summary>
    /// What the Windows scheduled task runs. No window, no install — it scans, writes the
    /// result to the log and the activity list, and tells the user if there is anything to
    /// look at. Opening the notification is the only thing that brings the app on screen.
    ///
    /// "Nothing installs itself" is the rule this path exists to respect. If it ever needs a
    /// reason to run an install, the answer is no.
    /// </summary>
    private async Task RunBackgroundScanAsync()
    {
        var exitCode = 0;

        try
        {
            await Shell.ScanAsync();

            var count = Shell.Updates.Count;
            Log.Info($"Background scan finished · {count} update(s) available.");

            if (count > 0 && Shell.Settings.Current.NotifyOnUpdates)
            {
                var names = string.Join(", ", Shell.Updates.Take(3).Select(u => u.Name));
                if (count > 3) names += $" and {count - 3} more";

                Views.ToastWindow.Show(
                    count == 1 ? "1 update is available" : $"{count} updates are available",
                    names + "\n\nClick here to review them. Nothing has been installed.",
                    OpenOnUpdates);

                // Leave the toast time to be read, and clicked.
                await Task.Delay(TimeSpan.FromSeconds(25));
                if (MainWindow is null) Shutdown(0);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Background scan failed", ex);
            exitCode = 1;
        }

        Shutdown(exitCode);
    }

    /// <summary>Brings the full app up on the Updates screen when a notification is clicked.</summary>
    private void OpenOnUpdates()
    {
        try
        {
            var window = new MainWindow { DataContext = Shell };
            MainWindow = window;
            Shell.Navigate("updates");
            window.Show();
            window.Activate();
        }
        catch (Exception ex) { Log.Error("Could not open AppGeek from the notification", ex); }
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
