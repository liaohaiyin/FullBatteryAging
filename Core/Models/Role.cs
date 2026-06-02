using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Windows;

namespace BatteryAging.Core.Models
{
    [Table("Roles")]
    public class Role
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(50)]
        public string Name { get; set; }

        [StringLength(200)]
        public string Description { get; set; }

        /// <summary>权限掩码（按位存储）</summary>
        public long PermissionMask { get; set; }

        public bool IsSystem { get; set; } = false;  // 系统内置角色不可删除
        public DateTime CreateTime { get; set; } = DateTime.Now;

        [NotMapped]
        public Permission Permissions
        {
            get => (Permission)PermissionMask;
            set => PermissionMask = (long)value;
        }

        public bool HasPermission(Permission permission) =>
            (Permissions & permission) == permission;

        // ── 预置系统角色 ─────────────────────────────────────────────────
        public static Role CreateAdmin() => new()
        {
            Name = RoleLocalizer.GetString("Role_Admin_Name", "管理员"),
            Description = RoleLocalizer.GetString("Role_Admin_Description", "拥有全部权限"),
            Permissions = Permission.Role_Admin,
            IsSystem = true,
            CreateTime = DateTime.Now,
        };

        public static Role CreateEngineer() => new()
        {
            Name = RoleLocalizer.GetString("Role_Engineer_Name", "工程师"),
            Description = RoleLocalizer.GetString("Role_Engineer_Description", "流程编辑和测试执行"),
            Permissions = Permission.Role_Engineer,
            IsSystem = true,
            CreateTime = DateTime.Now,
        };

        public static Role CreateOperator() => new()
        {
            Name = RoleLocalizer.GetString("Role_Operator_Name", "操作员"),
            Description = RoleLocalizer.GetString("Role_Operator_Description", "仅执行测试和查看数据"),
            Permissions = Permission.Role_Operator,
            IsSystem = true,
            CreateTime = DateTime.Now,
        };

        public static Role CreateViewer() => new()
        {
            Name = RoleLocalizer.GetString("Role_Viewer_Name", "观察者"),
            Description = RoleLocalizer.GetString("Role_Viewer_Description", "只读查看"),
            Permissions = Permission.Role_Viewer,
            IsSystem = true,
            CreateTime = DateTime.Now,
        };
    }

    public static class RoleLocalizer
    {
        /// <summary>从当前 WPF 资源字典读取字符串，失败时返回 fallback。</summary>
        public static string GetString(string key, string fallback)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return fallback;

                string result = fallback;
                app.Dispatcher.Invoke(() =>
                {
                    if (app.Resources[key] is string s && !string.IsNullOrEmpty(s))
                        result = s;
                });
                return result;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
