using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryAging.Services
{
    /// <summary>记住用户名等本地登录偏好，存于 %AppData%\BatteryAging\login_settings.json</summary>
    public class LoginSettings
    {
        public bool RememberUsername { get; set; } = false;
        public string LastUsername { get; set; } = string.Empty;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BatteryAging",
            "login_settings.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public static LoginSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new LoginSettings();
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<LoginSettings>(json, JsonOpts) ?? new LoginSettings();
            }
            catch
            {
                return new LoginSettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
            }
            catch
            {
                // 保存失败静默处理，不影响登录流程
            }
        }
    }
}
