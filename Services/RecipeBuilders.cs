using System;
using System.Collections.Generic;
using System.Linq;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;

namespace BatteryAging.Services
{
    // ════════════════════════════════════════════════════════════════════
    //  老化测试方案构建器（生成 TestRecipe，可直接保存/运行）
    //  依赖 TestStep 新增字段：TargetTemperature / WaitForTempStable / SubRecipeId
    // ════════════════════════════════════════════════════════════════════

    public class CalendarAgingOptions
    {
        public double NominalCapacity = 2.6;
        public double TargetSocPercent = 80;
        public double StorageTempC = 45;
        public int StorageDays = 180;        // 总存放天数（如 6 个月）
        public int CheckIntervalDays = 30;   // 每隔多少天做一次容量标定(RPT)
        public double ChargeCurrent = 2.6;   // 1C
        public double DischargeCurrent = 2.6;
        public double FullVoltage = 4.2;
        public double EmptyVoltage = 2.8;
        public double CutoffCurrent = 0.13;  // CV 截止
        public double RptTempC = 25;         // 标定温度
    }

    public static class CalendarAgingBuilder
    {
        public static TestRecipe Build(CalendarAgingOptions o)
        {
            var cn = o.NominalCapacity;
            int cycles = Math.Max(1, o.StorageDays / Math.Max(1, o.CheckIntervalDays));
            var steps = new List<TestStep>();
            int seq = 1;

            TestStep S(string name, StepType t, Action<TestStep> cfg)
            {
                var s = new TestStep { Sequence = seq++, Name = name, Type = t,
                    MaxVoltage = o.FullVoltage + 0.1, MinVoltage = o.EmptyVoltage - 0.1,
                    MaxCurrent = o.ChargeCurrent * 2, MaxTemperature = o.StorageTempC + 20,
                    ProtectionTimeSeconds = 90000, LoopCount = 1, TargetTemperature = o.RptTempC };
                cfg(s); steps.Add(s); return s;
            }

            // —— 调理：满放→充到目标 SOC ——
            S("调理放电", StepType.CC_Discharge, s => { s.Current = o.DischargeCurrent; s.CutoffVoltage = o.EmptyVoltage; });
            S($"充到 SOC{o.TargetSocPercent:F0}%", StepType.CC_Charge, s =>
                { s.Current = o.ChargeCurrent; s.CutoffVoltage = o.FullVoltage; s.CapacityLimit = cn * o.TargetSocPercent / 100.0; });

            // —— 循环体起点 ——
            int loopStart = steps.Count; // 0-based 索引
            S($"{o.StorageTempC:F0}℃ 存放 {o.CheckIntervalDays} 天", StepType.Rest, s =>
            {
                s.DurationSeconds = o.CheckIntervalDays * 86400.0;
                s.TargetTemperature = o.StorageTempC;
                s.WaitForTempStable = true;
                s.ProtectionTimeSeconds = s.DurationSeconds + 86400; // 保护时间必须大于存放时长，否则误触发超时保护
            });

            // RPT 标定：满充→满放(测容量)→回到存放 SOC，均在 25℃
            S("RPT-恒流充电", StepType.CC_Charge, s => { s.Current = o.ChargeCurrent; s.CutoffVoltage = o.FullVoltage; s.TargetTemperature = o.RptTempC; s.WaitForTempStable = true; });
            S("RPT-恒压充电", StepType.CV_Charge, s => { s.Voltage = o.FullVoltage; s.Current = o.ChargeCurrent; s.CutoffCurrent = o.CutoffCurrent; });
            S("RPT-静置", StepType.Rest, s => s.DurationSeconds = 600);
            S("RPT-恒流放电(测容量)", StepType.CC_Discharge, s => { s.Current = o.DischargeCurrent; s.CutoffVoltage = o.EmptyVoltage; });
            S("回到存放 SOC", StepType.CC_Charge, s => { s.Current = o.ChargeCurrent; s.CutoffVoltage = o.FullVoltage; s.CapacityLimit = cn * o.TargetSocPercent / 100.0; });

            // —— 循环 ——
            S($"循环 {cycles} 次", StepType.Loop, s => { s.LoopStartIndex = loopStart; s.LoopCount = cycles; });

            return new TestRecipe
            {
                Name = $"日历寿命_{o.StorageTempC:F0}℃_SOC{o.TargetSocPercent:F0}_{o.StorageDays}天",
                Description = $"定温存储 + 每 {o.CheckIntervalDays} 天容量标定，共 {cycles} 次",
                BatteryType = "Calendar", NominalCapacity = cn, NominalVoltage = 3.7, Steps = steps
            };
        }
    }

