using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AppGeek.Views;

/// <summary>
/// The notification the background scan shows when it finds updates.
///
/// Built in code with no XAML and no packages. WinRT toasts need an installed Start-menu
/// shortcut with an AppUserModelID to appear at all, which a portable build does not have,
/// and every third-party toast library would be the first dependency in the project. A small
/// bottom-right window does the same job everywhere, including on a portable copy run from a
/// USB stick.
///
/// It says what was found. It does not offer to install anything — that decision stays on
/// the Updates screen where the user can see what would change.
/// </summary>
public sealed class ToastWindow : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(20);

    private ToastWindow(string title, string body, Action? onClick)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = 340;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            TextWrapping = TextWrapping.Wrap
        });

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x13)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x27)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = stack
        };

        MouseLeftButtonUp += (_, _) => { onClick?.Invoke(); Close(); };
        Cursor = onClick is null ? Cursors.Arrow : Cursors.Hand;
    }

    /// <summary>Shows the toast bottom-right and closes it again after a few seconds.</summary>
    public static void Show(string title, string body, Action? onClick = null)
    {
        try
        {
            var toast = new ToastWindow(title, body, onClick);
            toast.Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                toast.Left = area.Right - toast.Width - 24;
                toast.Top = area.Bottom - toast.ActualHeight - 24;
            };
            toast.Show();

            var timer = new DispatcherTimer { Interval = Lifetime };
            timer.Tick += (_, _) => { timer.Stop(); try { toast.Close(); } catch { } };
            timer.Start();
        }
        catch (Exception ex)
        {
            Services.Log.Warn("Notification could not be shown: " + ex.Message);
        }
    }
}
