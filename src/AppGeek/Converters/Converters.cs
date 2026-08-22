using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AppGeek.Models;

namespace AppGeek.Converters;

/// <summary>Hex string ("#2E78D8") to a brush, used for the generated app tiles.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value as string;
        if (string.IsNullOrWhiteSpace(hex)) hex = "#2E78D8";

        if (Cache.TryGetValue(hex, out var cached)) return cached;

        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            brush.Freeze();
            Cache[hex] = brush;
            return brush;
        }
        catch
        {
            return Brushes.SteelBlue;
        }
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var flag = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not bool b || !b;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is not bool b || !b;
}

/// <summary>Shows an element only when the bound string has content.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var has = !string.IsNullOrWhiteSpace(value as string);
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>True when the bound value equals the parameter — used for filter chips.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) =>
        value is bool b && b ? parameter ?? Binding.DoNothing : Binding.DoNothing;
}

/// <summary>Zero means "show the empty state".</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var count = value is int i ? i : 0;
        var visibleWhenEmpty = parameter is string s && s.Equals("empty", StringComparison.OrdinalIgnoreCase);
        var show = visibleWhenEmpty ? count == 0 : count > 0;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Colours the run status dot.</summary>
public sealed class RunStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Ok = Freeze("#123021");
    private static readonly SolidColorBrush Running = Freeze("#12233D");
    private static readonly SolidColorBrush Waiting = Freeze("#1A2233");
    private static readonly SolidColorBrush Failed = Freeze("#3A1512");

    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        RunItemState.Succeeded => Ok,
        RunItemState.Running => Running,
        RunItemState.Failed => Failed,
        _ => Waiting
    };

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;

    private static SolidColorBrush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }
}

public sealed class RunStateToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush Ok = Freeze("#5ED18A");
    private static readonly SolidColorBrush Running = Freeze("#6BA8F0");
    private static readonly SolidColorBrush Waiting = Freeze("#69738A");
    private static readonly SolidColorBrush Failed = Freeze("#E08D8A");

    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        RunItemState.Succeeded => Ok,
        RunItemState.Running => Running,
        RunItemState.Failed => Failed,
        _ => Waiting
    };

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;

    private static SolidColorBrush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }
}

public sealed class RunStateToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        RunItemState.Succeeded => "✓",
        RunItemState.Running => "↻",
        RunItemState.Failed => "✕",
        RunItemState.Skipped => "!",
        RunItemState.Cancelled => "―",
        _ => "·"
    };

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Activity entries get the same treatment.</summary>
public sealed class ActivityKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var hex = value switch
        {
            ActivityKind.Success => "#123021",
            ActivityKind.Warning => "#2A2410",
            ActivityKind.Failure => "#3A1512",
            _ => "#1A2233"
        };
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Multi-binding version of <see cref="EqualsConverter"/> for chip selection.</summary>
public sealed class MultiEqualsConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        return string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
