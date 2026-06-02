using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace BatteryAging.Services
{
    public class LanguageService : ILanguageService
    {
        private readonly IConfiguration _configuration;
        private string _currentLanguage;
        private ResourceDictionary _currentLanguageDict;

        public string CurrentLanguage => _currentLanguage;

        public event EventHandler<LanguageChangedEventArgs> LanguageChanged;

        public LanguageService(IConfiguration configuration)
        {
            _configuration = configuration;
            _currentLanguage = _configuration["UI:Language"] ?? "zh-CN";
            LoadLanguageResources(_currentLanguage);
        }

        public List<LanguageInfo> GetAvailableLanguages() => new()
        {
            new LanguageInfo { Code = "zh-CN", DisplayName = "简体中文", NativeName = "简体中文" },
            new LanguageInfo { Code = "en-US", DisplayName = "English",  NativeName = "English"  },
        };

        public async Task<bool> ChangeLanguageAsync(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode) || languageCode == _currentLanguage)
                return false;

            try
            {
                var oldLanguage = _currentLanguage;
                if (!LoadLanguageResources(languageCode)) return false;
                _currentLanguage = languageCode;
                await SaveLanguageSettingAsync(languageCode);

                LanguageChanged?.Invoke(this, new LanguageChangedEventArgs
                {
                    OldLanguage = oldLanguage,
                    NewLanguage = languageCode
                });
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"切换语言失败: {ex.Message}");
                return false;
            }
        }

        public string GetString(string key)
        {
            if (_currentLanguageDict != null && _currentLanguageDict.Contains(key))
                return _currentLanguageDict[key]?.ToString() ?? key;
            return key;
        }

        private bool LoadLanguageResources(string languageCode)
        {
            try
            {
                if (_currentLanguageDict != null)
                    Application.Current.Resources.MergedDictionaries.Remove(_currentLanguageDict);

                var langFile = languageCode switch
                {
                    "zh-CN" => "pack://application:,,,/BatteryAging;component/UI/Themes/Languages/zh-CN.xaml",
                    "en-US" => "pack://application:,,,/BatteryAging;component/UI/Themes/Languages/en-US.xaml",
                    _ => "pack://application:,,,/BatteryAging;component/UI/Themes/Languages/zh-CN.xaml"
                };

                _currentLanguageDict = new ResourceDictionary { Source = new Uri(langFile, UriKind.Absolute) };
                Application.Current.Resources.MergedDictionaries.Add(_currentLanguageDict);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载语言资源失败: {ex.Message}");
                return false;
            }
        }

        private async Task SaveLanguageSettingAsync(string languageCode)
        {
            try
            {
                var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(appSettingsPath)) return;

                var json = await File.ReadAllTextAsync(appSettingsPath);
                var rootObj = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
                if (rootObj == null) return;

                if (!rootObj.ContainsKey("UI"))
                    rootObj["UI"] = new System.Text.Json.Nodes.JsonObject();

                var uiNode = rootObj["UI"]?.AsObject();
                if (uiNode != null) uiNode["Language"] = languageCode;

                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(appSettingsPath, rootObj.ToJsonString(options));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存语言设置失败: {ex.Message}");
            }
        }
    }
}
