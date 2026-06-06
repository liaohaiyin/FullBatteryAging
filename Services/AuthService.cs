using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BatteryAging.Core.Models;
using BatteryAging.Data.Context;

namespace BatteryAging.Services
{
    public class AuthService : IAuthService
    {
        private readonly IDbContextFactory<BatteryDbContext> _dbFactory;
        private readonly ILogger<AuthService> _logger;

        private UserSession _currentSession = UserSession.Empty;

        public UserSession CurrentSession => _currentSession;
        public bool IsAuthenticated => _currentSession.IsAuthenticated;

        public event EventHandler<UserSession> SessionChanged;

        // 登录失败锁定策略
        private const int MaxFailCount = 5;
        private const int LockMinutes = 30;
        private const int PbkdfIterations = 100_000;

        private const string DeveloperUsername = "developer";
        private const string DeveloperPassword = "dev@BatteryAging#2026";
        public AuthService(IDbContextFactory<BatteryDbContext> dbFactory, ILogger<AuthService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        // ══════════════════════════════════════════════════════════════════
        //  初始化
        // ══════════════════════════════════════════════════════════════════
        public async Task InitializeAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await db.Database.MigrateAsync();

            var systemRoles = new[]
            {
                Role.CreateAdmin(),
                Role.CreateEngineer(),
                Role.CreateOperator(),
                Role.CreateViewer(),
            };

            foreach (var role in systemRoles)
            {
                // 兼容中/英名称：优先按 IsSystem + 权限掩码匹配
                var existing = await db.Roles
                    .FirstOrDefaultAsync(r => r.IsSystem && (r.PermissionMask == role.PermissionMask || r.Name == role.Name));

                if (existing == null)
                {
                    db.Roles.Add(role);
                    _logger.LogInformation("创建系统角色: {Role}", role.Name);
                }
                else
                {
                    existing.PermissionMask = role.PermissionMask;
                    existing.Description = role.Description;
                    existing.Name = role.Name;
                }
            }

            await db.SaveChangesAsync();

            // 默认 admin（按权限掩码定位管理员角色，避免依赖语言）
            var adminRole = await db.Roles.FirstAsync(r => r.IsSystem && r.PermissionMask == (long)Permission.Role_Admin);
            if (!await db.Users.AnyAsync(u => u.Username == "admin"))
            {
                var salt = GenerateSalt();
                db.Users.Add(new User
                {
                    Username = "admin",
                    DisplayName = "系统管理员",
                    PasswordHash = HashPassword("admin123", salt),
                    Salt = salt,
                    RoleId = adminRole.Id,
                    IsSystem = true,
                    IsActive = true
                });
                await db.SaveChangesAsync();
                _logger.LogWarning("已创建默认管理员账号 admin / admin123，请及时修改密码");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  认证
        // ══════════════════════════════════════════════════════════════════
        public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, "用户名和密码不能为空");

            if (username == DeveloperUsername && password == DeveloperPassword)
            {
                var devSession = new UserSession
                {
                    IsAuthenticated = true,
                    UserId = -1,
                    Username = DeveloperUsername,
                    DisplayName = "开发者",
                    RoleName = "Developer",
                    Permissions = Permission.Role_Admin,
                    IsDeveloper = true,
                    LoginTime = DateTime.Now,
                };
                _currentSession = devSession;
                SessionChanged?.Invoke(this, devSession);
                _logger.LogWarning("开发者账号登录");
                return (true, "登录成功");
            }

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var user = await db.Users
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                {
                    _logger.LogWarning("登录失败：用户不存在 [{User}]", username);
                    return (false, "用户名或密码错误");
                }

                if (!user.IsActive)
                    return (false, "账号已被禁用，请联系管理员");

                if (user.IsLocked)
                {
                    var remaining = (int)(user.LockUntil.Value - DateTime.Now).TotalMinutes + 1;
                    return (false, $"账号已锁定，请 {remaining} 分钟后重试");
                }

                var hash = HashPassword(password, user.Salt);
                if (hash != user.PasswordHash)
                {
                    user.LoginFailCount++;
                    if (user.LoginFailCount >= MaxFailCount)
                    {
                        user.LockUntil = DateTime.Now.AddMinutes(LockMinutes);
                        _logger.LogWarning("账号已锁定: {User}", username);
                    }
                    await db.SaveChangesAsync();
                    var remain = MaxFailCount - user.LoginFailCount;
                    return (false, remain > 0
                        ? $"用户名或密码错误，还可尝试 {remain} 次"
                        : $"账号已锁定 {LockMinutes} 分钟");
                }

                // 成功
                user.LoginFailCount = 0;
                user.LockUntil = null;
                user.LastLoginTime = DateTime.Now;
                await db.SaveChangesAsync();

                var session = new UserSession
                {
                    IsAuthenticated = true,
                    UserId = user.Id,
                    Username = user.Username,
                    DisplayName = user.DisplayName ?? user.Username,
                    RoleName = user.Role?.Name ?? string.Empty,
                    Permissions = user.Role?.Permissions ?? Permission.None,
                    LoginTime = DateTime.Now,
                };

                _currentSession = session;
                SessionChanged?.Invoke(this, session);
                _logger.LogInformation("用户登录: {User} [{Role}]", username, session.RoleName);
                return (true, "登录成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登录异常");
                return (false, $"登录异常: {ex.Message}");
            }
        }

