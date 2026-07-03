using System;
using System.ComponentModel.DataAnnotations;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 操作审计日志 —— 记录谁在何时对哪个对象做了什么修改，用于追溯配置/工步/账号变更。
    /// </summary>
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public int UserId { get; set; }
        [StringLength(50)]
        public string Username { get; set; }

        /// <summary>操作类型：Create/Update/Delete/Login/Start/Stop 等</summary>
        [StringLength(30)]
        public string Action { get; set; }

        /// <summary>被操作对象类型：Cabinet/TestRecipe/User/Role/Channel 等</summary>
        [StringLength(50)]
        public string EntityType { get; set; }

        /// <summary>被操作对象标识（Id 或名称）</summary>
        [StringLength(100)]
        public string EntityId { get; set; }

        /// <summary>人类可读的操作摘要</summary>
        [StringLength(500)]
        public string Detail { get; set; }
    }
}
