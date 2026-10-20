using YolanLive.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace YolanLive.WinUI.Converters;

public partial class SearchBadgeConvert : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var room = value as SearchRoomItem;
        var text = parameter?.ToString()?.StartsWith("Platform", StringComparison.Ordinal) == true ? room?.PlatformLabel : room?.StatusLabel;
        return targetType == typeof(Visibility) ? text == null ? Visibility.Collapsed : Visibility.Visible : text;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
