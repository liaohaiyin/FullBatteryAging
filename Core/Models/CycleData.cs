using System;
using System.ComponentModel.DataAnnotations;

namespace BatteryAging.Core.Models
{
    /// <summary>单个循环的容量/能量/效率记录，用于绘制循环衰减曲线</summary>
    public class CycleData
    {
        [Key] public long Id { get; set; }
        public int TestRecordId { get; set; }
        public int ChannelIndex { get; set; }
        public int CycleIndex { get; set; }                 // 第几次循环(1-based)
        public double ChargeCapacity { get; set; }          // 本循环充电容量 (Ah)
        public double DischargeCapacity { get; set; }       // 本循环放电容量 (Ah)
        public double ChargeEnergy { get; set; }            // 本循环充电能量 (Wh)
        public double DischargeEnergy { get; set; }         // 本循环放电能量 (Wh)
        public double CoulombicEfficiency { get; set; }     // 库伦效率 = 放电Ah/充电Ah
        public double EnergyEfficiency { get; set; }        // 能量效率 = 放电Wh/充电Wh
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}