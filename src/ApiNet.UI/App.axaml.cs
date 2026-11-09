using Avalonia;
using Avalonia.Markup.Xaml;

namespace ApiNet.Desktop;

public partial class App : Application
{
    public static Action<Application>? StartLifetime { get; set; }
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        StartLifetime?.Invoke(this);
        base.OnFrameworkInitializationCompleted();
    }
}
