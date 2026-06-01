using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// DCIR 直流内阻测量结果（脉冲法）
    /// R = |V_pulse(t) - V_rest| / |I_pulse|
    /// </summary>
    public class DcirResult
    {
        [Key] public long Id { get; set; }
        public int TestRecordId { get; set; }
        public int ChannelIndex { get; set; }

        public double Soc { get; set; }              // 测量时 SOC(%)
        public double RestVoltage { get; set; }      // 脉冲前静置电压 V0 (V)
        public double PulseCurrent { get; set; }     // 脉冲电流 I (A，正充负放)
        public bool IsCharge { get; set; }           // 充电方向还是放电方向

        /// <summary>不同脉冲持续时间下的内阻，键=秒，值=Ω（如 R_1s, R_10s, R_30s）</summary>
        public Dictionary<double, double> ResistanceByTime { get; set; } = new();

        public double Resistance => ResistanceByTime.Count > 0
            ? GetClosest(10.0) : 0;                  // 默认取 10s 内阻作为代表值
        public double Temperature { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        private double GetClosest(double t)
        {
            double best = 0, bestDiff = double.MaxValue;
            foreach (var kv in ResistanceByTime)
            {
                var d = Math.Abs(kv.Key - t);
                if (d < bestDiff) { bestDiff = d; best = kv.Value; }
            }
            return best;
        }
    }
}
