using PostNet.Core.Models;
using PostNet.Desktop.Controls;
using PostNet.Desktop.Services;
using PostNet.Desktop.ViewModels.Settings;
using PostNet.Desktop.ViewModels.Tabs;
using PostNet.Desktop.ViewModels.Workspace;
using PostNet.Desktop.ViewModels.Tools;
using PostNet.Desktop.Views;
using PostNet.Desktop.Views.Tabs;
using PostNet.Infrastructure.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PostNet.Desktop.Tests;

public partial class UiFeedbackTests
{
    [AvaloniaFact]
    public void HeaderRowsPreserveDuplicateFieldsColonValuesAndRawLineEndings()
    {
        const string raw = "Content-Type: application/json\r\nSet-Cookie: a=1; Path=/\r\nSet-Cookie: b=2; Path=/\r\nLocation: https://example.test:8443/a\r\nDate: Thu, 12 Nov 2026 08:30:00 GMT\r\nX-Empty:\r\n\r\n";
        var view = new ResponseHeadersViewModel { RawText = raw };
        Assert.True(view.IsTableView); Assert.False(view.IsTextView);
        Assert.Equal(6, view.Rows.Count);
        Assert.Equal(2, view.Rows.Count(x => x.Key == "Set-Cookie"));
        Assert.Equal("https://example.test:8443/a", view.Rows[3].Value);
        Assert.Equal("Thu, 12 Nov 2026 08:30:00 GMT", view.Rows[4].Value);
        Assert.Empty(view.Rows[5].Value); Assert.Equal(raw, view.RawText);
        view.IsTextView = true; view.RawText = "X-New: new";
        Assert.True(view.IsTextView); Assert.Equal("X-New", Assert.Single(view.Rows).Key);
        view.RawText = ""; Assert.False(view.HasHeaders); Assert.Empty(view.Rows);
    }

