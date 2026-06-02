using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BatteryAging.Core.Models;

namespace BatteryAging.Services
{
    public interface IAuthService
    {
        // 当前会话
        UserSession CurrentSession { get; }
        bool IsAuthenticated { get; }

        // 事件
        event EventHandler<UserSession> SessionChanged;

        // 认证
        Task<(bool Success, string Message)> LoginAsync(string username, string password);
        void Logout();

        // 权限检查（供 ViewModel 和 Converter 调用）
        bool HasPermission(Permission permission);
        bool HasAnyPermission(params Permission[] permissions);

        // 用户管理
        Task<List<User>> GetUsersAsync();
        Task<User> GetUserAsync(int id);
        Task<(bool Success, string Message)> CreateUserAsync(
            string username, string password, string displayName, string email, int roleId);
        Task<(bool Success, string Message)> UpdateUserAsync(
            int id, string displayName, string email, int roleId, bool isActive);
        Task<(bool Success, string Message)> ChangePasswordAsync(
            int id, string newPassword, bool requireOldPassword = false, string oldPassword = null);
        Task<(bool Success, string Message)> DeleteUserAsync(int id);
        Task ResetLoginFailCountAsync(int userId);

        // 角色管理
        Task<List<Role>> GetRolesAsync();
        Task<Role> GetRoleAsync(int id);
        Task<(bool Success, string Message)> CreateRoleAsync(
            string name, string description, Permission permissions);
        Task<(bool Success, string Message)> UpdateRoleAsync(
            int id, string name, string description, Permission permissions);
        Task<(bool Success, string Message)> DeleteRoleAsync(int id);

        // 初始化（建库 + 内置角色 + 默认 admin）
        Task InitializeAsync();
    }
}
