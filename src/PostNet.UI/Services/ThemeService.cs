using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PostNet.Desktop.Services;

public static class ThemeService
{
    public static ThemeVariant Midnight { get; } = new("Midnight", ThemeVariant.Dark);
    public static string Next(string theme) => theme switch { "Dark" => "Light", "Light" => "Midnight", _ => "Dark" };
    public static void Apply(string theme)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = theme switch { "Light" => ThemeVariant.Light, "Midnight" => Midnight, _ => ThemeVariant.Dark };
    }

    public static IBrush Brush(string name)
    {
        var app = Application.Current;
        return app?.TryGetResource(name + "Brush", app.ActualThemeVariant, out var value) == true && value is IBrush brush ? brush : Brushes.Gray;
    }

    public static string MethodColor(string method) => Brush(method switch { "WS" => "Purple", "GET" => "Success", "POST" => "Warning", "DELETE" => "Danger", "PUT" or "PATCH" => "Info", _ => "TextSecondary" }).ToString()!;
}