    [AvaloniaFact]
    public async Task CompactHeaderViewSwitchesWithTheMouseAndUpdatesForHistoryWithoutEditingTheRequest()
    {
        var model = CreateModel(); await model.InitializeAsync();
        var tab = model.SelectedTab!;
        const string raw = "Content-Type: application/json; charset=utf-8\r\nCache-Control: no-cache\r\nLocation: https://example.test:8443/users\r\nSet-Cookie: session=abc; HttpOnly; Secure\r\nX-Trace: " + "long-value-";
        tab.ShowHistory(new() { Headers = raw + new string('a', 300), Body = "{\"success\":true}", StatusCode = 200, StatusText = "OK", ContentType = "application/json" });
        var dirty = tab.IsDirty;
        var window = new MainWindow { DataContext = model, Width = 1100, Height = 700 }; window.Show();
        try
        {
            Render();
            var http = window.GetVisualDescendants().OfType<HttpRequestView>().Single();
            http.FindControl<TabControl>("HttpResponseTabs")!.SelectedIndex = 1; Render();
            var headers = http.GetVisualDescendants().OfType<ResponseHeadersView>().Single();
            var table = headers.FindControl<ItemsControl>("ResponseHeadersTable")!;
            Assert.True(table.IsEffectivelyVisible); Assert.Equal(5, table.ItemCount);
            SaveFrame(window, "compact-response-headers.png");
            Click(headers.FindControl<RadioButton>("HeadersTextMode")!); Render();
            Assert.True(tab.ResponseHeaderView.IsTextView); Assert.False(table.IsEffectivelyVisible);
            var text = headers.FindControl<CodeEditor>("ResponseHeadersText")!;
            Assert.True(text.IsEffectivelyVisible); Assert.Equal(tab.ResponseHeaders, text.Text); Assert.True(text.IsReadOnly);
            SaveFrame(window, "compact-response-headers-text.png");
            tab.ShowHistory(new() { Headers = "X-New: replacement", StatusCode = 201 }); Render();
            Assert.Equal("X-New: replacement", text.Text); Assert.True(tab.ResponseHeaderView.IsTextView);
            Click(headers.FindControl<RadioButton>("HeadersTableMode")!); Render();
            Assert.True(table.IsEffectivelyVisible); Assert.Equal("replacement", Assert.Single(tab.ResponseHeaderView.Rows).Value);
            Assert.Equal(dirty, tab.IsDirty); Assert.Equal("X-New: replacement", tab.ResponseHeaders);
            await model.NewHttpCommand.ExecuteAsync(null);
            Assert.NotSame(tab.ResponseHeaderView, model.SelectedTab!.ResponseHeaderView);
            Assert.True(model.SelectedTab.ResponseHeaderView.IsTableView);
        }
        finally { await model.ShutdownAsync(); window.Close(); }

        void Render() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        }
    }

    [AvaloniaFact]
    public async Task ThreeThemesUpdateExistingEditorsCheckboxesAndHeadersAndPersistMidnightSelection()
    {
        var store = new SqliteWorkspaceStore(Path.Combine(Path.GetTempPath(), "apinet-appearance-" + Guid.NewGuid().ToString("N")));
        var model = CreateModel(store: store); await model.InitializeAsync();
        var tab = model.SelectedTab!; tab.Name = "查询用户列表"; tab.Url = "{{baseUrl}}/users?page=1&limit=20";
        tab.HeaderTable.Items.Last().Key = "Authorization"; tab.HeaderRows.Last().Value = "Bearer {{token}}"; tab.HeaderRows.Last().Description = "访问凭证";
        tab.HeaderTable.Items.Last().Key = "Accept"; tab.HeaderRows.Last().Value = "application/json"; tab.HeaderRows.Last().Description = "响应格式";
        tab.HeaderTable.Items.Last().Key = "X-Debug"; tab.HeaderRows.Last().Value = "true"; tab.HeaderRows.Last().Enabled = false;
        tab.ShowHistory(new() { Headers = "Content-Type: application/json; charset=utf-8\nDate: Thu, 12 Nov 2026 08:30:00 GMT\nCache-Control: no-cache\nX-Request-Id: 8927f4c0\nServer: nginx", Body = "{\"data\":[{\"id\":1,\"name\":\"ApiNet\"}],\"total\":1}", ContentType = "application/json", StatusCode = 200, StatusText = "OK", DurationMs = 128, Size = 268 });
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var http = window.GetVisualDescendants().OfType<HttpRequestView>().Single();
            http.FindControl<TabControl>("HttpRequestTabs")!.SelectedIndex = 1;
            http.FindControl<TabControl>("HttpResponseTabs")!.SelectedIndex = 1;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
            var initialItem = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(item => item.DataContext is WorkspaceNodeViewModel node && node.Id == tab.Id);
            var label = initialItem.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == ((WorkspaceNodeViewModel)initialItem.DataContext!).Name);
            var selectPoint = label.TranslatePoint(new Point(12, label.Bounds.Height / 2), window)!.Value;
            window.MouseDown(selectPoint, MouseButton.Left); window.MouseUp(selectPoint, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(model.HasWorkspaceSelection);
            var colors = new HashSet<Color>();
            foreach (var theme in new[] { "Dark", "Light", "Midnight" })
            {
                var settings = new SettingsViewModel(model.Workspace.Settings) { Theme = theme switch { "Dark" => "深色", "Light" => "浅色", _ => "午夜蓝" } };
                settings.SaveCommand.Execute(null); await model.SettingsCommand.ExecuteAsync(null);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                colors.Add(Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
                var table = http.GetVisualDescendants().OfType<ParameterTable>().Single(x => x.Title == "Headers");
                var checkbox = table.GetVisualDescendants().OfType<CheckBox>().Single(x => x.DataContext == tab.HeaderRows[0]);
                var box = checkbox.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "CheckBoxBorder");
                Assert.Equal(ThemeService.Brush("Accent").ToString(), box.Background!.ToString());
                var editor = table.GetVisualDescendants().OfType<CodeEditor>().First(x => x.Watermark == "Key");
                Assert.Equal(ThemeService.Brush("TableText").ToString(), editor.Editor.Foreground!.ToString());
                Assert.True(Application.Current!.TryGetResource("SystemAccentColor", Application.Current.ActualThemeVariant, out var fluentAccent));
                Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(ThemeService.Brush("Accent")).Color, Assert.IsType<Color>(fluentAccent));
                var selected = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(item => item.DataContext is WorkspaceNodeViewModel node && node.Id == tab.Id);
                Assert.Same(initialItem, selected);
                Assert.True(selected.IsSelected);
                var row = selected.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_LayoutRoot");
                Assert.Equal(ThemeService.Brush("AccentSurface").ToString(), row.Background!.ToString());
                Assert.Equal(new CornerRadius(4), row.CornerRadius);
                Assert.InRange(selected.Bounds.Height, 27, 29);
                var hoverPoint = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
                window.MouseMove(hoverPoint); Dispatcher.UIThread.RunJobs();
                Assert.Equal(ThemeService.Brush("AccentSurface").ToString(), row.Background!.ToString());
                Assert.Equal(ThemeService.Brush("Accent").ToString(), row.BorderBrush!.ToString());
                window.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs();
                SaveFrame(window, $"workbench-{theme.ToLowerInvariant()}-compact.png");
                model.ActivePanel = "工具";
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var toolList = window.GetVisualDescendants().OfType<ListBox>().Single(list => list.ItemsSource == ToolsViewModel.Kinds);
                var toolItem = Assert.IsType<ListBoxItem>(toolList.ContainerFromIndex(toolList.SelectedIndex));
                var toolPresenter = toolItem.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(presenter => presenter.Name == "PART_ContentPresenter");
                Assert.Equal(ThemeService.Brush("AccentSurface").ToString(), toolPresenter.Background!.ToString());
                Assert.Equal(new CornerRadius(4), toolPresenter.CornerRadius);
                SaveFrame(window, $"navigation-tools-{theme.ToLowerInvariant()}.png");
                model.ActivePanel = "工作区";
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(3, colors.Count); Assert.Equal(ThemeService.Midnight, window.ActualThemeVariant);
            var headerTable = http.GetVisualDescendants().OfType<ParameterTable>().Single(x => x.Title == "Headers");
            var all = headerTable.GetVisualDescendants().OfType<CheckBox>().Single(x => x.DataContext == tab.HeaderTable);
            Assert.Null(all.IsChecked);
            var point = all.TranslatePoint(new Point(all.Bounds.Width / 2, all.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.All(tab.HeaderRows, row => Assert.True(row.Enabled));
            all.Focus(); window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.All(tab.HeaderRows, row => Assert.False(row.Enabled));
            tab.HeaderRows[0].Enabled = true; Assert.Null(all.IsChecked);
            await model.SaveDraftsAsync(); Assert.Equal("Midnight", (await store.LoadAsync())!.Settings.Theme);
            Assert.Equal("午夜蓝", new SettingsViewModel((await store.LoadAsync())!.Settings).Theme);
            await model.ToggleThemeCommand.ExecuteAsync(null); Assert.Equal("Dark", model.Workspace.Settings.Theme);
        }
        finally { ThemeService.Apply("Dark"); await model.ShutdownAsync(); window.Close(); }
    }
}
