using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BatteryAging.Core.Enums;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 测试记录 - 一次完整的测试任务
    /// </summary>
    public class TestRecord
    {
        [Key]
        public int Id { get; set; }

        public string CabinetId { get; set; }            // 所属机柜
        public int ChannelIndex { get; set; }            // 测试通道

        public string BarCode { get; set; }              // 电池条码

        public string RecipeId { get; set; }             // 使用的测试方案ID

        public string RecipeName { get; set; }           // 方案名称（冗余字段）

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public ChannelStatus Status { get; set; }

        public ProtectionType ProtectionTrigger { get; set; } = ProtectionType.None;

        public string FailReason { get; set; }

        // ── 汇总统计 ──
        public double TotalChargeCapacity { get; set; }   // 总充电容量 (Ah)
        public double TotalDischargeCapacity { get; set; }// 总放电容量 (Ah)
        public double TotalChargeEnergy { get; set; }     // 总充电能量 (Wh)
        public double TotalDischargeEnergy { get; set; }  // 总放电能量 (Wh)
        public int CompletedCycles { get; set; }          // 已完成循环次数

        // ── SOH 估算 ──
        public double SohEstimate { get; set; }           // SOH 健康度 (0~1)
        public double NominalCapacity { get; set; }       // 标称容量 (Ah)，从方案中冗余
        public double InternalResistance { get; set; }    // 估算内阻 (Ω)

        // ── 容量分档 ──
        public CapacityGrade Grade { get; set; } = CapacityGrade.Unknown;

        // ── 掉电续测断点 ──
        public int LastStepIndex { get; set; }            // 上次断点的工步索引
        public int LastLoopIndex { get; set; }            // 上次断点的循环索引
        public double LastTotalElapsed { get; set; }      // 已运行总秒数
        public DateTime LastCheckpointTime { get; set; }  // 最后一次检查点时间

        public string Operator { get; set; } = Environment.UserName;
    }
}