        public void Logout()
        {
            var user = _currentSession.Username;
            _currentSession = UserSession.Empty;
            SessionChanged?.Invoke(this, UserSession.Empty);
            _logger.LogInformation("用户退出: {User}", user);
        }

        public bool HasPermission(Permission permission) => _currentSession.HasPermission(permission);

        public bool HasAnyPermission(params Permission[] permissions)
        {
            if (!_currentSession.IsAuthenticated) return false;
            foreach (var p in permissions)
                if ((_currentSession.Permissions & p) == p) return true;
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        //  用户管理
        // ══════════════════════════════════════════════════════════════════
        public async Task<List<User>> GetUsersAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Users.Include(u => u.Role).OrderBy(u => u.Id).ToListAsync();
        }

        public async Task<User> GetUserAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<(bool Success, string Message)> CreateUserAsync(
            string username, string password, string displayName, string email, int roleId)
        {
            if (string.IsNullOrWhiteSpace(username)) return (false, "用户名不能为空");
            if (string.IsNullOrWhiteSpace(password)) return (false, "密码不能为空");
            if (password.Length < 6) return (false, "密码长度不能少于 6 位");

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                if (await db.Users.AnyAsync(u => u.Username == username))
                    return (false, $"用户名 '{username}' 已存在");
                if (!await db.Roles.AnyAsync(r => r.Id == roleId))
                    return (false, "角色不存在");

                var salt = GenerateSalt();
                db.Users.Add(new User
                {
                    Username = username.Trim(),
                    DisplayName = displayName?.Trim() ?? username,
                    Email = email?.Trim(),
                    PasswordHash = HashPassword(password, salt),
                    Salt = salt,
                    RoleId = roleId,
                    IsActive = true,
                    CreateTime = DateTime.Now,
                });
                await db.SaveChangesAsync();
                return (true, "用户创建成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建用户失败");
                return (false, $"创建失败: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> UpdateUserAsync(
            int id, string displayName, string email, int roleId, bool isActive)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var user = await db.Users.FindAsync(id);
                if (user == null) return (false, "用户不存在");

                // 禁止禁用/降权唯一管理员
                if (user.IsSystem && (!isActive || roleId != user.RoleId))
                {
                    var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.IsSystem && r.PermissionMask == (long)Permission.Role_Admin);
                    if (adminRole != null && user.RoleId == adminRole.Id)
                    {
                        var adminCount = await db.Users.CountAsync(u => u.RoleId == adminRole.Id && u.IsActive);
                        if (adminCount <= 1)
                            return (false, "系统中必须保留至少一个有效管理员账号");
                    }
                }

                if (!await db.Roles.AnyAsync(r => r.Id == roleId))
                    return (false, "角色不存在");

