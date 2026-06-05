using BatteryAging.Core.Models;
using BatteryAging.Services;
using BatteryAging.UI.Pages;
using BatteryAging.UI.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BatteryAging.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IServiceProvider _services;
        private readonly ILanguageService _language;
        private readonly DispatcherTimer _clockTimer;
        private readonly IAuthService _auth;
        private readonly IDialogService _dialogService;

        [ObservableProperty] private Page _currentPage;
        [ObservableProperty] private string _currentTime;
        [ObservableProperty] private string _version = "v1.0.0";
        [ObservableProperty] private UserSession _currentSession;
        [ObservableProperty] private string _currentUser;

        public bool CanExecution => _auth.HasPermission(Permission.TestExecution_View);
        public bool CanRecipe => _auth.HasPermission(Permission.FlowEditor_View);
        public bool CanData => _auth.HasPermission(Permission.DataQuery_View);
        public bool CanStats => _auth.HasPermission(Permission.Statistics_View);
        public bool CanCabinet => _auth.HasPermission(Permission.Settings_CommConfig);
        public bool CanUserMgmt => _auth.HasAnyPermission(Permission.Settings_UserManagement, Permission.Settings_RoleManagement);

        public IRelayCommand NavExecutionCommand { get; }
        public IRelayCommand NavRecipeCommand { get; }
        public IRelayCommand NavDataCommand { get; }
        public IRelayCommand NavBatchCommand { get; }
        public IRelayCommand NavCabinetCommand { get; }
        public IRelayCommand NavAboutCommand { get; }
        public IRelayCommand NavCompareCommand { get; }
        public IRelayCommand NavUserMgmtCommand { get; }
        public IRelayCommand NavLogoutCommand { get; }

        public MainWindowViewModel(IServiceProvider services, ILanguageService language, IAuthService auth, IDialogService dialogService)
        {
            _services = services;
            _language = language;
            _auth = auth;
            _dialogService = dialogService;

            NavExecutionCommand = new RelayCommand(() => CurrentPage = _services.GetRequiredService<TestExecutionPage>());
            NavRecipeCommand = new RelayCommand(() => CurrentPage = _services.GetRequiredService<RecipeEditorPage>());
            NavDataCommand = new RelayCommand(() => CurrentPage = _services.GetRequiredService<DataQueryPage>());
            NavBatchCommand = new RelayCommand(() => CurrentPage = _services.GetRequiredService<BatchAnalysisPage>());
            NavCompareCommand = new RelayCommand(() => CurrentPage = _services.GetRequiredService<ComparisonPage>());
            NavCabinetCommand = new RelayCommand(() => CurrentPage = _services.GetRequiredService<CabinetManagerPage>());
            NavUserMgmtCommand = new RelayCommand(() => CurrentPage = _services.GetRequiredService<UserManagementPage>());
            NavLogoutCommand = new RelayCommand(Logout);
            NavAboutCommand = new RelayCommand(() =>
            {
                MessageBox.Show(
                    _language.GetString("Main_About_Content"),
                    _language.GetString("Main_About_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
            NavExecutionCommand.Execute(null);
            NavigateToFirstAllowed();
            CurrentSession = _auth.CurrentSession;
            _auth.SessionChanged += OnSessionChanged;

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _clockTimer.Start();
            CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            CurrentUser = $"{CurrentSession.DisplayName}  [{CurrentSession.RoleName}]";
        }

        private void NavigateToFirstAllowed()
        {
            if (CanExecution) NavExecutionCommand.Execute(null);
            else if (CanRecipe) NavRecipeCommand.Execute(null);
            else if (CanData) NavDataCommand.Execute(null);
            else if (CanStats) NavBatchCommand.Execute(null);
            else if (CanCabinet) NavCabinetCommand.Execute(null);
            else if (CanUserMgmt) NavUserMgmtCommand.Execute(null);
        }

        private void Logout()
        {
            if (!ShowConfirmation(_language.GetString("Logout_ConfirmationMessage"), _language.GetString("Logout_ConfirmationTitle"))) return;

            _auth.Logout();

            // 重新弹登录窗口
            var loginVm = _services.GetRequiredService<LoginWindowViewModel>();
            var loginWindow = new LoginWindow(loginVm);
            var loggedIn = loginWindow.ShowDialog();

            if (loggedIn != true)
            {
                Application.Current.Shutdown();
                return;
            }

            // 登录成功后刷新主窗口状态
            CurrentSession = _auth.CurrentSession;
            CurrentUser = $"{CurrentSession.DisplayName}  [{CurrentSession.RoleName}]";
            CurrentPage = _services.GetRequiredService<TestExecutionPage>();
        }

        private bool ShowConfirmation(string message, string title)
        {
            try
            {
                return _dialogService.Confirm(message, title);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"{_language.GetString("Main_Nav_Error")}: {ex.Message}");
                return false;
            }
        }

        private void OnSessionChanged(object sender, UserSession session)
        {
            CurrentSession = session;
            CurrentUser = session.IsAuthenticated
                ? $"{session.DisplayName}  [{session.RoleName}]"
                : string.Empty;
        }
    }
}
