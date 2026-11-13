using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using PostNet.Desktop;

namespace PostNet.Android;

[Activity(Label = "Post.NET", Theme = "@style/MyTheme.NoActionBar", Icon = "@drawable/icon", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    private AndroidLifetime? _lifetime;
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.StartLifetime = app => { _lifetime = new AndroidLifetime(this); _lifetime.Initialize(app); };
        return base.CustomizeAppBuilder(builder).WithInterFont().With(new FontManagerOptions { DefaultFamilyName = "avares://PostNet.Android/Assets/Fonts#Noto Sans CJK SC" });
    }
    protected override void OnPause() { _lifetime?.SaveDrafts(); base.OnPause(); }
    protected override void OnResume() { base.OnResume(); _lifetime?.ResumeInstall(); }
    public override void OnBackPressed()
    {
        if (_lifetime?.HandleBack() != true) base.OnBackPressed();
    }
}
