using PostNet.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace PostNet.Desktop.ViewModels.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    public static string[] Themes { get; } = ["深色", "浅色", "午夜蓝"];
    [ObservableProperty] private string _theme;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private string _proxyPort;
    [ObservableProperty] private bool _decryptHttps;
    [ObservableProperty] private bool _autoCheckUpdates;
    [ObservableProperty] private string _error = "";
    public event Action? Saved;
    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        _theme = settings.Theme switch { "Light" => "浅色", "Midnight" => "午夜蓝", _ => "深色" };
        _closeToTray = settings.CloseToTray;
        _proxyPort = settings.ProxyPort.ToString();
        _decryptHttps = settings.DecryptHttps;
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
        _settings.CloseToTray = CloseToTray;
        _settings.ProxyPort = port;
        _settings.DecryptHttps = DecryptHttps;
        _settings.AutoCheckUpdates = AutoCheckUpdates;
        _settings.Theme = Theme switch { "浅色" => "Light", "午夜蓝" => "Midnight", _ => "Dark" };
        Saved?.Invoke();
    }
}
