using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MovieTime.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolVal = value switch
        {
            bool b => b,
            _ => false
        };

        if (Invert)
        {
            boolVal = !boolVal;
        }

        return boolVal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility vis)
        {
            var res = vis == Visibility.Visible;
            return Invert ? !res : res;
        }
        return false;
    }
}
