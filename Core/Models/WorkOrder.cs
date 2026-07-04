using System;
using System.ComponentModel.DataAnnotations;
using BatteryAging.Core.Enums;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 工单 —— 绑定生产批次/产线/操作员，测试记录可关联到工单用于统计与追溯。
    /// </summary>
    public class WorkOrder
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [StringLength(50)]
        public string WorkOrderNo { get; set; }
        [StringLength(50)]
        public string BatchNo { get; set; }
        [StringLength(50)]
        public string ProductionLine { get; set; }
        [StringLength(50)]
        public string Operator { get; set; }

        public int PlannedQuantity { get; set; }          // 计划测试电芯/PACK 数量
        public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Open;

        public DateTime CreateTime { get; set; } = DateTime.Now;
        public DateTime? CompletedTime { get; set; }

        [StringLength(300)]
        public string Remark { get; set; }
    }
}
