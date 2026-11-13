using PostNet.Desktop.Controls;
using PostNet.Desktop.ViewModels.Tools;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace PostNet.Desktop.Tests;

public class LargeTextToolsTests
{
    private static string LargeJson() => "[" + string.Join(',', Enumerable.Repeat("{\"id\":90071992547409931234,\"text\":\"中文 a  b\"}", 200000)) + "]";

    [AvaloniaFact]
    public async Task LargeJsonRunsInTheBackgroundAndCanBeCanceledFromTheUi()
    {
        var tool = new ToolsViewModel { Input = LargeJson() };
        var processing = tool.TransformCommand.ExecuteAsync("format");
        Assert.True(tool.IsBusy); Assert.False(processing.IsCompleted);
        var responded = false;
        Dispatcher.UIThread.Post(() => { responded = true; tool.CancelTransformCommand.Execute(null); });
        Dispatcher.UIThread.RunJobs();
        await processing;
        Assert.True(responded); Assert.False(tool.IsBusy); Assert.Empty(tool.Output);
        Assert.Equal("已取消处理", tool.Status);
        tool.Input = "{\"ok\":true}";
        await tool.TransformCommand.ExecuteAsync("format");
        Assert.Contains('\n', tool.Output);
        tool.UseOutputCommand.Execute(null);
        await tool.TransformCommand.ExecuteAsync("minify");
        Assert.Equal("{\"ok\":true}", tool.Output);
    }

    [AvaloniaFact]
    public async Task EditingInputOrSwitchingToolsPreventsOldWorkOverwritingTheResult()
    {
        var tool = new ToolsViewModel { Input = LargeJson() };
        var processing = tool.TransformCommand.ExecuteAsync("format");
        tool.Input = "{\"new\":1}";
        await processing;
        Assert.Empty(tool.Output); Assert.Contains("输入已修改", tool.Status);
        await tool.TransformCommand.ExecuteAsync("minify");
        Assert.Equal(tool.Input, tool.Output);
        tool.Input = LargeJson();
        processing = tool.TransformCommand.ExecuteAsync("format");
        tool.SelectedTool = "URL 编码 / 解码";
        await processing;
        Assert.Empty(tool.Output); Assert.Equal("准备就绪", tool.Status);
    }

    [AvaloniaFact]
    public void LargeSingleLineEditorKeepsAllContentAndRendersTheViewport()
    {
        var text = "{\"text\":\"" + new string('x', 2 * 1024 * 1024) + "\"}";
        var editor = new CodeEditor { Text = text, Language = "JSON", IsReadOnly = true };
        var window = new Window { Content = editor, Width = 600, Height = 300 }; window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); editor.Editor.TextArea.TextView.EnsureVisualLines();
            Assert.Equal(text.Length, editor.Editor.Document.TextLength);
            Assert.Equal(text, editor.Text);
            Assert.Equal(1, editor.Editor.Document.LineCount);
            Assert.NotEmpty(editor.Editor.TextArea.TextView.VisualLines);
        }
        finally { window.Close(); }
    }
}
