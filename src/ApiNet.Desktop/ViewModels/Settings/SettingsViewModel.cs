using ApiNet.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace ApiNet.Desktop.ViewModels.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private string _proxyPort;
    [ObservableProperty] private bool _decryptHttps;
    [ObservableProperty] private string _updateUrl;
    [ObservableProperty] private bool _autoCheckUpdates;
    [ObservableProperty] private string _error = "";
    public event Action? Saved;
    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        _closeToTray = settings.CloseToTray;
        _proxyPort = settings.ProxyPort.ToString();
        _decryptHttps = settings.DecryptHttps;
        _updateUrl = settings.UpdateUrl;
        _autoCheckUpdates = settings.AutoCheckUpdates;
    }
    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(ProxyPort, out var port) || port is < 1024 or > 65535)
        {
            Error = "代理端口必须在 1024–65535 之间。";
            return;
        }
        if (UpdateUrl.Length > 0 && (!Uri.TryCreate(UpdateUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
        {
            Error = "更新源需要填写 HTTPS 地址。";
            return;
        }
        _settings.CloseToTray = CloseToTray;
        _settings.ProxyPort = port;
        _settings.DecryptHttps = DecryptHttps;
        _settings.UpdateUrl = UpdateUrl.Trim();
        _settings.AutoCheckUpdates = AutoCheckUpdates;
        Saved?.Invoke();
    }
}
