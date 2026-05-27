using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BatteryAging.Core.Enums;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 测试工步 - 描述一个充放电动作
    /// </summary>
    public class TestStep
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public int Sequence { get; set; }                // 工步序号

        public string Name { get; set; }                 // 工步名称

        public StepType Type { get; set; }               // 工步类型

        // ── 电气参数 ──
        public double Current { get; set; }              // 设定电流 (A)，正充负放
        public double Voltage { get; set; }              // 设定电压 (V)
        public double CutoffCurrent { get; set; }        // 截止电流 (A) - CV/CCCV用

        // ── 截止条件 ──
        public double CutoffVoltage { get; set; }        // 截止电压 (V)
        public double DurationSeconds { get; set; }      // 工步时长 (s)
        public double CapacityLimit { get; set; }        // 容量上限 (Ah)

        // ── 触发跳转 ──
        public TriggerType TriggerType { get; set; }     // 触发类型
        public CompareOperator TriggerOperator { get; set; }
        public double TriggerValue { get; set; }         // 触发阈值

        // ── 循环参数 ──（仅 Loop 类型有效）
        public int LoopStartIndex { get; set; }          // 从哪一步开始循环
        public int LoopCount { get; set; } = 1;          // 循环次数（1~999）

        // ── 保护参数 ──
        public double MaxVoltage { get; set; } = 4.2;    // 上限电压保护
        public double MinVoltage { get; set; } = 2.5;    // 下限电压保护
        public double MaxCurrent { get; set; } = 5.0;    // 过流保护
        public double MaxTemperature { get; set; } = 60; // 温度保护
        public double ProtectionTimeSeconds { get; set; } = 3600; // 保护时间(s)

        // ── 增强保护（P1/P2 新增） ──
        public bool ReversePolarityCheck { get; set; } = true;   // 启动时反接检测
        public double MaxVoltageDropRate { get; set; } = 0.0;    // 电压跌落速率阈值 (V/s)，0=不检测

        public string Remark { get; set; }               // 备注
    }
}
