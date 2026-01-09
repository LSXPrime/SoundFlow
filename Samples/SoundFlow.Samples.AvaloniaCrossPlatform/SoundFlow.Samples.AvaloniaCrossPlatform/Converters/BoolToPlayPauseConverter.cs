using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SoundFlow.Samples.AvaloniaCrossPlatform.Converters;

public class BoolToPlayPauseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPlaying && isPlaying)
            return "⏸"; // Pause icon
        return "▶";   // Play icon
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}