using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace BatteryAging.Core
{
    public class EnumDisplayItem<T>
    {
        public T Value { get; }
        public string Display { get; }
        public string DisplayName => Display;  // 别名，方便 XAML 绑定
        public EnumDisplayItem(T value, string display) { Value = value; Display = display; }
        public override string ToString() => Display;
    }

    public static class EnumHelper
    {
        public static Func<bool> IsEnglish { get; set; } = () => false;
        public static string GetDescription(Enum value)
        {
            if (IsEnglish())
                return value.ToString();
            var field = value.GetType().GetField(value.ToString());
            var attr = field?.GetCustomAttribute<DescriptionAttribute>();
            return attr?.Description ?? value.ToString();
        }

        public static List<EnumDisplayItem<T>> GetItems<T>() where T : Enum
        {
            return Enum.GetValues(typeof(T))
                .Cast<T>()
                .Select(v => new EnumDisplayItem<T>(v, GetDescription(v)))
                .ToList();
        }
    }
}
