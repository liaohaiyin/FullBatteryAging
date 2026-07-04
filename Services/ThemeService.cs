using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;

namespace BatteryAging.Services
{
    /// <summary>
    /// 主题切换服务。大部分 XAML 用 StaticResource 引用画刷（性能更好，但只在首次加载时解析一次，
    /// 后续换主题不会自动刷新）。这里换主题时不是替换字典条目，而是拿到已存在的 SolidColorBrush
    /// 实例本身、原地改它的 .Color 属性 —— 因为 StaticResource 拿到的就是这个实例的引用，
    /// 改它的属性能让所有已渲染的 UI 立即重绘，等价于低成本实现了 DynamicResource 的效果。
    /// 前提是这些画刷不能是 Frozen（WPF 默认会冻结用 XAML 字面量声明的画刷以优化性能），
    /// 所以启动时先用 <see cref="ThawBrushes"/> 把它们换成可写克隆。
    /// </summary>
    public class ThemeService : IThemeService
    {
        private readonly IConfiguration _config;
        private readonly ILanguageService _language;
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
                ["VsBgDeepColor"] = "#FF3A3A3D",
                ["VsBgMidColor"] = "#FF424245",
                ["VsBgPanelColor"] = "#FF4A4A4D",
                ["VsBgHeaderColor"] = "#FF515155",
                ["VsBgHoverColor"] = "#FF5A5A5E",
                ["VsBgActiveColor"] = "#FF155A8A",
                ["VsBgInputColor"] = "#FF515155",
                ["VsBorderColor"] = "#FF5E5E63",
                ["VsBorderActiveColor"] = "#FF1C97EA",
                ["VsBorderLightColor"] = "#FF6E6E73",
                ["VsAccentColor"] = "#FF1C97EA",
                ["VsAccentHoverColor"] = "#FF3AA8F0",
                ["VsAccentDarkColor"] = "#FF0E6FB8",
                ["VsSuccessColor"] = "#FF5FD4BB",
                ["VsWarningColor"] = "#FFE0C68A",
                ["VsErrorColor"] = "#FFF89580",
                ["VsInfoColor"] = "#FFB0E0FF",
                ["VsRunningColor"] = "#FF6F9E5E",
                ["VsAmberColor"] = "#FFD89A82",
                ["VsTextPrimaryColor"] = "#FFF5F5F5",
                ["VsTextSecondaryColor"] = "#FFD8D8D8",
                ["VsTextMutedColor"] = "#FFA8A8A8",
                ["VsTextDisabledColor"] = "#FF707074",
            },
            ["Navy"] = new()
            {
                ["VsBgDeepColor"] = "#FF12203A",
                ["VsBgMidColor"] = "#FF182943",
                ["VsBgPanelColor"] = "#FF1E314F",
                ["VsBgHeaderColor"] = "#FF243A5C",
                ["VsBgHoverColor"] = "#FF2C456B",
                ["VsBgActiveColor"] = "#FF1565C0",
                ["VsBgInputColor"] = "#FF1E314F",
                ["VsBorderColor"] = "#FF34507A",
                ["VsBorderActiveColor"] = "#FF409EFF",
                ["VsBorderLightColor"] = "#FF456596",
                ["VsAccentColor"] = "#FF409EFF",
                ["VsAccentHoverColor"] = "#FF5CADFF",
                ["VsAccentDarkColor"] = "#FF1976D2",
                ["VsSuccessColor"] = "#FF4EC9B0",
                ["VsWarningColor"] = "#FFE2B964",
                ["VsErrorColor"] = "#FFF47B6E",
                ["VsInfoColor"] = "#FF8FD0FF",
                ["VsRunningColor"] = "#FF5FB87A",
                ["VsAmberColor"] = "#FFE0915F",
                ["VsTextPrimaryColor"] = "#FFEAF2FF",
                ["VsTextSecondaryColor"] = "#FFC2D4EC",
                ["VsTextMutedColor"] = "#FF8AA3C4",
                ["VsTextDisabledColor"] = "#FF5A7196",
            },
        };

        public ThemeService(IConfiguration config, ILanguageService language)
        {
            _config = config;
            _language = language;
            _currentTheme = _config["UI:Theme"] ?? "Dark";
            if (!Palettes.ContainsKey(_currentTheme)) _currentTheme = "Dark";

            // 关键:在任何窗口加载前,把冻结画刷换成可写克隆
            var root = Application.Current?.Resources;
            if (root != null) ThawBrushes(root);

            ApplyInternal(_currentTheme);            
        }

        public List<ThemeInfo> GetAvailableThemes() => new()
        {
            new ThemeInfo { Code = "Dark",  DisplayName = _language.GetString("Main_Theme_Dark") },
            new ThemeInfo { Code = "Light", DisplayName = _language.GetString("Main_Theme_Light") },
            new ThemeInfo { Code = "Navy",  DisplayName = _language.GetString("Main_Theme_Navy") },
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
            // 1) 覆盖 Color 资源(供 DynamicResource / 新建画刷使用)
            foreach (var colorKey in colors.Keys)
                if (dict.Contains(colorKey) && dict[colorKey] is Color)
                    dict[colorKey] = colors[colorKey];

            // 2) 原地修改画刷 Color —— StaticResource 引用会立即重绘
            foreach (var pair in BrushToColor)
            {
                if (dict.Contains(pair.Key) && dict[pair.Key] is SolidColorBrush b
                    && colors.TryGetValue(pair.Value, out var c))
                {
                    if (!b.IsFrozen) b.Color = c;
                    else dict[pair.Key] = new SolidColorBrush(c);  // 兜底:仍冻结则替换(仅影响后续加载的元素)
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
        /// <summary>把字典(含合并字典)中所有冻结的 Vs* 画刷替换为未冻结克隆,
        /// 使后续可原地修改 .Color,且 StaticResource 引用到的就是可写实例。</summary>
        private static void ThawBrushes(ResourceDictionary dict)
        {
            foreach (var key in BrushToColor.Keys)
            {
                if (dict.Contains(key) && dict[key] is SolidColorBrush b && b.IsFrozen)
                {
                    try { dict[key] = b.Clone(); }   // Clone() 返回未冻结副本
                    catch { /* 字典只读时忽略,该实例走不到这条路径 */ }
                }
            }
            foreach (var md in dict.MergedDictionaries)
                ThawBrushes(md);
        }
    }
}