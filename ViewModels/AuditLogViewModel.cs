using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Core.Models;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    /// <summary>操作审计日志查询页 —— 只读查看，谁在何时对哪个对象做了什么操作</summary>
    public partial class AuditLogViewModel : ObservableObject
    {
        private readonly IDataService _dataService;
        private readonly IDialogService _dialogService;

        public ObservableCollection<AuditLog> Logs { get; } = new();

        [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-30);
        [ObservableProperty] private DateTime _endDate = DateTime.Today.AddDays(1);
        [ObservableProperty] private string _usernameFilter;
        [ObservableProperty] private string _entityTypeFilter;
        [ObservableProperty] private string _statusText = "";

        [ObservableProperty] private int _totalLogs;
        [ObservableProperty] private int _totalPages = 1;

        private int _currentPage = 1;
        private const int PageSize = 50;
        private readonly RelayCommand _firstPageCommand;
        private readonly RelayCommand _previousPageCommand;
        private readonly RelayCommand _nextPageCommand;
        private readonly RelayCommand _lastPageCommand;

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                    _ = QueryAsync(keepPage: true);
            }
        }

        public IRelayCommand FirstPageCommand => _firstPageCommand;
        public IRelayCommand PreviousPageCommand => _previousPageCommand;
        public IRelayCommand NextPageCommand => _nextPageCommand;
        public IRelayCommand LastPageCommand => _lastPageCommand;

        public IAsyncRelayCommand QueryCommand { get; }

        public AuditLogViewModel(IDataService dataService, IDialogService dialogService)
        {
            _dataService = dataService;
            _dialogService = dialogService;

            QueryCommand = new AsyncRelayCommand(QueryAsync);

            _firstPageCommand = new RelayCommand(() => CurrentPage = 1, () => CurrentPage > 1);
            _previousPageCommand = new RelayCommand(() => CurrentPage = Math.Max(1, CurrentPage - 1), () => CurrentPage > 1);
            _nextPageCommand = new RelayCommand(() => CurrentPage = Math.Min(TotalPages, CurrentPage + 1), () => CurrentPage < TotalPages);
            _lastPageCommand = new RelayCommand(() => CurrentPage = TotalPages, () => CurrentPage < TotalPages);

            _ = QueryAsync();
        }

        private async Task QueryAsync() => await QueryAsync(keepPage: false);

        private async Task QueryAsync(bool keepPage)
        {
            try
            {
                if (!keepPage && _currentPage != 1)
                    SetProperty(ref _currentPage, 1, nameof(CurrentPage));

                var (list, total) = await _dataService.QueryAuditLogsPagedAsync(
                    StartDate, EndDate, UsernameFilter, EntityTypeFilter, _currentPage, PageSize);

                Logs.Clear();
                foreach (var l in list) Logs.Add(l);

                TotalLogs = total;
                TotalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
                UpdatePagingCommands();
                StatusText = $"共 {TotalLogs} 条记录，第 {CurrentPage}/{TotalPages} 页";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"查询失败: {ex.Message}");
            }
        }

        partial void OnTotalPagesChanged(int value) => UpdatePagingCommands();

        private void UpdatePagingCommands()
        {
            _firstPageCommand.NotifyCanExecuteChanged();
            _previousPageCommand.NotifyCanExecuteChanged();
            _nextPageCommand.NotifyCanExecuteChanged();
            _lastPageCommand.NotifyCanExecuteChanged();
        }
    }
}
