using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BatteryAging.Data.Context
{
    public enum DbProvider { Sqlite, MySql }

    /// <summary>
    /// 由配置 Database:Provider 决定 EF Core 使用 SQLite 还是 MySQL，
    /// 把"选库"逻辑集中到一处，运行期(App)与设计期(Factory)共用。
    /// </summary>
    public static class DbProviderExtensions
    {
        // ServerVersion.AutoDetect 会真正连库，缓存避免每次构造 Context 都连一次
        private static ServerVersion _mysqlVersion;
        private const string MigrationsAsm = "BatteryAging";

        public static DbProvider GetDbProvider(this IConfiguration config)
        {
            var name = config["Database:Provider"] ?? "Sqlite";
            return name.Trim().ToLowerInvariant() switch
            {
                "mysql" => DbProvider.MySql,
                "mariadb" => DbProvider.MySql,
                _ => DbProvider.Sqlite
            };
        }

        public static void UseConfiguredDatabase(this DbContextOptionsBuilder options, IConfiguration config)
        {
            if (config.GetDbProvider() == DbProvider.MySql)
            {
                var conn = config.GetConnectionString("BatteryDatabaseMySql")
                    ?? throw new InvalidOperationException("缺少连接串 ConnectionStrings:BatteryDatabaseMySql");

                var verCfg = config["Database:MySqlServerVersion"];
                _mysqlVersion ??= string.IsNullOrWhiteSpace(verCfg)
                    ? ServerVersion.AutoDetect(conn)
                    : ServerVersion.Parse(verCfg);

                options.UseMySql(conn, _mysqlVersion,
                    o => o.MigrationsAssembly(MigrationsAsm)
                          .MigrationsHistoryTable("__ef_migrations_history"));
            }
            else
            {
                var conn = config.GetConnectionString("BatteryDatabase");
                options.UseSqlite(conn, o => o.MigrationsAssembly(MigrationsAsm));
            }
        }
    }
}