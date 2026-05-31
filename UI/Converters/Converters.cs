using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BatteryAging.Core.Enums;

namespace BatteryAging.UI.Converters
{
    /// <summary>Enum → DescriptionAttribute 文本</summary>
    public class EnumDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Enum e)
            {
                var field = e.GetType().GetField(e.ToString());
                var attr = field?.GetCustomAttribute<DescriptionAttribute>();
                return attr?.Description ?? e.ToString();
            }
            return value?.ToString();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>通道状态 → 背景色</summary>
    public class ChannelStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ChannelStatus s)
            {
                return s switch
                {
                    ChannelStatus.Idle => new SolidColorBrush(Color.FromRgb(0x55, 0x6E, 0x8A)),   // 灰蓝
                    ChannelStatus.Running => new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53)),   // 亮绿
                    ChannelStatus.Paused => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),   // 琥珀
                    ChannelStatus.Completed => new SolidColorBrush(Color.FromRgb(0x00, 0xB0, 0xFF)),   // 亮蓝
                    ChannelStatus.Stopped => new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C)),   // 蓝灰
                    ChannelStatus.Error => new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)),   // 鲜红
                    ChannelStatus.Protected => new SolidColorBrush(Color.FromRgb(0xFF, 0x1F, 0x4D)),   // 警示红
                    _ => new SolidColorBrush(Color.FromRgb(0x55, 0x6E, 0x8A))
                };
            }
            return new SolidColorBrush(Color.FromRgb(0x55, 0x6E, 0x8A));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>工步类型 → 颜色（用于实时显示）</summary>
    public class StepTypeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StepType t)
            {
                return t switch
                {
                    StepType.CC_Charge => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    StepType.CV_Charge => new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)),
                    StepType.CCCV_Charge => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                    StepType.CC_Discharge => new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
                    StepType.CP_Charge => new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47)),
                    StepType.CP_Discharge => new SolidColorBrush(Color.FromRgb(0xD8, 0x43, 0x15)),
                    StepType.CR_Discharge => new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00)),
                    StepType.Pulse => new SolidColorBrush(Color.FromRgb(0x00, 0xAC, 0xC1)),
                    StepType.Rest => new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE)),
                    StepType.Loop => new SolidColorBrush(Color.FromRgb(0x7E, 0x57, 0xC2)),
                    StepType.End => new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42)),
                    _ => Brushes.Gray
                };
            }
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>电流值 → 显示带正负号字符串</summary>
    public class CurrentSignFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                if (Math.Abs(d) < 0.0001) return "0.000 A";
                return d > 0 ? $"+{d:F3} A" : $"{d:F3} A";
            }
            return "-";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>布尔反转</summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>布尔 → 显示/隐藏（反向）</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v != Visibility.Visible;
    }

    /// <summary>StepType == 指定类型 → Visibility</summary>
    public class StepTypeEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StepType actual && parameter is string targets)
            {
                foreach (var p in targets.Split(','))
                {
                    if (Enum.TryParse<StepType>(p.Trim(), out var t) && t == actual)
                        return Visibility.Visible;
                }
                return Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>SOH (0~1) → 百分比字符串（"95.3"），用于 Run.Text 绑定</summary>
    public class SohPercentConverter : IValueConverter
    {
        public static readonly SohPercentConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return (d * 100).ToString("F1");
            return "0.0";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>直方图柱高度：count × 6 像素，最大 240，最小 2</summary>
    public class HistogramHeightConverter : IValueConverter
    {
        public static readonly HistogramHeightConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int n)
                return (double)Math.Max(2, Math.Min(240, n * 12));
            return 2.0;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>(cabinetIndex, localChannelIndex) → "1-1" 显示</summary>
    public class ChannelLabelConverter : IMultiValueConverter
    {
        public static readonly ChannelLabelConverter Instance = new();
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return "?";
            var cab = values[0]?.ToString() ?? "?";
            var ch = values[1]?.ToString() ?? "?";
            return $"{cab}-{ch}";
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    /// <summary>SOC (0~100) → 高度，ConverterParameter = 最大高度（像素）</summary>
    public class SocToHeightConverter : IValueConverter
    {
        public static readonly SocToHeightConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double soc = value is double d ? d : 0;
            soc = Math.Clamp(soc, 0, 100);
            double max = 80;
            if (parameter is string s &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                max = m;
            return soc / 100.0 * max;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>SOC → 液位颜色（绿/琥珀/红）</summary>
    public class SocToBrushConverter : IValueConverter
    {
        public static readonly SocToBrushConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double soc = value is double d ? d : 0;
            if (soc >= 60) return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            if (soc >= 25) return new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
            return new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}