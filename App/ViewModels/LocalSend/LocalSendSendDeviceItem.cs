using System.ComponentModel;
using System.Runtime.CompilerServices;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.App.ViewModels.LocalSend;

/// <summary>
/// Wrapper for discovered LocalSend device with per-device PIN input.
/// ponytail: Split out to keep LocalSendSendViewModel.cs under 300 lines limit.
/// </summary>
public sealed class LocalSendSendDeviceItem : INotifyPropertyChanged
{
    private string _pin = string.Empty;
    private bool _isSelected;

    public LocalSendSendDeviceItem(LocalSendDeviceInfo device) => Device = device;

    public LocalSendDeviceInfo Device { get; private set; }

    public string Alias => Device.Alias;
    public string IpAddress => Device.IpAddress;

    public void UpdateDevice(LocalSendDeviceInfo newDev)
    {
        Device = newDev;
        OnPropertyChanged(nameof(Alias));
        OnPropertyChanged(nameof(IpAddress));
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string Pin
    {
        get => _pin;
        set { if (_pin != value) { _pin = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}
