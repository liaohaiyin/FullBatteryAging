using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 设备校准记录 —— 周期性用标准源对充放电通道的电压/电流做比对校准，
    /// 登记基准值/实测值/偏差，并可设置有效期用于到期提醒。
    /// </summary>
    public class CalibrationRecord
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string CabinetId { get; set; }
        [StringLength(100)]
        public string CabinetName { get; set; }
        public int ChannelIndex { get; set; }

        public DateTime CalibrationDate { get; set; } = DateTime.Now;
        public DateTime? NextDueDate { get; set; }

        // ── 电压校准 ──
        public double RefVoltage { get; set; }
        public double MeasuredVoltage { get; set; }
        [NotMapped]
        public double VoltageDeviation => RefVoltage == 0 ? 0 : (MeasuredVoltage - RefVoltage) / RefVoltage * 100.0;

        // ── 电流校准 ──
        public double RefCurrent { get; set; }
        public double MeasuredCurrent { get; set; }
        [NotMapped]
        public double CurrentDeviation => RefCurrent == 0 ? 0 : (MeasuredCurrent - RefCurrent) / RefCurrent * 100.0;

        /// <summary>允许偏差百分比，超出则判定不合格</summary>
        public double ToleragePercent { get; set; } = 0.5;

        [NotMapped]
        public bool IsPassed =>
            Math.Abs(VoltageDeviation) <= ToleragePercent && Math.Abs(CurrentDeviation) <= ToleragePercent;

        [StringLength(50)]
        public string Technician { get; set; }
        [StringLength(300)]
        public string Remark { get; set; }

        [NotMapped]
        public bool IsOverdue => NextDueDate.HasValue && NextDueDate.Value.Date < DateTime.Today;

        [NotMapped]
        public bool IsDueSoon => NextDueDate.HasValue && !IsOverdue &&
            (NextDueDate.Value.Date - DateTime.Today).TotalDays <= 14;
    }
}
