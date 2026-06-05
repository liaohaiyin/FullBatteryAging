using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;

namespace BatteryAging.Services
{
    public class ThemeService : IThemeService
    {
        private readonly IConfiguration _config;
        private string _currentTheme;

        public string CurrentTheme => _currentTheme;
        public event EventHandler ThemeChanged;

        // ── 画刷键 → 颜色键 映射（覆盖 Colors.xaml 中全部 Vs* 与兼容画刷）──
        private static readonly Dictionary<string, string> BrushToColor = new()
        {
            ["VsBgDeepBrush"] = "VsBgDeepColor",
            ["VsBgMidBrush"] = "VsBgMidColor",
            ["VsBgPanelBrush"] = "VsBgPanelColor",
            ["VsBgHeaderBrush"] = "VsBgHeaderColor",
            ["VsBgHoverBrush"] = "VsBgHoverColor",
            ["VsBgActiveBrush"] = "VsBgActiveColor",
            ["VsBgInputBrush"] = "VsBgInputColor",
            ["VsBorderBrush"] = "VsBorderColor",
            ["VsBorderActiveBrush"] = "VsBorderActiveColor",
            ["VsBorderLightBrush"] = "VsBorderLightColor",
            ["VsAccentBrush"] = "VsAccentColor",
            ["VsAccentHoverBrush"] = "VsAccentHoverColor",
            ["VsAccentDarkBrush"] = "VsAccentDarkColor",
            ["VsSuccessBrush"] = "VsSuccessColor",
            ["VsWarningBrush"] = "VsWarningColor",
            ["VsErrorBrush"] = "VsErrorColor",
            ["VsInfoBrush"] = "VsInfoColor",
            ["VsRunningBrush"] = "VsRunningColor",
            ["VsAmberBrush"] = "VsAmberColor",
            ["VsTextPrimaryBrush"] = "VsTextPrimaryColor",
            ["VsTextSecondaryBrush"] = "VsTextSecondaryColor",
            ["VsTextMutedBrush"] = "VsTextMutedColor",
            ["VsTextDisabledBrush"] = "VsTextDisabledColor",
            // 兼容旧键
            ["BgDeepBrush"] = "VsBgDeepColor",
            ["BgMidBrush"] = "VsBgMidColor",
            ["BgCardBrush"] = "VsBgPanelColor",
            ["BgCardHeaderBrush"] = "VsBgHeaderColor",
            ["AccentCyanBrush"] = "VsAccentColor",
            ["AccentBlueBrush"] = "VsAccentHoverColor",
            ["AccentAmberBrush"] = "VsWarningColor",
            ["AccentGreenBrush"] = "VsSuccessColor",
            ["AccentRedBrush"] = "VsErrorColor",
            ["BorderDimBrush"] = "VsBorderColor",
            ["TextPrimaryBrush"] = "VsTextPrimaryColor",
            ["TextSecondaryBrush"] = "VsTextSecondaryColor",
            ["TextMutedBrush"] = "VsTextMutedColor",
            ["HeaderBgBrush"] = "VsBgHeaderColor",
            ["CardHeaderBrush"] = "VsBgHeaderColor",
            ["ChannelCardBrush"] = "VsBgPanelColor",
            ["PrimaryHueMidBrush"] = "VsBgHeaderColor",
            ["PrimaryHueDarkBrush"] = "VsBgHeaderColor",
            ["PrimaryHueLightBrush"] = "VsAccentColor",
            ["MaterialDesignPaper"] = "VsBgMidColor",
            ["MaterialDesignBody"] = "VsTextPrimaryColor",
        };

