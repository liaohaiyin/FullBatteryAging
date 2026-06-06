using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BatteryAging.Data.Context
{
    /// <summary>
    /// 设计期(dotnet ef)用：通过 --provider 或环境变量 BA_DB_PROVIDER 选择库，默认 sqlite。
    /// 运行期连接串仍由 App 的 DI + appsettings.json 决定，与此处无关。
    /// </summary>
    public class BatteryDbContextFactory : IDesignTimeDbContextFactory<BatteryDbContext>
    {
        public BatteryDbContext CreateDbContext(string[] args)
        {
            var provider = GetArg(args, "--provider")
                ?? Environment.GetEnvironmentVariable("BA_DB_PROVIDER")
                ?? "sqlite";

            var builder = new DbContextOptionsBuilder<BatteryDbContext>();

            if (provider.Trim().Equals("mysql", StringComparison.OrdinalIgnoreCase))
            {
                var conn = "Server=localhost;Port=3306;Database=battery_aging;Uid=root;Pwd=1234;CharSet=utf8mb4;";
                builder.UseMySql(conn, ServerVersion.AutoDetect(conn),
                    o => o.MigrationsAssembly("BatteryAging")
                          .MigrationsHistoryTable("__ef_migrations_history"));
            }
            else
            {
                builder.UseSqlite("Data Source=db/BatteryAging.db",
                    o => o.MigrationsAssembly("BatteryAging"));
            }

            return new BatteryDbContext(builder.Options);
        }

        private static string GetArg(string[] args, string key)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}