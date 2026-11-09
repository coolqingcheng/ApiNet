using System.Globalization;
using ApiNet.Infrastructure.Documents;
using Avalonia.Platform;
using SkiaSharp;

namespace ApiNet.Desktop.Services.Documents;

public static class PdfDocumentWriter
{
    public static byte[] Write(RequestDocument document)
    {
        using var font = AssetLoader.Open(new Uri("avares://ApiNet.UI/Assets/Fonts/ApiNetDocumentSC-Regular.otf"));
        using var typeface = SKTypeface.FromStream(font) ?? throw new InvalidOperationException("无法加载 PDF 中文字体。");
        using var output = new MemoryStream();
        using var pdf = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Title = document.Title, Author = "ApiNet", Creator = "ApiNet" });
        var layout = new Layout(pdf, typeface);
        layout.Heading(document.Title, 23);
        layout.Paragraph($"共 {document.RequestCount} 个请求。变量保留为占位符，认证值及本地文件路径不导出。", true);
        foreach (var block in document.Blocks)
        {
            switch (block.Kind)
            {
                case DocumentBlockKind.Heading: layout.Heading(block.Text, block.Language == "folder" ? 17 : 13); break;
                case DocumentBlockKind.Paragraph: layout.Paragraph(block.Text); break;
                case DocumentBlockKind.Code: layout.Code(block.Text); break;
                case DocumentBlockKind.Table: layout.Table(block.Rows!); break;
            }
        }
        layout.Finish();
        pdf.Close();
        return output.ToArray();
    }

    private sealed class Layout(SKDocument document, SKTypeface typeface)
    {
        private const float Width = 595.28f, Height = 841.89f, Margin = 40, Bottom = Height - 48, ContentWidth = Width - Margin * 2;
        private readonly SKColor _ink = new(35, 43, 55), _muted = new(104, 116, 132), _accent = new(205, 80, 27), _border = new(215, 221, 230), _surface = new(245, 247, 250);
        private SKCanvas? _canvas;
        private float _y;
        private int _page;
        private SKPaint Paint(float size, bool bold = false, SKColor? color = null) => new() { Typeface = typeface, TextSize = size, FakeBoldText = bold, IsAntialias = true, Color = color ?? _ink };
        private void NewPage()
        {
            Finish();
            _canvas = document.BeginPage(Width, Height);
            _canvas.Clear(SKColors.White);
            _page++;
            using var header = Paint(9, true, _muted);
            _canvas.DrawText("API.NET  /  接口文档", Margin, 32, header);
            using var line = new SKPaint { Color = _accent, StrokeWidth = 1.5f };
            _canvas.DrawLine(Margin, 43, Width - Margin, 43, line);
            _y = 61;
        }
        public void Finish()
        {
            if (_canvas is null) return;
            using var footer = Paint(9, color: _muted);
            var text = "第 " + _page + " 页";
            _canvas.DrawText("ApiNet", Margin, Height - 22, footer);
            _canvas.DrawText(text, Width - Margin - footer.MeasureText(text), Height - 22, footer);
            document.EndPage(); _canvas = null;
        }
        private void Ensure(float height)
        {
            if (_canvas is null || _y + height > Bottom) NewPage();
        }
        public void Heading(string text, float size)
        {
            using var paint = Paint(size, true, size >= 17 ? _accent : _ink);
            var lines = Wrap(text, paint, ContentWidth);
            Ensure(Math.Min(lines.Count * (size + 9) + 40, Bottom - 61));
            _y += 12;
            foreach (var line in lines) { Ensure(size + 9); Draw(line, Margin, _y, paint); _y += size + 9; }
            _y += 5;
        }
        public void Paragraph(string text, bool muted = false)
        {
            using var paint = Paint(10.5f, color: muted ? _muted : _ink);
            foreach (var line in Wrap(text, paint, ContentWidth)) { Ensure(19); Draw(line, Margin, _y, paint); _y += 19; }
            _y += 7;
        }
        public void Code(string text)
        {
            using var paint = Paint(9.3f);
            using var background = new SKPaint { Color = _surface };
            foreach (var line in Wrap(text, paint, ContentWidth - 20))
            {
                Ensure(19);
                _canvas!.DrawRect(Margin, _y, ContentWidth, 19, background);
                Draw(line, Margin + 10, _y + 1, paint);
                _y += 19;
            }
            _y += 10;
        }
        public void Table(IReadOnlyList<string[]> rows)
        {
            if (rows.Count == 0) return;
            float[] widths = [43, 125, 45, ContentWidth - 213];
            using var regular = Paint(9.1f);
            using var bold = Paint(9.1f, true);
            using var fill = new SKPaint { Color = _surface };
            using var stroke = new SKPaint { Color = _border, IsStroke = true, StrokeWidth = .5f };
            DrawRow(rows[0], true);
            foreach (var row in rows.Skip(1)) DrawRow(row, false);
            _y += 12;

            void DrawRow(string[] row, bool header)
            {
                var paint = header ? bold : regular;
                var lines = Enumerable.Range(0, 4).Select(column => Wrap(column < row.Length ? row[column] : "", paint, widths[column] - 12)).ToArray();
                var count = lines.Max(x => x.Count);
                var offset = 0;
                while (offset < count)
                {
                    if (_canvas is null || _y + 28 > Bottom) { NewPage(); if (!header) DrawRow(rows[0], true); }
                    var take = Math.Min(count - offset, Math.Max(1, (int)((Bottom - _y - 10) / 17)));
                    var height = take * 17 + 10;
                    var x = Margin;
                    for (var column = 0; column < 4; column++)
                    {
                        var box = new SKRect(x, _y, x + widths[column], _y + height);
                        if (header) _canvas!.DrawRect(box, fill);
                        _canvas!.DrawRect(box, stroke);
                        for (var line = 0; line < take; line++)
                            if (offset + line < lines[column].Count) Draw(lines[column][offset + line], x + 6, _y + 5 + line * 17, paint);
                        x += widths[column];
                    }
                    _y += height; offset += take;
                }
            }
        }
        private void Draw(string text, float x, float top, SKPaint paint) => _canvas!.DrawText(text, x, top - paint.FontMetrics.Ascent, paint);
        private static List<string> Wrap(string text, SKPaint paint, float width)
        {
            List<string> lines = [];
            foreach (var paragraph in text.Replace("\t", "    ").ReplaceLineEndings("\n").Split('\n'))
            {
                var elements = StringInfo.GetTextElementEnumerator(paragraph);
                var current = new System.Text.StringBuilder(); float length = 0;
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    if (element.Any(char.IsControl)) continue;
                    var size = paint.MeasureText(element);
                    if (length + size > width && current.Length > 0) { lines.Add(current.ToString()); current.Clear(); length = 0; }
                    current.Append(element); length += size;
                }
                lines.Add(current.ToString());
            }
            return lines;
        }
    }
}
