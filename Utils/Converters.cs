using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LbpArchiveToolkit.Utils
{
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class IconToBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return new SolidColorBrush(Color.FromRgb(25, 19, 43));
            
            var imageSource = values[0] as BitmapSource;
            bool isLocked = values[1] is bool b && b;

            if (imageSource == null) return new SolidColorBrush(Color.FromRgb(25, 19, 43));

            if (isLocked)
            {
                var grayscale = new FormatConvertedBitmap(imageSource, PixelFormats.Gray8, null, 0);
                grayscale.Freeze();
                var grayBrush = new ImageBrush(grayscale) { Stretch = Stretch.UniformToFill };
                grayBrush.Freeze();
                return grayBrush;
            }

            var brush = new ImageBrush(imageSource) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}