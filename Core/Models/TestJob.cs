using System;
using BatteryAging.Core.Enums;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 批量任务队列条目 —— 运行时内存态（不落库），用于"把测试任务批量派发给空闲通道，
    /// 通道用完自动排队"：指定通道号或留空(0)表示派给任意空闲通道。
    /// </summary>
    public class TestJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string RecipeId { get; set; }
        public string RecipeName { get; set; }

        /// <summary>目标通道号，0 = 任意空闲通道</summary>
        public int TargetChannelIndex { get; set; }
        public string BarCode { get; set; }

        public TestJobStatus Status { get; set; } = TestJobStatus.Queued;
        public DateTime EnqueuedTime { get; set; } = DateTime.Now;
        public DateTime? StartedTime { get; set; }

        public string TargetLabel => TargetChannelIndex > 0 ? $"通道{TargetChannelIndex}" : "任意空闲通道";
    }
}
