using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BatteryAging.Services.Mes
{
    /// <summary>
    /// 通用 REST/JSON MES 对接实现。不同工厂的 MES 系统接口路径、字段命名、认证方式都不一样，
    /// 这里不为每家 MES 写专用代码，而是把"过站校验/结果上传"这两个动作抽象成固定的中性字段
    /// （见 Map 方法），通过配置里的 FieldMap 把中性字段名翻译成对方系统要求的实际字段名，
    /// 再叠加 ExtraFields 里配置的固定附加字段（如产线编号、密钥等）。换一家 MES 通常只需改配置，
    /// 不需要改代码。CheckInAsync 在 MES 不可达时的行为（拦停还是放行）也由 BlockOnCheckFail
    /// 配置项决定，避免 MES 系统故障时把产线全部堵死。
    /// </summary>
    public class RestMesService : IMesService
    {
        private readonly MesConfig _cfg;
        private readonly ILogger<RestMesService> _logger;
        private readonly HttpClient _http;

        public bool IsEnabled => _cfg.Provider == MesProvider.RestJson
                                 && !string.IsNullOrWhiteSpace(_cfg.BaseUrl);
        public bool CheckInEnabled => IsEnabled && _cfg.CheckInEnabled;

        public RestMesService(IConfiguration config, ILogger<RestMesService> logger)
        {
            _cfg = config.GetSection("Mes").Get<MesConfig>() ?? new MesConfig();
            _logger = logger;
            _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(_cfg.TimeoutMs) };
            if (IsEnabled) _http.BaseAddress = new Uri(_cfg.BaseUrl.TrimEnd('/') + "/");
        }

        public async Task<MesCheckResult> CheckInAsync(MesCheckRequest req, CancellationToken ct = default)
        {
            if (!CheckInEnabled) return new MesCheckResult { Allowed = true, Message = "未启用过站校验" };

            var body = Map(new Dictionary<string, object>
            {
                ["BarCode"] = req.BarCode,
                ["Channel"] = req.ChannelIndex,
                ["CabinetId"] = req.CabinetId,
                ["RecipeName"] = req.RecipeName,
            });

            try
            {
                using var resp = await SendAsync(_cfg.CheckIn, body, ct);
                var json = await resp.Content.ReadAsStringAsync(ct);
                resp.EnsureSuccessStatusCode();

                // 约定:响应含布尔字段(默认名 "allowed",可被 FieldMap 改名)与消息("message")
                using var doc = JsonDocument.Parse(json);
                bool allowed = TryGetBool(doc.RootElement, MappedName("Allowed", "allowed"));
                string msg = TryGetString(doc.RootElement, MappedName("Message", "message"));
                return new MesCheckResult { Allowed = allowed, Message = msg ?? "" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MES 过站校验失败 [{Bar}]", req.BarCode);
                // 降级:拦停 or 告警放行,由配置决定
                return new MesCheckResult
                {
                    Allowed = !_cfg.BlockOnCheckFail,
                    Message = $"MES 不可达: {ex.Message}" + (_cfg.BlockOnCheckFail ? "(已拦停)" : "(放行)")
                };
            }
        }

        public async Task<MesAck> UploadResultAsync(MesTestResult r, CancellationToken ct = default)
        {
            if (!IsEnabled) return new MesAck { Success = true, Message = "未接 MES" };

            var body = Map(new Dictionary<string, object>
            {
                ["BarCode"] = r.BarCode,
                ["Channel"] = r.ChannelIndex,
                ["RecipeName"] = r.RecipeName,
                ["StartTime"] = r.StartTime.ToString(_cfg.TimeFormat),
                ["EndTime"] = r.EndTime?.ToString(_cfg.TimeFormat),
                ["Status"] = r.Status,
                ["ChargeCapacity"] = r.TotalChargeCapacity,
                ["DischargeCapacity"] = r.TotalDischargeCapacity,
                ["ChargeEnergy"] = r.TotalChargeEnergy,
                ["DischargeEnergy"] = r.TotalDischargeEnergy,
                ["Soh"] = r.SohEstimate,
                ["Grade"] = r.Grade,
                ["CompletedCycles"] = r.CompletedCycles,
            });

            try
            {
                using var resp = await SendAsync(_cfg.Upload, body, ct);
                resp.EnsureSuccessStatusCode();
                return new MesAck { Success = true, Message = "上传成功" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MES 结果上传失败 [{Bar}]", r.BarCode);
                return new MesAck { Success = false, Message = ex.Message };
            }
        }

        // ── 工具 ──
        private async Task<HttpResponseMessage> SendAsync(MesEndpoint ep, object body, CancellationToken ct)
        {
            var method = (ep.Method ?? "POST").ToUpperInvariant();
            using var msg = new HttpRequestMessage(new HttpMethod(method), ep.Path)
            {
                Content = JsonContent.Create(body)
            };
            return await _http.SendAsync(msg, ct);
        }

        /// <summary>按 FieldMap 翻译字段名,并并入固定附加字段</summary>
        private Dictionary<string, object> Map(Dictionary<string, object> neutral)
        {
            var outp = new Dictionary<string, object>();
            foreach (var kv in neutral)
            {
                if (kv.Value == null) continue;
                outp[MappedName(kv.Key, kv.Key)] = kv.Value;
            }
            foreach (var kv in _cfg.ExtraFields) outp[kv.Key] = kv.Value;
            return outp;
        }

        private string MappedName(string neutralKey, string fallback)
            => _cfg.FieldMap.TryGetValue(neutralKey, out var mapped) && !string.IsNullOrEmpty(mapped)
               ? mapped : fallback;

        private static bool TryGetBool(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var el))
            {
                if (el.ValueKind == JsonValueKind.True) return true;
                if (el.ValueKind == JsonValueKind.False) return false;
                if (el.ValueKind == JsonValueKind.Number) return el.GetInt32() != 0;
                if (el.ValueKind == JsonValueKind.String) return el.GetString() is "1" or "OK" or "true" or "PASS";
            }
            return false;
        }

        private static string TryGetString(JsonElement root, string name)
            => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }
}