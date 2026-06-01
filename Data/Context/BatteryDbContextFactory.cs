using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BatteryAging.Data.Context
{
    /// <summary>
    /// 仅供 dotnet ef 设计期使用：让迁移工具能在不启动 WPF 的情况下构造 DbContext。
    /// 运行期实际连接串由 App.xaml.cs 的 DI 注入，与此处无关。
    /// </summary>
    public class BatteryDbContextFactory : IDesignTimeDbContextFactory<BatteryDbContext>
    {
        public BatteryDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<BatteryDbContext>()
                .UseSqlite("Data Source=db/BatteryAging.db")
                .Options;
            return new BatteryDbContext(options);
        }
    }
}