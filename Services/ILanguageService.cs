using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BatteryAging.Services
{
    public interface ILanguageService
    {
        /// <summary>当前语言代码（如 zh-CN / en-US）</summary>
        string CurrentLanguage { get; }

        /// <summary>可用语言列表</summary>
        List<LanguageInfo> GetAvailableLanguages();

        /// <summary>切换语言（合并对应资源字典并持久化到 appsettings.json）</summary>
        Task<bool> ChangeLanguageAsync(string languageCode);

        /// <summary>取本地化字符串，找不到时返回 key 本身</summary>
        string GetString(string key);

        event EventHandler<LanguageChangedEventArgs> LanguageChanged;
    }

    public class LanguageInfo
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public string NativeName { get; set; }
    }

    public class LanguageChangedEventArgs : EventArgs
    {
        public string OldLanguage { get; set; }
        public string NewLanguage { get; set; }
    }
}
