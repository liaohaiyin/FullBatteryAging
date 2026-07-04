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
            ScottPlot.Fonts.Default = "微软雅黑";

            // ── 初始化数据库 + 启用 WAL ──
            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BatteryDbContext>();
                db.Database.Migrate();
                if (config.GetDbProvider() == DbProvider.Sqlite)
                {
                    try { db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); } catch { }
                    try { db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;"); } catch { }
                }
            }

            // ── 语言服务（构造即加载资源字典；必须在显示任何窗口之前）──
            var language = Services.GetRequiredService<ILanguageService>();
            Core.EnumHelper.IsEnglish = () => language.CurrentLanguage?.
                StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
            // ── 主题服务（构造即套用持久化主题）──
            _ = Services.GetRequiredService<IThemeService>();
            var license = Services.GetRequiredService<ILicenseService>();            
            var licStatus = license.CheckCurrentLicense();
            if (!licStatus.IsValid)
            {
                var licVm = Services.GetRequiredService<LicenseWindowViewModel>();
                var licWin = new LicenseWindow(licVm);
                if (licWin.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            // ── 鉴权初始化（建内置角色 + 默认 admin）──
            Task.Run(() => Services.GetRequiredService<IAuthService>().InitializeAsync()).GetAwaiter().GetResult();

            // ── 登录闸门 ──
            var loginVm = Services.GetRequiredService<LoginWindowViewModel>();
            var login = new LoginWindow(loginVm);
            if (login.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            // ── 初始化机柜与通道 ──
            Task.Run(() => InitializeChannelsAsync(config)).GetAwaiter().GetResult();

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
                    ConnectionType = ConnectionType.Tcp,
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

            services.AddDbContextFactory<BatteryDbContext>(opts => opts.UseConfiguredDatabase(config));
            services.AddDbContext<BatteryDbContext>(opts => opts.UseConfiguredDatabase(config), ServiceLifetime.Scoped);

            services.AddLogging(b => { b.AddDebug(); b.SetMinimumLevel(LogLevel.Information); });


            services.AddLogging(b => { b.AddDebug(); b.SetMinimumLevel(LogLevel.Information); });

            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IBatteryAnalyticsService, BatteryAnalyticsService>();
            services.AddSingleton<IReportExportService, ReportExportService>();
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<ILanguageService, LanguageService>();
            services.AddSingleton<ILicenseService, LicenseService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ChannelManager>();
            services.AddSingleton<Services.Mes.IMesService, Services.Mes.RestMesService>();

            services.AddSingleton<LoginWindowViewModel>();
            services.AddSingleton<UserManagementViewModel>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<TestExecutionViewModel>();
            services.AddSingleton<RecipeEditorViewModel>();
            services.AddSingleton<DataQueryViewModel>();
            services.AddSingleton<BatchAnalysisViewModel>();
            services.AddSingleton<CabinetManagerViewModel>();
            services.AddSingleton<ComparisonViewModel>();
            services.AddSingleton<AuditLogViewModel>();
            services.AddSingleton<CalibrationViewModel>();
            services.AddSingleton<WorkOrderViewModel>();
            services.AddSingleton<UtilizationViewModel>();
            services.AddSingleton<CellHeatmapViewModel>();
            services.AddTransient<LicenseWindowViewModel>();

            services.AddTransient<MainWindow>();
            services.AddTransient<TestExecutionPage>();
            services.AddTransient<RecipeEditorPage>();
            services.AddTransient<DataQueryPage>();
            services.AddTransient<BatchAnalysisPage>();
            services.AddTransient<CabinetManagerPage>();
            services.AddTransient<ComparisonPage>();
            services.AddTransient<UserManagementPage>();
            services.AddTransient<AuditLogPage>();
            services.AddTransient<CalibrationPage>();
            services.AddTransient<WorkOrderPage>();
            services.AddTransient<UtilizationPage>();
            services.AddTransient<CellHeatmapPage>();
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
