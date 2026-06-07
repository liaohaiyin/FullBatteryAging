using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace BatteryAging.Services
{
    public class LicenseStatus
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public DateTime? Expiry { get; set; }
        public int RemainingDays =>
            Expiry.HasValue ? (int)Math.Ceiling((Expiry.Value.Date - DateTime.Now.Date).TotalDays) : 0;
    }

    public interface ILicenseService
    {
        string GetMachineCode();
        LicenseStatus CheckCurrentLicense();             // 读取本地授权并校验
        LicenseStatus Activate(string licenseKey);       // 校验通过则持久化
        LicenseStatus Validate(string licenseKey);       // 仅校验
        string GenerateLicense(string machineCode, DateTime expiry); // 厂商端生成
    }

    public class LicenseService : ILicenseService
    {
        // ⚠ 发布前务必替换为你自己的随机密钥，并妥善保管（不要泄露给客户）
        private static readonly byte[] SecretKey =
            Encoding.UTF8.GetBytes("BatteryAging-License-Secret-2026-#Change@Me!");

        private static readonly string LicenseFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BatteryAging", "license.lic");

        private string _cachedMachineCode;

        public string GetMachineCode()
        {
            if (!string.IsNullOrEmpty(_cachedMachineCode)) return _cachedMachineCode;

            var raw = $"{GetMachineGuid()}|{GetPrimaryMac()}|{Environment.ProcessorCount}";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            var hex = Convert.ToHexString(hash, 0, 10);   // 20 hex → 5 组
            _cachedMachineCode = string.Join("-",
                Enumerable.Range(0, 5).Select(i => hex.Substring(i * 4, 4)));
            return _cachedMachineCode;
        }

        public LicenseStatus CheckCurrentLicense()
        {
            try
            {
                if (!File.Exists(LicenseFile))
                    return new LicenseStatus { IsValid = false, Message = "未授权，请输入授权码激活" };
                return Validate(File.ReadAllText(LicenseFile).Trim());
            }
            catch (Exception ex)
            {
                return new LicenseStatus { IsValid = false, Message = $"读取授权失败: {ex.Message}" };
            }
        }

        public LicenseStatus Activate(string licenseKey)
        {
            var status = Validate(licenseKey);
            if (status.IsValid)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LicenseFile)!);
                    File.WriteAllText(LicenseFile, licenseKey.Trim());
                }
                catch (Exception ex)
                {
                    return new LicenseStatus { IsValid = false, Message = $"保存授权失败: {ex.Message}" };
                }
            }
            return status;
        }

        public LicenseStatus Validate(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return new LicenseStatus { IsValid = false, Message = "授权码为空" };

            // 格式: BA-yyyyMMdd-<24位签名>
            var parts = licenseKey.Trim().ToUpperInvariant().Split('-');
            if (parts.Length != 3 || parts[0] != "BA")
                return new LicenseStatus { IsValid = false, Message = "授权码格式错误" };

            if (!DateTime.TryParseExact(parts[1], "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var expiry))
                return new LicenseStatus { IsValid = false, Message = "授权码有效期格式错误" };

            var expected = Sign(GetMachineCode(), expiry);
            if (!ConstTimeEquals(expected, parts[2]))
                return new LicenseStatus { IsValid = false, Message = "授权码与本机不匹配" };

            var expiryEnd = expiry.Date.AddDays(1).AddSeconds(-1);
            if (DateTime.Now > expiryEnd)
                return new LicenseStatus { IsValid = false, Message = $"授权已过期（{expiry:yyyy-MM-dd}）", Expiry = expiry };

            return new LicenseStatus { IsValid = true, Message = "授权有效", Expiry = expiry };
        }

        public string GenerateLicense(string machineCode, DateTime expiry)
            => $"BA-{expiry:yyyyMMdd}-{Sign(machineCode.Trim().ToUpperInvariant(), expiry)}";

        private static string Sign(string machineCode, DateTime expiry)
        {
            using var hmac = new HMACSHA256(SecretKey);
            var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{machineCode}|{expiry:yyyyMMdd}"));
            return Convert.ToHexString(sig, 0, 12);   // 24 hex
        }

        private static bool ConstTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string GetMachineGuid()
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid")?.ToString() ?? "NO-GUID";
            }
            catch { return "NO-GUID"; }
        }

        private static string GetPrimaryMac()
        {
            try
            {
                var macList = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                                && !n.Description.Contains("Virtual"))
                    .Select(n => n.GetPhysicalAddress()?.ToString())
                    .Where(m => !string.IsNullOrEmpty(m))
                    .OrderBy(m => m)
                    .ToList();

                return macList.FirstOrDefault() ?? "NO-MAC";
            }
            catch { return "NO-MAC"; }
        }
    }
}