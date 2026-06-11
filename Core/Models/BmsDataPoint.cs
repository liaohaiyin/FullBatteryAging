using System;
using System.ComponentModel.DataAnnotations;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// PACK 单体电压 / 多路温度采集点（仅 HasBms 启用的通道写入）。
    /// 与 DataPoint 解耦：DataPoint 存总压/总流，BmsDataPoint 存逐节单体 + 多温。
    /// </summary>
    public class BmsDataPoint
    {
        [Key]
        public long Id { get; set; }

        public int TestRecordId { get; set; }
        public int ChannelIndex { get; set; }
        public DateTime Timestamp { get; set; }
        public double TotalElapsedSeconds { get; set; }

        // 单体电压 / 多路温度（JSON 列）
        public double[] CellVoltages { get; set; } = Array.Empty<double>();
        public double[] Temperatures { get; set; } = Array.Empty<double>();

        // 派生指标
        public double MaxCellVoltage { get; set; }
        public double MinCellVoltage { get; set; }
        public double CellVoltageDelta { get; set; }
        public int MaxCellIndex { get; set; }
        public int MinCellIndex { get; set; }
        public double MaxTempPoint { get; set; }
        public double TempDelta { get; set; }
        public double BmsSoc { get; set; }
        public double BmsSoh { get; set; }
        public int FaultCode { get; set; }
    }
}