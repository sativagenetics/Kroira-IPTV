using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace Kroira.App.Converters
{
    /// <summary>
    /// Formats an integer (or anything IConvertible to long) with comma
    /// grouping, e.g. 2431 -> "2,431". Used by result-count indicators
    /// across the library screens.
    /// </summary>
    public sealed class NumberFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is null)
            {
                return string.Empty;
            }

            try
            {
                var asLong = System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return asLong.ToString("N0", CultureInfo.InvariantCulture);
            }
            catch
            {
                return value.ToString() ?? string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
