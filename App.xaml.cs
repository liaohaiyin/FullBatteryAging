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
                db.Database.EnsureCreated();
                // SQLite WAL 模式：崩溃时不丢数据，支持掉电续测
                try { db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); } catch { }
                try { db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;"); } catch { }
            }

            // ── 初始化机柜与通道 ──
            InitializeChannelsAsync(config).GetAwaiter().GetResult();

            var main = Services.GetRequiredService<MainWindow>();
            main.WindowState = WindowState.Maximized;
            main.ShowDialog();
            Shutdown();
        }

        /// <summary>从数据库加载机柜配置初始化；若无机柜则按 appsettings 创建默认</summary>
        private static async Task InitializeChannelsAsync(IConfiguration config)
        {
            var dataService = Services.GetRequiredService<IDataService>();
            var channelMgr = Services.GetRequiredService<ChannelManager>();
            var sampleMs = config.GetValue<int>("BatteryChannel:SamplingIntervalMs", 1000);

            var cabinets = await dataService.GetAllCabinetsAsync();
            if (cabinets.Count == 0)
            {
                var channelCount = config.GetValue<int>("BatteryChannel:ChannelCount", 8);
                var def = new Cabinet
                {
                    Name = "默认模拟机柜",
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
            channelMgr.InitializeFromCabinets(cabinets, sampleMs);
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

            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<TestExecutionViewModel>();
            services.AddSingleton<RecipeEditorViewModel>();
            services.AddSingleton<DataQueryViewModel>();
            services.AddSingleton<BatchAnalysisViewModel>();
            services.AddSingleton<CabinetManagerViewModel>();

            services.AddTransient<MainWindow>();
            services.AddTransient<TestExecutionPage>();
            services.AddTransient<RecipeEditorPage>();
            services.AddTransient<DataQueryPage>();
            services.AddTransient<BatchAnalysisPage>();
            services.AddTransient<CabinetManagerPage>();
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