    public class HppcOptions
    {
        public double NominalCapacity = 2.6;
        public double DischargePulseC = 1.0; // 放电脉冲倍率
        public double ChargePulseC = 0.75;   // 充电(回馈)脉冲倍率
        public double PulseSeconds = 10;
        public double RestSeconds = 40;
        public int SocStepPercent = 10;      // 每次消耗 10% SOC
        public int StartSoc = 100, EndSoc = 10;
        public double FullVoltage = 4.2, EmptyVoltage = 2.8;
        public double SocRestSeconds = 1800; // 每个 SOC 点静置(达OCV)
    }

    public static class HppcBuilder
    {
        public static TestRecipe Build(HppcOptions o)
        {
            var cn = o.NominalCapacity;
            var steps = new List<TestStep>(); int seq = 1;
            void Add(string n, StepType t, Action<TestStep> cfg)
            { var s = new TestStep { Sequence = seq++, Name = n, Type = t, MaxVoltage = o.FullVoltage + 0.1,
                MinVoltage = o.EmptyVoltage - 0.1, MaxCurrent = cn * 3, MaxTemperature = 60, ProtectionTimeSeconds = 7200, LoopCount = 1 };
                cfg(s); steps.Add(s); }

            for (int soc = o.StartSoc; soc >= o.EndSoc; soc -= o.SocStepPercent)
            {
                Add($"SOC{soc}-放电脉冲", StepType.CC_Discharge, s => { s.Current = cn * o.DischargePulseC; s.DurationSeconds = o.PulseSeconds; });
                Add($"SOC{soc}-脉冲后静置", StepType.Rest, s => s.DurationSeconds = o.RestSeconds);
                Add($"SOC{soc}-充电脉冲", StepType.CC_Charge, s => { s.Current = cn * o.ChargePulseC; s.DurationSeconds = o.PulseSeconds; });
                Add($"SOC{soc}-脉冲后静置", StepType.Rest, s => s.DurationSeconds = o.RestSeconds);
                if (soc - o.SocStepPercent >= o.EndSoc)
                {
                    Add($"消耗到 SOC{soc - o.SocStepPercent}", StepType.CC_Discharge, s => { s.Current = cn; s.CapacityLimit = cn * o.SocStepPercent / 100.0; s.CutoffVoltage = o.EmptyVoltage; });
                    Add("SOC 点静置", StepType.Rest, s => s.DurationSeconds = o.SocRestSeconds);
                }
            }
            return new TestRecipe { Name = "HPPC功率特性", Description = "各 SOC 点充放脉冲，配合 DcirCalculator 求功率能力",
                BatteryType = "HPPC", NominalCapacity = cn, NominalVoltage = 3.7, Steps = steps };
        }
    }

    public class MaxEnergyOptions
    {
        public double NominalCapacity = 2.6;
        public double ChargeCurrent = 2.6, DischargeCurrent = 2.6;
        public double FullVoltage = 4.2, EmptyVoltage = 2.8, CutoffCurrent = 0.13;
        public bool ConstantPowerDischarge = false; public double DischargePowerW = 0;
    }

