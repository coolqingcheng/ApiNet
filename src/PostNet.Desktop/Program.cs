using Avalonia;
using System;
using Velopack;
using PostNet.Core;

namespace PostNet.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        App.StartLifetime = application => new DesktopLifetime().Initialize(application);
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            var directory = Environment.GetEnvironmentVariable("APINET_DATA_DIR") ?? WorkspaceDataPaths.DefaultDirectory;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "error.log"), $"{DateTimeOffset.Now:O} {eventArgs.ExceptionObject}\n");
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
