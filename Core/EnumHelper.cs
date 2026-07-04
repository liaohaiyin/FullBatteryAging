using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace BatteryAging.Core
{
    /// <summary>枚举值 + 其显示文本的配对，供 ComboBox 等控件绑定（SelectedValuePath=Value, DisplayMemberPath=Display）</summary>
    public class EnumDisplayItem<T>
    {
        public T Value { get; }
        public string Display { get; }
        public string DisplayName => Display;  // 别名，方便 XAML 绑定
        public EnumDisplayItem(T value, string display) { Value = value; Display = display; }
        public override string ToString() => Display;
    }

    /// <summary>枚举 [Description] 特性辅助类，统一处理中英文切换显示</summary>
    public static class EnumHelper
    {
        /// <summary>由 App 启动时注入，返回当前 UI 语言是否为英文；决定 GetDescription 是否读取 [Description]</summary>
        public static Func<bool> IsEnglish { get; set; } = () => false;

        /// <summary>取枚举值的显示文本：英文界面直接用成员名，中文界面读 [Description] 特性（缺失则回退成员名）</summary>
        public static string GetDescription(Enum value)
        {
            if (IsEnglish())
                return value.ToString();
            var field = value.GetType().GetField(value.ToString());
            var attr = field?.GetCustomAttribute<DescriptionAttribute>();
            return attr?.Description ?? value.ToString();
        }

        /// <summary>枚举全部成员 + 显示文本，用于填充 ComboBox 等选择控件</summary>
        public static List<EnumDisplayItem<T>> GetItems<T>() where T : Enum
        {
            return Enum.GetValues(typeof(T))
                .Cast<T>()
                .Select(v => new EnumDisplayItem<T>(v, GetDescription(v)))
                .ToList();
        }
    }
}
