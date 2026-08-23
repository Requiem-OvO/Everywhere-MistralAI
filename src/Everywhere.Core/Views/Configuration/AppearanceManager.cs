using Avalonia.Controls;
using Avalonia.Media;

namespace Everywhere.Views;

public sealed class AppearanceManager : ResourceDictionary
{
    public static AppearanceManager Shared => _shared ?? throw new InvalidOperationException("AppearanceManager is not initialized.");

    private static AppearanceManager? _shared;

    public string? FontFamily
    {
        set
        {
            if (value.IsNullOrEmpty())
            {
                this["CommonFontFamily"] = Avalonia.Media.FontFamily.Default;
            }
            else
            {
                this["CommonFontFamily"] = new FontFamily(value);
            }
        }
    }

    public double FontSize
    {
        set
        {
            var lineHeight = value / 2 * 3;
            SetItems(
            [
                // <system:Double x:Key="FontSizeXs">10</system:Double>
                // <system:Double x:Key="FontSizeS">12.8</system:Double>
                // <system:Double x:Key="FontSizeM">14</system:Double>
                // <system:Double x:Key="FontSizeL">16</system:Double>
                // <system:Double x:Key="FontSizeXl">20</system:Double>
                // <system:Double x:Key="FontSize2Xl">24</system:Double>
                // <system:Double x:Key="FontSize3Xl">30</system:Double>
                // <system:Double x:Key="FontSize4Xl">48</system:Double>
                new KeyValuePair<object, object?>("FontSizeXs", value * 0.714),
                new KeyValuePair<object, object?>("FontSizeS", value * 0.914),
                new KeyValuePair<object, object?>("FontSizeM", value * 1.0),
                new KeyValuePair<object, object?>("FontSizeL", value * 1.142),
                new KeyValuePair<object, object?>("FontSizeXl", value * 1.428),
                new KeyValuePair<object, object?>("FontSize2Xl", value * 1.714),
                new KeyValuePair<object, object?>("FontSize3Xl", value * 2.142),
                new KeyValuePair<object, object?>("FontSize4Xl", value * 3.428),
                new KeyValuePair<object, object?>("LineHeightXs", lineHeight * 0.714),
                new KeyValuePair<object, object?>("LineHeightS", lineHeight * 0.914),
                new KeyValuePair<object, object?>("LineHeightM", lineHeight),
                new KeyValuePair<object, object?>("LineHeightL", lineHeight * 1.142),
                new KeyValuePair<object, object?>("LineHeightXl", lineHeight * 1.428),
                new KeyValuePair<object, object?>("LineHeight2Xl", lineHeight * 1.714),
                new KeyValuePair<object, object?>("LineHeight3Xl", lineHeight * 2.142),
                new KeyValuePair<object, object?>("LineHeight4Xl", lineHeight * 3.428),
            ]);
        }
    }

    public AppearanceManager()
    {
        if (_shared is not null) throw new InvalidOperationException("AppearanceManager is already initialized.");

        _shared = this;
    }
}