using System;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using BatteryAging.UI.Pages;

namespace BatteryAging.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IServiceProvider _services;
        private readonly DispatcherTimer _clockTimer;

        [ObservableProperty] private Page _currentPage;
        [ObservableProperty] private string _currentTime;
        [ObservableProperty] private string _version = "v1.0.0";
        [ObservableProperty] private string _navTitle = "测试执行";

        public IRelayCommand NavExecutionCommand { get; }
        public IRelayCommand NavRecipeCommand { get; }
        public IRelayCommand NavDataCommand { get; }
        public IRelayCommand NavBatchCommand { get; }
        public IRelayCommand NavCabinetCommand { get; }
        public IRelayCommand NavAboutCommand { get; }
        public IRelayCommand NavCompareCommand { get; }

        public MainWindowViewModel(IServiceProvider services)
        {
            _services = services;

            NavExecutionCommand = new RelayCommand(() =>
            {
                CurrentPage = _services.GetRequiredService<TestExecutionPage>();
                NavTitle = "测试执行";
            });
            NavRecipeCommand = new RelayCommand(() =>
            {
                CurrentPage = _services.GetRequiredService<RecipeEditorPage>();
                NavTitle = "工步编辑";
            });
            NavDataCommand = new RelayCommand(() =>
            {
                CurrentPage = _services.GetRequiredService<DataQueryPage>();
                NavTitle = "数据查询";
            });
            NavBatchCommand = new RelayCommand(() =>
            {
                CurrentPage = _services.GetRequiredService<BatchAnalysisPage>();
                NavTitle = "批次分析";
            });
            NavCompareCommand = new RelayCommand(() =>
            {
                CurrentPage = _services.GetRequiredService<ComparisonPage>();
                NavTitle = "对比分析";
            });
            NavCabinetCommand = new RelayCommand(() =>
            {
                CurrentPage = _services.GetRequiredService<CabinetManagerPage>();
                NavTitle = "机柜管理";
            });
            NavAboutCommand = new RelayCommand(() =>
            {
                System.Windows.MessageBox.Show(
                    "锂电池充放电老化上位机\n版本: v1.0.0\n基于 .NET 9 + WPF + MaterialDesign",
                    "关于", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });

            NavExecutionCommand.Execute(null);

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _clockTimer.Start();
            CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
