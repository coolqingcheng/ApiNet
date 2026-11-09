using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace ApiNet.Desktop.Services;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    public static IBrush Brush(string name)
    {
        var app = Application.Current;
        return app?.TryGetResource(name + "Brush", app.ActualThemeVariant, out var value) == true && value is IBrush brush ? brush : Brushes.Gray;
    }

    public static string MethodColor(string method) => Brush(method switch { "WS" => "Purple", "GET" => "Success", "POST" => "Warning", "DELETE" => "Danger", "PUT" or "PATCH" => "Info", _ => "TextSecondary" }).ToString()!;
}
