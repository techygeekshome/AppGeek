namespace AppGeek.ViewModels;

public sealed class NavItemViewModel : ObservableObject
{
    public NavItemViewModel(string key, string title, string glyph, bool isGroupHeader = false)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        IsGroupHeader = isGroupHeader;
    }

    public string Key { get; }
    public string Title { get; }
    public string Glyph { get; }
    public bool IsGroupHeader { get; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private string? _badge;
    public string? Badge { get => _badge; set { if (Set(ref _badge, value)) Raise(nameof(HasBadge)); } }

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    private bool _badgeIsAccent = true;
    public bool BadgeIsAccent { get => _badgeIsAccent; set => Set(ref _badgeIsAccent, value); }
}
