using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AppGeek.Services;

namespace AppGeek.Views;

/// <summary>
/// The standard TechyGeeksHome About dialog, rebuilt for WPF.
///
/// The shared TechyGeeksHome.Common version in the PDFGeek repo is Avalonia-only, so it
/// cannot be referenced from a WPF app. What is matched here is the design, not the code:
/// same layout, same four link buttons, same Ko-fi button, same grid of the rest of the
/// range, same inline update check.
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>
    /// The organisation page. It doubles as a filler tile so the range grid is always even.
    /// </summary>
    private const string GitHubProfileUrl = "https://github.com/techygeekshome";

    /// <summary>One tile in the range grid: a name, and where the name goes.</summary>
    private sealed record FamilyTile(string Name, string Url);

    public AboutWindow()
    {
        InitializeComponent();

        VersionText.Text = AppInfo.VersionLine;
        TaglineText.Text = AppInfo.Tagline;
        DescriptionText.Text = AppInfo.Description;
        LicenceText.Text = AppInfo.LicenceLine;
        LicenceNoticeText.Text = AppInfo.LicenceNotice;
        LicenceLinkText.Text = AppInfo.LicenceName;
        LicenceLinkText.Tag = AppInfo.LicenceUrl;

        BuildFamilyGrid();
        GitHubProfileButton.Click += (_, _) => AppInfo.OpenUrl(GitHubProfileUrl);
        FamilyHubLink.Tag = Family.HubUrl;

        WebsiteButton.Click += (_, _) => AppInfo.OpenUrl(AppInfo.WebsiteUrl);
        ProductButton.Click += (_, _) => AppInfo.OpenUrl(AppInfo.ProductUrl);
        SourceButton.Click += (_, _) => AppInfo.OpenUrl(AppInfo.SourceUrl);
        IssuesButton.Click += (_, _) => AppInfo.OpenUrl(AppInfo.IssuesUrl);
        DonateButton.Click += (_, _) => AppInfo.OpenUrl(AppInfo.DonateUrl);

        CloseButton.Click += (_, _) => Close();
        CheckUpdatesButton.Click += CheckUpdates_Click;

        SourceInitialized += (_, _) => WindowTheme.ApplyDarkTitleBar(this);
    }

    /// <summary>
    /// Fills the two-column grid with one button per app, AppGeek filtered out so the list
    /// never advertises the app you are already in. Each button opens that app's page on the
    /// website rather than its repository - a visitor wants the product, not the source.
    ///
    /// An odd number of apps would leave a hole in the second column, so the GitHub profile
    /// goes in as an extra tile and the full-width button below is hidden. With an even
    /// number the grid is already square and that button stays.
    /// </summary>
    private void BuildFamilyGrid()
    {
        var tiles = Family.Others("AppGeek")
                          .Select(a => new FamilyTile(a.Name, a.ProductUrl))
                          .ToList();

        if (tiles.Count % 2 == 1)
        {
            tiles.Add(new FamilyTile("All our code on GitHub", GitHubProfileUrl));
            GitHubProfileButton.Visibility = Visibility.Collapsed;
        }

        FamilyList.ItemsSource = tiles;
    }

    private void FamilyTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
            AppInfo.OpenUrl(url);
    }

    private void Credit_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
            AppInfo.OpenUrl(url);
    }

    /// <summary>
    /// async void is unavoidable on an event handler, so nothing may be allowed to escape it.
    /// </summary>
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckUpdatesButton.IsEnabled = false;
            StatusText.Text = "Checking…";

            var result = await UpdateChecker.CheckAsync();
            StatusText.Text = result.Message;

            if (!result.UpdateAvailable || result.ReleaseUrl is null) return;

            var answer = MessageBox.Show(
                result.Message + "\n\nOpen the release page? AppGeek will not download or " +
                "install anything by itself.",
                "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
                AppInfo.OpenUrl(result.ReleaseUrl);
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed from the About window", ex);
            StatusText.Text = "The update check could not be completed.";
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
