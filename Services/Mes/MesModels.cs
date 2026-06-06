using System;

namespace BatteryAging.Services.Mes
{
    /// <summary>过站校验请求(开测前)</summary>
    public class MesCheckRequest
    {
        public string BarCode { get; set; }
        public int ChannelIndex { get; set; }
        public string CabinetId { get; set; }
        public string RecipeName { get; set; }
    }

    public class MesCheckResult
    {
        public bool Allowed { get; set; }       // MES 是否允许开测
        public string Message { get; set; }
    }

    /// <summary>测试结果上传(测完)</summary>
    public class MesTestResult
    {
        public string BarCode { get; set; }
        public int ChannelIndex { get; set; }
        public string RecipeName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; }              // Completed/Stopped/Protected...
        public double TotalChargeCapacity { get; set; }
        public double TotalDischargeCapacity { get; set; }
        public double TotalChargeEnergy { get; set; }
        public double TotalDischargeEnergy { get; set; }
        public double SohEstimate { get; set; }
        public string Grade { get; set; }
        public int CompletedCycles { get; set; }
    }

    public class MesAck
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    // ── 配置(绑定 appsettings.json 的 "Mes" 节点)──
    public enum MesProvider { None, RestJson }

    public class MesEndpoint
    {
        public string Path { get; set; }
        public string Method { get; set; } = "POST";
    }

    public class MesConfig
    {
        public MesProvider Provider { get; set; } = MesProvider.None;
        public string BaseUrl { get; set; }
        public int TimeoutMs { get; set; } = 5000;
        public bool CheckInEnabled { get; set; } = false;   // 是否启用过站校验
        public bool BlockOnCheckFail { get; set; } = true;  // 校验失败/不通是否拦停(false=告警放行)
        public string TimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";

        public MesEndpoint CheckIn { get; set; } = new();
        public MesEndpoint Upload { get; set; } = new();

        // 中性字段名 → 客户字段名;未列出的字段用原名
        public Dictionary<string, string> FieldMap { get; set; } = new();

        // 固定附加字段(如线体号、token),原样并入请求体
        public Dictionary<string, string> ExtraFields { get; set; } = new();
    }
}