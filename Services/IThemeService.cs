using System;
using System.Collections.Generic;

namespace BatteryAging.Services
{
    public class ThemeInfo
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }
    }

    public interface IThemeService
    {
        string CurrentTheme { get; }
        List<ThemeInfo> GetAvailableThemes();
        void ApplyTheme(string themeCode);   // 应用 + 持久化
        event EventHandler ThemeChanged;
    }
}