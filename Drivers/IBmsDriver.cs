using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryAging.Drivers
{
    /// <summary>一次 BMS 采集读数（单体电压 / 多点温度 / SOC / SOH / 故障）</summary>
    public class BmsReading
    {
        public double[] CellVoltages { get; set; } = Array.Empty<double>();   // 各单体电压 (V)
        public double[] Temperatures { get; set; } = Array.Empty<double>();   // 各点温度 (℃)
        public double PackVoltage { get; set; }     // BMS 侧总压 (V)
        public double PackCurrent { get; set; }     // BMS 侧电流 (A)
        public double Soc { get; set; }             // (%)
        public double Soh { get; set; }             // (%)
        public int FaultCode { get; set; }          // 0 = 正常
        public bool[] BalancingState { get; set; } = Array.Empty<bool>();
        public DateTime Timestamp { get; set; } = DateTime.Now;

        // ── 派生指标 ──
        public bool HasCells => CellVoltages.Length > 0;
        public double MaxCellVoltage => HasCells ? CellVoltages.Max() : 0;
        public double MinCellVoltage => HasCells ? CellVoltages.Min() : 0;
        public double CellVoltageDelta => HasCells ? MaxCellVoltage - MinCellVoltage : 0;
        public int MaxCellIndex => HasCells ? Array.IndexOf(CellVoltages, MaxCellVoltage) + 1 : 0;
        public int MinCellIndex => HasCells ? Array.IndexOf(CellVoltages, MinCellVoltage) + 1 : 0;
        public bool HasTemps => Temperatures.Length > 0;
        public double MaxTemperature => HasTemps ? Temperatures.Max() : 0;
        public double MinTemperature => HasTemps ? Temperatures.Min() : 0;
        public double TempDelta => HasTemps ? MaxTemperature - MinTemperature : 0;
    }

    /// <summary>
    /// BMS 采集驱动接口（与 IDeviceDriver 平级，独立链路）。
    /// 充放电机给"总压/总流"，BMS 给"逐节单体电压 + 多点温度 + SOC/SOH/故障"。
    /// </summary>
    public interface IBmsDriver : IDisposable
    {
        /// <summary>是否已连接</summary>
        bool IsConnected { get; }
        /// <summary>连接到 BMS</summary>
        Task<bool> ConnectAsync(CancellationToken token = default);
        /// <summary>断开连接</summary>
        Task DisconnectAsync();
        /// <summary>读取指定 PACK（通道内 1-based）的一帧 BMS 数据</summary>
        Task<BmsReading> ReadAsync(int packIndex, CancellationToken token = default);
        /// <summary>通讯异常事件</summary>
        event EventHandler<string> CommunicationError;
    }
}