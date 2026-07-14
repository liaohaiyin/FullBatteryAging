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
        /// <summary>本次校验通过是否来自演示试用（而非正式授权码）</summary>
        public bool IsDemo { get; set; }
        public int RemainingDays =>
            Expiry.HasValue ? (int)Math.Ceiling((Expiry.Value.Date - DateTime.Now.Date).TotalDays) : 0;
    }

    public interface ILicenseService
    {
        string GetMachineCode();
        LicenseStatus CheckCurrentLicense();             // 读取本地授权并校验（含演示试用兜底）
        LicenseStatus Activate(string licenseKey);       // 校验通过则持久化
        LicenseStatus Validate(string licenseKey);       // 仅校验
        string GenerateLicense(string machineCode, DateTime expiry); // 厂商端生成
        /// <summary>开通/续查本机的演示试用（首次调用即绑定本机与起始时间，7天后失效，无需授权码）</summary>
        LicenseStatus ActivateDemo();
    }

    /// <summary>
    /// 离线机器码授权：授权码格式 BA-到期日-签名，签名 = HMAC-SHA256(机器码|到期日, SecretKey)。
    /// 全程本地校验、不依赖任何服务器，厂商侧用同一份 SecretKey 通过 GenerateLicense
    /// 离线生成授权码发给客户即可。机器码由主板/系统 GUID + 主网卡 MAC + CPU 核数派生，
    /// 换机器或改硬件后机器码会变，原授权码即失效。
    /// </summary>
    public class LicenseService : ILicenseService
    {
        // ⚠ 发布前务必替换为你自己的随机密钥，并妥善保管（不要泄露给客户）
        private static readonly byte[] SecretKey =
            Encoding.UTF8.GetBytes("BatteryAging-License-Secret-2026-#Change@Me!");

        private static readonly string LicenseFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BatteryAging", "license.lic");

        private static readonly string DemoFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BatteryAging", "demo.trial");

        private const int DemoTrialDays = 7;

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
                if (File.Exists(LicenseFile))
                {
                    var real = Validate(File.ReadAllText(LicenseFile).Trim());
                    if (real.IsValid) return real;
                    // 正式授权文件存在但校验未通过（过期/换机器等），仍尝试演示试用兜底
                }
                var demo = CheckDemoTrial();
                if (demo != null) return demo;
                return new LicenseStatus { IsValid = false, Message = "未授权，请输入授权码激活，或使用演示试用" };
            }
            catch (Exception ex)
            {
                return new LicenseStatus { IsValid = false, Message = $"读取授权失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 开通演示试用：首次调用在本机写入试用起始时间（HMAC 签名绑定当前机器码，
        /// 防止直接复制/篡改试用文件延长或跨机器使用），此后每次调用都只是重新校验剩余天数，
        /// 不会重置起始时间。
        /// </summary>
        public LicenseStatus ActivateDemo()
        {
            try
            {
                if (!File.Exists(DemoFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(DemoFile)!);
                    var startTicks = DateTime.UtcNow.Ticks;
                    File.WriteAllText(DemoFile, $"{startTicks}|{SignDemo(startTicks)}");
                }
                return CheckDemoTrial() ?? new LicenseStatus { IsValid = false, Message = "演示试用初始化失败" };
            }
            catch (Exception ex)
            {
                return new LicenseStatus { IsValid = false, Message = $"演示试用激活失败: {ex.Message}" };
            }
        }

        /// <summary>返回 null 表示从未开通过演示试用；否则返回当前试用状态（有效或已到期）</summary>
        private LicenseStatus CheckDemoTrial()
        {
            if (!File.Exists(DemoFile)) return null;
            try
            {
                var parts = File.ReadAllText(DemoFile).Trim().Split('|');
                if (parts.Length != 2 || !long.TryParse(parts[0], out var startTicks))
                    return new LicenseStatus { IsValid = false, IsDemo = true, Message = "演示试用文件已损坏，请重新激活" };

                if (!ConstTimeEquals(SignDemo(startTicks), parts[1]))
                    return new LicenseStatus { IsValid = false, IsDemo = true, Message = "演示试用与本机不匹配（可能是从其他电脑复制而来）" };

                var startDate = new DateTime(startTicks, DateTimeKind.Utc);
                var expiry = startDate.AddDays(DemoTrialDays).ToLocalTime();
                if (DateTime.UtcNow > startDate.AddDays(DemoTrialDays))
                    return new LicenseStatus
                    {
                        IsValid = false, IsDemo = true, Expiry = expiry,
                        Message = $"演示试用已到期（{DemoTrialDays} 天已用完），请联系供应商获取正式授权"
                    };

                return new LicenseStatus
                {
                    IsValid = true, IsDemo = true, Expiry = expiry,
                    Message = "演示试用中"
                };
            }
            catch
            {
                return new LicenseStatus { IsValid = false, IsDemo = true, Message = "演示试用文件已损坏，请重新激活" };
            }
        }

        /// <summary>演示试用文件签名：绑定机器码 + 起始时间，本机之外或改动起始时间都会校验失败</summary>
        private string SignDemo(long startTicks)
        {
            using var hmac = new HMACSHA256(SecretKey);
            var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes($"DEMO|{GetMachineCode()}|{startTicks}"));
            return Convert.ToHexString(sig, 0, 12);
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

        /// <summary>逐字符异或比较而不提前 return，比较耗时与内容无关，避免通过响应时间差侧信道爆破签名</summary>
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