                user.DisplayName = displayName?.Trim() ?? user.DisplayName;
                user.Email = email?.Trim();
                user.RoleId = roleId;
                user.IsActive = isActive;
                await db.SaveChangesAsync();
                return (true, "用户信息已更新");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户失败");
                return (false, $"更新失败: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> ChangePasswordAsync(
            int id, string newPassword, bool requireOldPassword = false, string oldPassword = null)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return (false, "新密码长度不能少于 6 位");

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var user = await db.Users.FindAsync(id);
                if (user == null) return (false, "用户不存在");

                if (requireOldPassword)
                {
                    if (string.IsNullOrEmpty(oldPassword)) return (false, "请输入当前密码");
                    if (HashPassword(oldPassword, user.Salt) != user.PasswordHash)
                        return (false, "当前密码错误");
                }

                var salt = GenerateSalt();
                user.Salt = salt;
                user.PasswordHash = HashPassword(newPassword, salt);
                await db.SaveChangesAsync();
                return (true, "密码修改成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修改密码失败");
                return (false, $"操作失败: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> DeleteUserAsync(int id)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var user = await db.Users.FindAsync(id);
                if (user == null) return (false, "用户不存在");
                if (user.IsSystem) return (false, "系统账号不可删除");
                if (user.Id == _currentSession.UserId) return (false, "不能删除当前登录账号");

                db.Users.Remove(user);
                await db.SaveChangesAsync();
                return (true, "用户已删除");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除用户失败");
                return (false, $"删除失败: {ex.Message}");
            }
        }

        public async Task ResetLoginFailCountAsync(int userId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var user = await db.Users.FindAsync(userId);
            if (user == null) return;
            user.LoginFailCount = 0;
            user.LockUntil = null;
            await db.SaveChangesAsync();
        }

        // ══════════════════════════════════════════════════════════════════
        //  角色管理
        // ══════════════════════════════════════════════════════════════════
        public async Task<List<Role>> GetRolesAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Roles.OrderBy(r => r.Id).ToListAsync();
        }

        public async Task<Role> GetRoleAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Roles.FindAsync(id);
        }

        public async Task<(bool Success, string Message)> CreateRoleAsync(
            string name, string description, Permission permissions)
        {
            if (string.IsNullOrWhiteSpace(name)) return (false, "角色名不能为空");
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                if (await db.Roles.AnyAsync(r => r.Name == name))
                    return (false, $"角色 '{name}' 已存在");

                db.Roles.Add(new Role
                {
                    Name = name.Trim(),
                    Description = description?.Trim(),
                    Permissions = permissions,
                    IsSystem = false,
                    CreateTime = DateTime.Now,
                });
                await db.SaveChangesAsync();
                return (true, "角色创建成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建角色失败");
                return (false, $"创建失败: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> UpdateRoleAsync(
            int id, string name, string description, Permission permissions)
        {
            if (string.IsNullOrWhiteSpace(name)) return (false, "角色名不能为空");
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var role = await db.Roles.FindAsync(id);
                if (role == null) return (false, "角色不存在");
                if (role.IsSystem) return (false, "系统内置角色不可修改名称");

                if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != id))
                    return (false, $"角色名 '{name}' 已被占用");

                role.Name = name.Trim();
                role.Description = description?.Trim();
                role.Permissions = permissions;
                await db.SaveChangesAsync();
                return (true, "角色已更新");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新角色失败");
                return (false, $"更新失败: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> DeleteRoleAsync(int id)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var role = await db.Roles.FindAsync(id);
                if (role == null) return (false, "角色不存在");
                if (role.IsSystem) return (false, "系统内置角色不可删除");
                if (await db.Users.AnyAsync(u => u.RoleId == id))
                    return (false, "该角色下还有用户，无法删除");

                db.Roles.Remove(role);
                await db.SaveChangesAsync();
                return (true, "角色已删除");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除角色失败");
                return (false, $"删除失败: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  密码工具（PBKDF2 / SHA256）
        // ══════════════════════════════════════════════════════════════════
        private static string GenerateSalt()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static string HashPassword(string password, string salt)
        {
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                Encoding.UTF8.GetBytes(salt),
                PbkdfIterations,
                HashAlgorithmName.SHA256,
                32);
            return Convert.ToBase64String(hash);
        }
    }
}
