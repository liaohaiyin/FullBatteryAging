using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    public partial class LicenseWindowViewModel : ObservableObject
    {
        private readonly ILicenseService _license;

        [ObservableProperty] private string _machineCode;
        [ObservableProperty] private string _licenseKey = "";
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private string _expiryText = "";
        [ObservableProperty] private bool _isActivated;
        /// <summary>本次是否通过"演示试用"入口生效（而非正式授权码），供 App 启动流程决定是否跳过登录</summary>
        [ObservableProperty] private bool _isDemoActivated;

        public IRelayCommand CopyMachineCodeCommand { get; }
        public IRelayCommand ActivateCommand { get; }
        public IRelayCommand TryDemoCommand { get; }
        public event Action ActivationSucceeded;

        public LicenseWindowViewModel(ILicenseService license)
        {
            _license = license;
            MachineCode = _license.GetMachineCode();

            CopyMachineCodeCommand = new RelayCommand(() =>
            {
                try { Clipboard.SetText(MachineCode); StatusText = "机器码已复制到剪贴板"; } catch { }
            });
            ActivateCommand = new RelayCommand(Activate);
            TryDemoCommand = new RelayCommand(TryDemo);

            var cur = _license.CheckCurrentLicense();
            IsActivated = cur.IsValid;
            IsDemoActivated = cur.IsValid && cur.IsDemo;
            StatusText = cur.IsValid ? (cur.IsDemo ? "演示试用中" : "本机已授权") : cur.Message;
            if (cur.Expiry.HasValue)
                ExpiryText = $"有效期至 {cur.Expiry:yyyy-MM-dd}（剩余 {cur.RemainingDays} 天）";
        }

        private void Activate()
        {
            var status = _license.Activate(LicenseKey);
            StatusText = status.Message;
            IsActivated = status.IsValid;
            IsDemoActivated = false;
            if (status.IsValid)
            {
                ExpiryText = $"有效期至 {status.Expiry:yyyy-MM-dd}（剩余 {status.RemainingDays} 天）";
                ActivationSucceeded?.Invoke();
            }
        }

        private void TryDemo()
        {
            var status = _license.ActivateDemo();
            StatusText = status.Message;
            IsActivated = status.IsValid;
            IsDemoActivated = status.IsValid;
            if (status.IsValid)
            {
                ExpiryText = $"演示试用至 {status.Expiry:yyyy-MM-dd}（剩余 {status.RemainingDays} 天）";
                ActivationSucceeded?.Invoke();
            }
        }
    }
}