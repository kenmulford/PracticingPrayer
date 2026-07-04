using System.Globalization;

namespace PrayerApp.Converters;

/// <summary>
/// Binds a single enum-valued property to a group of mutually-exclusive
/// <see cref="Microsoft.Maui.Controls.RadioButton"/> controls (the app's existing
/// single-select pattern — see PrayerTimeBoxScopePage.xaml's GroupName RadioButtons).
/// <c>ConverterParameter</c> is the enum member's name as a string (XAML converter
/// parameters are always strings unless passed via <c>x:Static</c>, which this codebase
/// has no precedent for). <c>ConvertBack</c> only ever produces the parsed enum value:
/// selecting a RadioButton sets the bound enum to that value; MAUI's RadioButton
/// GroupName machinery handles unchecking sibling buttons, so a "false" ConvertBack for
/// the deselected radios is never needed.
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is string paramName
           && value.ToString() == paramName;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string paramName && targetType.IsEnum
           && Enum.TryParse(targetType, paramName, out var parsed)
            ? parsed
            : BindableProperty.UnsetValue;
}
