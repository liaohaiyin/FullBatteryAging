using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Drivers;

namespace BatteryAging.Services
{
    /// <summary>DCIR 测量配置</summary>
    public class DcirProfile
    {
        public double PulseCurrent { get; set; } = -2.6;   // 脉冲电流(A)，负=放电脉冲(更常用)
        public double PreRestSeconds { get; set; } = 30;    // 脉冲前静置稳压时间
        public double[] SampleTimes { get; set; } = { 1, 10, 18, 30 }; // 在脉冲第 t 秒采样计算内阻
        public double SampleIntervalMs { get; set; } = 200; // 脉冲期间采样间隔
        public bool RecoverRestAfter { get; set; } = true;  // 测后回静置
    }

    /// <summary>
    /// 纯计算 + 测量流程，独立于 ChannelExecutor，可在工步流程外单独调用做内阻标定。
    /// </summary>
    public static class DcirCalculator
    {
        /// <summary>R = ΔV / |I|</summary>
        public static double Compute(double restVoltage, double pulseVoltage, double pulseCurrent)
        {
            var i = Math.Abs(pulseCurrent);
            if (i < 1e-6) return 0;
            return Math.Abs(pulseVoltage - restVoltage) / i;
        }

        /// <summary>
        /// 执行一次完整 DCIR 测量：静置→记录 V0→施加脉冲→在各时间点采样→计算 R(t)→回静置。
        /// </summary>
        public static async Task<DcirResult> MeasureAsync(
            IDeviceDriver driver, int localChannel, DcirProfile profile,
            double soc, int testRecordId, int globalChannelIndex,
            CancellationToken token = default)
        {
            var result = new DcirResult
            {
                TestRecordId = testRecordId,
                ChannelIndex = globalChannelIndex,
                Soc = soc,
                PulseCurrent = profile.PulseCurrent,
                IsCharge = profile.PulseCurrent > 0
            };

            // 1) 静置稳压
            await driver.ApplyStepAsync(localChannel, new StepSetpoint { Type = StepType.Rest }, token);
            await Task.Delay(TimeSpan.FromSeconds(profile.PreRestSeconds), token);
            var m0 = await driver.ReadAsync(localChannel, token);
            result.RestVoltage = m0.Voltage;
            result.Temperature = m0.Temperature;

            // 2) 施加脉冲（用 CC 充/放表达脉冲幅值）
            var pulseSp = new StepSetpoint
            {
                Type = profile.PulseCurrent >= 0 ? StepType.CC_Charge : StepType.CC_Discharge,
                Current = Math.Abs(profile.PulseCurrent)
            };
            await driver.ApplyStepAsync(localChannel, pulseSp, token);

            // 3) 脉冲期间按时间点采样
            var targets = profile.SampleTimes.OrderBy(x => x).ToList();
            double maxT = targets.Last();
            var start = DateTime.Now;
            int idx = 0;
            while ((DateTime.Now - start).TotalSeconds <= maxT + 0.5 && idx < targets.Count)
            {
                token.ThrowIfCancellationRequested();
                double elapsed = (DateTime.Now - start).TotalSeconds;
                if (elapsed >= targets[idx])
                {
                    var m = await driver.ReadAsync(localChannel, token);
                    var r = Compute(result.RestVoltage, m.Voltage, profile.PulseCurrent);
                    result.ResistanceByTime[targets[idx]] = Math.Round(r, 6);
                    idx++;
                }
                await Task.Delay((int)profile.SampleIntervalMs, token);
            }

            // 4) 回静置
            if (profile.RecoverRestAfter)
                await driver.ApplyStepAsync(localChannel, new StepSetpoint { Type = StepType.Rest }, token);

            return result;
        }
    }
}