    public static class MaxEnergyBuilder
    {
        /// <summary>满充→满放，最大放电能量直接读 TestRecord.TotalDischargeEnergy(Wh)</summary>
        public static TestRecipe Build(MaxEnergyOptions o)
        {
            var steps = new List<TestStep>(); int seq = 1;
            void Add(string n, StepType t, Action<TestStep> cfg)
            { var s = new TestStep { Sequence = seq++, Name = n, Type = t, MaxVoltage = o.FullVoltage + 0.1,
                MinVoltage = o.EmptyVoltage - 0.1, MaxCurrent = o.ChargeCurrent * 2, MaxTemperature = 60, ProtectionTimeSeconds = 14400, LoopCount = 1 };
                cfg(s); steps.Add(s); }

            Add("恒流充电", StepType.CC_Charge, s => { s.Current = o.ChargeCurrent; s.CutoffVoltage = o.FullVoltage; });
            Add("恒压充电", StepType.CV_Charge, s => { s.Voltage = o.FullVoltage; s.Current = o.ChargeCurrent; s.CutoffCurrent = o.CutoffCurrent; });
            Add("静置", StepType.Rest, s => s.DurationSeconds = 1800);
            if (o.ConstantPowerDischarge && o.DischargePowerW > 0)
                Add("恒功率放电(测能量)", StepType.CP_Discharge, s => { s.Power = o.DischargePowerW; s.CutoffVoltage = o.EmptyVoltage; });
            else
                Add("恒流放电(测能量)", StepType.CC_Discharge, s => { s.Current = o.DischargeCurrent; s.CutoffVoltage = o.EmptyVoltage; });

            return new TestRecipe { Name = "最大能量测试", Description = "满充满放, 结果读放电能量(Wh)",
                BatteryType = "Energy", NominalCapacity = o.NominalCapacity, NominalVoltage = 3.7, Steps = steps };
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  子程序调用：把 StepType.SubCall 引用的子方案就地展开成扁平工步列表，
    //  并自动修正 Loop/Jump 的索引偏移，运行引擎无需改动循环逻辑。
    // ════════════════════════════════════════════════════════════════════
    public static class RecipeFlattener
    {
        /// <param name="resolver">根据 SubRecipeId 取得子方案</param>
        public static List<TestStep> Flatten(TestRecipe recipe, Func<string, TestRecipe> resolver, int maxDepth = 8)
        {
            var flat = Expand(recipe.Steps, resolver, maxDepth);
            for (int i = 0; i < flat.Count; i++) flat[i].Sequence = i + 1;
            return flat;
        }

        private static List<TestStep> Expand(List<TestStep> steps, Func<string, TestRecipe> resolver, int depth)
        {
            if (depth <= 0) throw new InvalidOperationException("子程序嵌套过深，疑似循环引用");

            // 1) 先算每个原始 step 展开后的块，及其在输出中的起始位置
            var blocks = new List<List<TestStep>>();
            foreach (var s in steps)
            {
                if (s.Type == StepType.SubCall)
                {
                    var sub = resolver(s.SubRecipeId) ?? throw new InvalidOperationException($"子方案不存在: {s.SubRecipeId}");
                    blocks.Add(Expand(sub.Steps, resolver, depth - 1)); // 递归展开（块内索引已 0-based 自洽）
                }
                else blocks.Add(new List<TestStep> { Clone(s) });
            }
            var newStart = new int[steps.Count];
            int acc = 0;
            for (int i = 0; i < blocks.Count; i++) { newStart[i] = acc; acc += blocks[i].Count; }

            // 2) 拼装并修正引用
            var outp = new List<TestStep>();
            for (int i = 0; i < steps.Count; i++)
            {
                var block = blocks[i];
                if (steps[i].Type == StepType.SubCall)
                {
                    // 子块内部 Loop/Jump 是块内 0-based，需整体右移 newStart[i]
                    foreach (var st in block)
                    {
                        if (st.Type == StepType.Loop) st.LoopStartIndex += newStart[i];
                        if (st.JumpTargetIndex >= 0) st.JumpTargetIndex += newStart[i];
                    }
                    outp.AddRange(block);
                }
                else
                {
                    var st = block[0];
                    // 外层引用指向原始索引 → 映射到新起始位置
                    if (st.Type == StepType.Loop && st.LoopStartIndex >= 0 && st.LoopStartIndex < steps.Count)
                        st.LoopStartIndex = newStart[st.LoopStartIndex];
                    if (st.JumpTargetIndex >= 0 && st.JumpTargetIndex < steps.Count)
                        st.JumpTargetIndex = newStart[st.JumpTargetIndex];
                    outp.Add(st);
                }
            }
            return outp;
        }

        private static TestStep Clone(TestStep s) => new TestStep
        {
            Sequence = s.Sequence, Name = s.Name, Type = s.Type,
            Current = s.Current, Voltage = s.Voltage, CutoffCurrent = s.CutoffCurrent,
            Power = s.Power, Resistance = s.Resistance,
            PulseCurrent = s.PulseCurrent, PulseOnSeconds = s.PulseOnSeconds, PulseOffSeconds = s.PulseOffSeconds,
            CutoffVoltage = s.CutoffVoltage, DurationSeconds = s.DurationSeconds, CapacityLimit = s.CapacityLimit,
            TriggerType = s.TriggerType, TriggerOperator = s.TriggerOperator, TriggerValue = s.TriggerValue,
            JumpTargetIndex = s.JumpTargetIndex, LoopStartIndex = s.LoopStartIndex, LoopCount = s.LoopCount,
            MaxVoltage = s.MaxVoltage, MinVoltage = s.MinVoltage, MaxCurrent = s.MaxCurrent,
            MaxTemperature = s.MaxTemperature, ProtectionTimeSeconds = s.ProtectionTimeSeconds,
            ReversePolarityCheck = s.ReversePolarityCheck, MaxVoltageDropRate = s.MaxVoltageDropRate,
            Remark = s.Remark, TargetTemperature = s.TargetTemperature,
            WaitForTempStable = s.WaitForTempStable, SubRecipeId = s.SubRecipeId,
            CellMaxVoltage = s.CellMaxVoltage,
            CellMinVoltage = s.CellMinVoltage,
            MaxCellVoltageDelta = s.MaxCellVoltageDelta,
            MaxTempDelta = s.MaxTempDelta
        };
    }
}
