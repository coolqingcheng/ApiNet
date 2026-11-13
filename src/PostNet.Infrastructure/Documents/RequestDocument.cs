namespace PostNet.Infrastructure.Documents;

public enum DocumentBlockKind { Heading, Paragraph, Code, Table }
public sealed record DocumentBlock(DocumentBlockKind Kind, string Text = "", string Language = "", IReadOnlyList<string[]>? Rows = null);
public sealed record RequestDocument(string Title, int RequestCount, IReadOnlyList<DocumentBlock> Blocks);
