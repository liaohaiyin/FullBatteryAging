using BatteryAging.Services;
using BatteryAging.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BatteryAging.UI.Windows
{
    public partial class LoginWindow : Window
    {
        private readonly LoginWindowViewModel _vm;
        private readonly LoginSettings _settings;
        private bool _pwdVisible;

        public LoginWindow(LoginWindowViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            _settings = LoginSettings.Load();
            DataContext = vm;

            vm.LoginSucceeded += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                RestoreButton();
                try { DialogResult = true; }
                catch (InvalidOperationException) { Close(); }
            }));

            vm.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(vm.IsLoggingIn):
                        Dispatcher.Invoke(() =>
                        {
                            if (vm.IsLoggingIn) ShowLoading();
                            else RestoreButton();
                        });
                        break;

                    case nameof(vm.ErrorMessage):
                        Dispatcher.Invoke(() =>
                        {
                            if (string.IsNullOrEmpty(vm.ErrorMessage))
                            {
                                ErrorPanel.Visibility = Visibility.Collapsed;
                            }
                            else
                            {
                                ErrorText.Text = vm.ErrorMessage;
                                ErrorPanel.Visibility = Visibility.Visible;
                                ShakePanel();
                            }
                        });
                        break;
                }
            };

            if (_settings.RememberUsername)
            {
                _vm.Username = _settings.LastUsername;
                RememberCheck.IsChecked = true;
            }

            Loaded += (_, _) =>
            {
                if (!string.IsNullOrEmpty(UsernameBox.Text)) PasswordBox.Focus();
                else UsernameBox.Focus();
            };
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (GetTemplateChild("PART_MinButton") is Button btnMin)
                btnMin.Click += (_, _) => WindowState = WindowState.Minimized;

            if (GetTemplateChild("PART_CloseButton") is Button btnClose)
                btnClose.Click += (_, _) => Close();
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void EyeButton_Click(object sender, RoutedEventArgs e)
        {
            _pwdVisible = !_pwdVisible;
            if (_pwdVisible)
            {
                PasswordTextBox.Text = PasswordBox.Password;
                PasswordTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                EyeIcon.Text = "🙈";
                PasswordTextBox.Focus();
                PasswordTextBox.SelectionStart = PasswordTextBox.Text.Length;
            }
            else
            {
                PasswordBox.Password = PasswordTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                EyeIcon.Text = "👁";
                PasswordBox.Focus();
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) TryLogin();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e) => TryLogin();

        private void TryLogin()
        {
            var password = _pwdVisible ? PasswordTextBox.Text : PasswordBox.Password;

            _settings.RememberUsername = RememberCheck.IsChecked == true;
            _settings.LastUsername = _settings.RememberUsername ? UsernameBox.Text.Trim() : string.Empty;
            _settings.Save();

            _vm.LoginCommand.Execute(password);
        }

        private void ShowLoading()
        {
            BtnNormal.Visibility = Visibility.Collapsed;
            BtnLoading.Visibility = Visibility.Visible;
            LoginButton.IsEnabled = false;
        }

        private void RestoreButton()
        {
            BtnNormal.Visibility = Visibility.Visible;
            BtnLoading.Visibility = Visibility.Collapsed;
            LoginButton.IsEnabled = true;
        }

        private void ShakePanel()
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(400)) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.00)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(-8, KeyTime.FromPercent(0.15)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(8, KeyTime.FromPercent(0.30)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromPercent(0.45)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(6, KeyTime.FromPercent(0.60)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromPercent(0.75)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.00)));

            var transform = new TranslateTransform();
            ErrorPanel.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}
