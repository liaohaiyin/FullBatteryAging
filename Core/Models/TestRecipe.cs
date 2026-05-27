using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 测试方案（工步流程）
    /// </summary>
    public class TestRecipe
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; }                 // 方案名称

        public string Description { get; set; }          // 方案描述

        public string BatteryType { get; set; }          // 电池类型（NCM/LFP/...）

        public double NominalCapacity { get; set; } = 2.6;  // 标称容量 (Ah)

        public double NominalVoltage { get; set; } = 3.7;   // 标称电压 (V)

        public DateTime CreateTime { get; set; } = DateTime.Now;

        public DateTime UpdateTime { get; set; } = DateTime.Now;

        public string Creator { get; set; } = Environment.UserName;

        public List<TestStep> Steps { get; set; } = new();

        public bool IsActive { get; set; } = true;
    }
}
