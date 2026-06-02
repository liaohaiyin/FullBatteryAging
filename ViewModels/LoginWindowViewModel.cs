using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    public class LoginWindowViewModel : ObservableObject, IDisposable
    {
        private readonly IAuthService _authService;
        private readonly ILanguageService _languageService;

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set
            {
                SetProperty(ref _username, value);
                LoginCommand.NotifyCanExecuteChanged();
                ErrorMessage = string.Empty;
            }
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _isLoggingIn;
        public bool IsLoggingIn
        {
            get => _isLoggingIn;
            set
            {
                SetProperty(ref _isLoggingIn, value);
                LoginCommand.NotifyCanExecuteChanged();
            }
        }

        public ObservableCollection<LanguageInfo> AvailableLanguages { get; }

        private LanguageInfo _selectedLanguage;
        public LanguageInfo SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (SetProperty(ref _selectedLanguage, value) && value != null)
                    _ = _languageService.ChangeLanguageAsync(value.Code);
            }
        }

        private string _version =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        public AsyncRelayCommand<object> LoginCommand { get; }

        // 登录成功回调，由 View 触发关窗
        public event Action LoginSucceeded;

        public LoginWindowViewModel(IAuthService authService, ILanguageService languageService)
        {
            _authService = authService;
            _languageService = languageService;

            LoginCommand = new AsyncRelayCommand<object>(LoginAsync, _ => CanLogin());

            AvailableLanguages = new ObservableCollection<LanguageInfo>(_languageService.GetAvailableLanguages());
            _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == _languageService.CurrentLanguage)
                                ?? AvailableLanguages.FirstOrDefault();

            _languageService.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged(object sender, LanguageChangedEventArgs e)
        {
            var match = AvailableLanguages.FirstOrDefault(l => l.Code == e.NewLanguage);
            if (match != null && !ReferenceEquals(match, _selectedLanguage))
            {
                _selectedLanguage = match;
                OnPropertyChanged(nameof(SelectedLanguage));
            }
        }

        private bool CanLogin() => !IsLoggingIn && !string.IsNullOrWhiteSpace(Username);

        private async Task LoginAsync(object passwordBoxParam)
        {
            var password = passwordBoxParam as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(password))
            {
                ErrorMessage = _languageService.GetString("Login_Msg_PasswordRequired");
                return;
            }

            IsLoggingIn = true;
            ErrorMessage = string.Empty;
            try
            {
                var (success, message) = await _authService.LoginAsync(Username.Trim(), password);
                if (success) LoginSucceeded?.Invoke();
                else ErrorMessage = message;
            }
            finally
            {
                IsLoggingIn = false;
            }
        }

        public void Dispose()
        {
            if (_languageService != null)
                _languageService.LanguageChanged -= OnLanguageChanged;
        }
    }
}
