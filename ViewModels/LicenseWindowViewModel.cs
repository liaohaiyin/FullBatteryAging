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

        public IRelayCommand CopyMachineCodeCommand { get; }
        public IRelayCommand ActivateCommand { get; }
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

            var cur = _license.CheckCurrentLicense();
            IsActivated = cur.IsValid;
            StatusText = cur.IsValid ? "本机已授权" : cur.Message;
            if (cur.Expiry.HasValue)
                ExpiryText = $"有效期至 {cur.Expiry:yyyy-MM-dd}（剩余 {cur.RemainingDays} 天）";
        }

        private void Activate()
        {
            var status = _license.Activate(LicenseKey);
            StatusText = status.Message;
            IsActivated = status.IsValid;
            if (status.IsValid)
            {
                ExpiryText = $"有效期至 {status.Expiry:yyyy-MM-dd}（剩余 {status.RemainingDays} 天）";
                ActivationSucceeded?.Invoke();
            }
        }
    }
}