        // ── 两套调色板（颜色键 → 十六进制）──
        private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
        {
            ["Dark"] = new()
            {
                ["VsBgDeepColor"] = "#FF1E1E1E",
                ["VsBgMidColor"] = "#FF252526",
                ["VsBgPanelColor"] = "#FF2D2D30",
                ["VsBgHeaderColor"] = "#FF333337",
                ["VsBgHoverColor"] = "#FF3E3E40",
                ["VsBgActiveColor"] = "#FF094771",
                ["VsBgInputColor"] = "#FF333337",
                ["VsBorderColor"] = "#FF3F3F46",
                ["VsBorderActiveColor"] = "#FF007ACC",
                ["VsBorderLightColor"] = "#FF555558",
                ["VsAccentColor"] = "#FF007ACC",
                ["VsAccentHoverColor"] = "#FF1C97EA",
                ["VsAccentDarkColor"] = "#FF005A9E",
                ["VsSuccessColor"] = "#FF4EC9B0",
                ["VsWarningColor"] = "#FFD7BA7D",
                ["VsErrorColor"] = "#FFF48771",
                ["VsInfoColor"] = "#FF9CDCFE",
                ["VsRunningColor"] = "#FF608B4E",
                ["VsAmberColor"] = "#FFCE9178",
                ["VsTextPrimaryColor"] = "#FFF1F1F1",
                ["VsTextSecondaryColor"] = "#FFCCCCCC",
                ["VsTextMutedColor"] = "#FF9B9B9B",
                ["VsTextDisabledColor"] = "#FF656565",
            },
            ["Light"] = new()
            {
                ["VsBgDeepColor"] = "#FFFFFFFF",
                ["VsBgMidColor"] = "#FFF5F5F5",
                ["VsBgPanelColor"] = "#FFEEEEEE",
                ["VsBgHeaderColor"] = "#FFE5E5E5",
                ["VsBgHoverColor"] = "#FFDCDCDC",
                ["VsBgActiveColor"] = "#FFCCE8FF",
                ["VsBgInputColor"] = "#FFFFFFFF",
                ["VsBorderColor"] = "#FFC8C8C8",
                ["VsBorderActiveColor"] = "#FF007ACC",
                ["VsBorderLightColor"] = "#FFB0B0B0",
                ["VsAccentColor"] = "#FF007ACC",
                ["VsAccentHoverColor"] = "#FF1C97EA",
                ["VsAccentDarkColor"] = "#FF005A9E",
                ["VsSuccessColor"] = "#FF0E8074",
                ["VsWarningColor"] = "#FFB8860B",
                ["VsErrorColor"] = "#FFD13438",
                ["VsInfoColor"] = "#FF0067C0",
                ["VsRunningColor"] = "#FF2E7D32",
                ["VsAmberColor"] = "#FFB35900",
                ["VsTextPrimaryColor"] = "#FF1E1E1E",
                ["VsTextSecondaryColor"] = "#FF3C3C3C",
                ["VsTextMutedColor"] = "#FF6E6E6E",
                ["VsTextDisabledColor"] = "#FFA0A0A0",
            },
        };

        public ThemeService(IConfiguration config)
        {
            _config = config;
            _currentTheme = _config["UI:Theme"] ?? "Dark";
            if (!Palettes.ContainsKey(_currentTheme)) _currentTheme = "Dark";
            ApplyInternal(_currentTheme);   // 启动时套用持久化主题
        }

        public List<ThemeInfo> GetAvailableThemes() => new()
        {
            new ThemeInfo { Code = "Dark",  DisplayName = "深色" },
            new ThemeInfo { Code = "Light", DisplayName = "浅色" },
        };

        public void ApplyTheme(string themeCode)
        {
            if (string.IsNullOrEmpty(themeCode) || !Palettes.ContainsKey(themeCode)
                || themeCode == _currentTheme) return;

            _currentTheme = themeCode;
            ApplyInternal(themeCode);
            SaveThemeSetting(themeCode);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyInternal(string themeCode)
        {
            var hexMap = Palettes[themeCode];
            var colors = new Dictionary<string, Color>();
            foreach (var kv in hexMap)
                colors[kv.Key] = (Color)ColorConverter.ConvertFromString(kv.Value);

            var root = Application.Current?.Resources;
            if (root != null) UpdateRecursive(root, colors);
        }

        private static void UpdateRecursive(ResourceDictionary dict, Dictionary<string, Color> colors)
        {
            // 1) 直接覆盖 Color 资源（供后续 DynamicResource / 新建画刷使用）
            foreach (var colorKey in colors.Keys)
                if (dict.Contains(colorKey) && dict[colorKey] is Color)
                    dict[colorKey] = colors[colorKey];

            // 2) 原地修改画刷的 Color —— StaticResource 引用会立即重绘
            foreach (var pair in BrushToColor)
            {
                if (dict.Contains(pair.Key) && dict[pair.Key] is SolidColorBrush b
                    && !b.IsFrozen && colors.TryGetValue(pair.Value, out var c))
                {
                    b.Color = c;
                }
            }

            foreach (var md in dict.MergedDictionaries)
                UpdateRecursive(md, colors);
        }

        private void SaveThemeSetting(string themeCode)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(path)) return;
                var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))?.AsObject();
                if (root == null) return;
                if (!root.ContainsKey("UI")) root["UI"] = new System.Text.Json.Nodes.JsonObject();
                root["UI"]!.AsObject()["Theme"] = themeCode;
                File.WriteAllText(path,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 保存失败不影响切换 */ }
        }
    }
}