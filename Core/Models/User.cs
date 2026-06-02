using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BatteryAging.Core.Models
{
    [Table("Users")]
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(50)]
        public string Username { get; set; }

        [Required, StringLength(200)]
        public string PasswordHash { get; set; }   // PBKDF2 哈希

        [StringLength(100)]
        public string Salt { get; set; }

        [StringLength(100)]
        public string DisplayName { get; set; }

        [StringLength(200)]
        public string Email { get; set; }

        public int RoleId { get; set; }

        [ForeignKey(nameof(RoleId))]
        public Role Role { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsSystem { get; set; } = false;  // admin 不可删除

        public DateTime CreateTime { get; set; } = DateTime.Now;
        public DateTime? LastLoginTime { get; set; }
        public int LoginFailCount { get; set; } = 0;
        public DateTime? LockUntil { get; set; }

        [NotMapped]
        public bool IsLocked => LockUntil.HasValue && LockUntil.Value > DateTime.Now;

        public bool HasPermission(Permission permission) => Role?.HasPermission(permission) == true;
    }
}
