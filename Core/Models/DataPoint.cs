using System;
using System.ComponentModel.DataAnnotations;
using BatteryAging.Core.Enums;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 实时数据采集点
    /// 软件每个采样周期记录一次：电压/电流/容量Ah/能量Wh/时间/温度
    /// </summary>
    public class DataPoint
    {
        [Key]
        public long Id { get; set; }

        public int TestRecordId { get; set; }            // 关联测试记录

        public int ChannelIndex { get; set; }            // 通道号

        public int StepSequence { get; set; }            // 当前工步序号

        public StepType StepType { get; set; }           // 当前工步类型

        public int LoopIndex { get; set; }               // 当前循环次数

        public DateTime Timestamp { get; set; }          // 时间戳

        public double ElapsedSeconds { get; set; }       // 工步累计运行时间(s)

        public double TotalElapsedSeconds { get; set; }  // 总累计运行时间(s)

        // ── 测量值 ──
        public double Voltage { get; set; }              // 电压 (V)

        public double Current { get; set; }              // 电流 (A)

        public double Capacity { get; set; }             // 容量 (Ah)

        public double Energy { get; set; }               // 能量 (Wh)

        public double Temperature { get; set; }          // 温度 (°C)

        public double Soc { get; set; }                  // SOC (%)，估算值
    }
}
