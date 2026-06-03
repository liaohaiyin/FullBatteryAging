using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BatteryAging.Communication;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Data.Context;
using BatteryAging.Services;
using BatteryAging.UI.Pages;
using BatteryAging.UI.Windows;
using BatteryAging.ViewModels;

namespace BatteryAging
{
    public partial class App : Application
    {
        public static ServiceProvider Services { get; private set; }
        private static Dispatcher _dispatcher;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _dispatcher = Dispatcher;

            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var dbDir = Path.Combine(AppContext.BaseDirectory, "db");
            Directory.CreateDirectory(dbDir);

            var services = new ServiceCollection();
            ConfigureServices(services, config);
            Services = services.BuildServiceProvider();

            // ── 初始化数据库 + 启用 WAL ──
            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BatteryDbContext>();
                db.Database.Migrate();
                try { db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); } catch { }
                try { db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;"); } catch { }
            }

            // ── 语言服务（构造即加载资源字典；必须在显示任何窗口之前）──
            _ = Services.GetRequiredService<ILanguageService>();

            // ── 鉴权初始化（建内置角色 + 默认 admin）──
            Services.GetRequiredService<IAuthService>().InitializeAsync().GetAwaiter().GetResult();

            // ── 登录闸门 ──
            var loginVm = Services.GetRequiredService<LoginWindowViewModel>();
            var login = new LoginWindow(loginVm);
            if (login.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            // ── 初始化机柜与通道 ──
            InitializeChannelsAsync(config).GetAwaiter().GetResult();

            var main = Services.GetRequiredService<MainWindow>();
            main.WindowState = WindowState.Maximized;
            main.ShowDialog();
            Shutdown();
        }

        private static async Task InitializeChannelsAsync(IConfiguration config)
        {
            var dataService = Services.GetRequiredService<IDataService>();
            var channelMgr = Services.GetRequiredService<ChannelManager>();

            var cabinets = await dataService.GetAllCabinetsAsync();
            if (cabinets.Count == 0)
            {
                var channelCount = config.GetValue<int>("BatteryChannel:ChannelCount", 8);
                var def = new Cabinet
                {
                    Name = "机柜1",
                    CabinetIndex = 1,
                    DriverType = DriverType.Simulator,
                    ConnectionType = ConnectionType.Simulation,
                    ChannelStartIndex = 1,
                    ChannelCount = channelCount,
                    IsEnabled = true
                };
                await dataService.SaveCabinetAsync(def);
                cabinets = new List<Cabinet> { def };
            }
            channelMgr.InitializeFromCabinets(cabinets);
        }

        private void ConfigureServices(IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IConfiguration>(config);

            services.AddDbContextFactory<BatteryDbContext>(opts =>
            {
                var conn = config.GetConnectionString("BatteryDatabase");
                opts.UseSqlite(conn);
            });
            services.AddDbContext<BatteryDbContext>(opts =>
            {
                var conn = config.GetConnectionString("BatteryDatabase");
                opts.UseSqlite(conn);
            }, ServiceLifetime.Scoped);

            services.AddLogging(b => { b.AddDebug(); b.SetMinimumLevel(LogLevel.Information); });

            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IBatteryAnalyticsService, BatteryAnalyticsService>();
            services.AddSingleton<ChannelManager>();

            // ── 新增：鉴权 + 语言 ──
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<ILanguageService, LanguageService>();
            services.AddSingleton<LoginWindowViewModel>();
            services.AddSingleton<UserManagementViewModel>();

            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<TestExecutionViewModel>();
            services.AddSingleton<RecipeEditorViewModel>();
            services.AddSingleton<DataQueryViewModel>();
            services.AddSingleton<BatchAnalysisViewModel>();
            services.AddSingleton<CabinetManagerViewModel>();
            services.AddSingleton<ComparisonViewModel>();

            services.AddTransient<MainWindow>();
            services.AddTransient<TestExecutionPage>();
            services.AddTransient<RecipeEditorPage>();
            services.AddTransient<DataQueryPage>();
            services.AddTransient<BatchAnalysisPage>();
            services.AddTransient<CabinetManagerPage>();
            services.AddTransient<ComparisonPage>();
            services.AddTransient<UserManagementPage>();   // 新增
        }

        public static void UIDispatch(Action action)
        {
            if (_dispatcher == null || _dispatcher.CheckAccess()) action();
            else _dispatcher.BeginInvoke(action);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { Services?.GetService<ChannelManager>()?.StopAll(); } catch { }
            Services?.Dispose();
            base.OnExit(e);
        }
    }
}
