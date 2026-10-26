using System.Security.Cryptography;
using System.Text;

namespace ProgramMigrationAnalyzer.Core;

/// <summary>UTF-16 offsets and one-based positions. EndOffset/EndColumn are exclusive.</summary>
public sealed record SourceSpan(int StartOffset, int Length, int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public int EndOffset => StartOffset + Length;
    public string Lines => StartLine == EndLine ? $"L{StartLine}" : $"L{StartLine}–L{EndLine}";
    public string Slice(string source) => source.Substring(StartOffset, Length);
}

public sealed class SourceSnapshot
{
    private readonly int[] _lineStarts;
    public SourceSnapshot(SourceDocument source)
    {
        Text = source.Content;
        EncodingName = source.EncodingName;
        ByteSha256 = source.ByteSha256;
        ByteLength = source.ByteLength;
        TextSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text))).ToLowerInvariant();
        var starts = new List<int> { 0 };
        for (var index = 0; index < Text.Length; index++)
        {
            if (Text[index] == '\r')
            {
                if (index + 1 < Text.Length && Text[index + 1] == '\n') index++;
                starts.Add(index + 1);
            }
            else if (Text[index] == '\n') starts.Add(index + 1);
        }
        _lineStarts = starts.ToArray();
    }
    public string Text { get; }
    public string EncodingName { get; }
    public string? ByteSha256 { get; }
    public long? ByteLength { get; }
    public string TextSha256 { get; }
    public int LineCount => _lineStarts.Length - (Text.Length > 0 && _lineStarts[^1] == Text.Length ? 1 : 0);
    public SourceSpan Span(int start, int end)
    {
        start = Math.Clamp(start, 0, Text.Length);
        end = Math.Clamp(end, start, Text.Length);
        var first = Array.BinarySearch(_lineStarts, start);
        if (first < 0) first = ~first - 1;
        var last = Array.BinarySearch(_lineStarts, end);
        if (last < 0) last = ~last - 1;
        return new(start, end - start, first + 1, start - _lineStarts[first] + 1, last + 1, end - _lineStarts[last] + 1);
    }
}

public sealed record FourGlDiagnostic(string Code, string Message, SourceSpan Span);
public sealed record FourGlComment(string Text, string Content, SourceSpan Span);
public sealed record FourGlDeclaration(string Name, string Type, string Scope, string RawText, SourceSpan Span);

public sealed class FourGlStatement
{
    public required string Kind { get; init; }
    public required string Text { get; init; }
    public string CodeText { get; init; } = string.Empty;
    public required SourceSpan Span { get; set; }
    public required SourceSpan HeaderSpan { get; init; }
    public string? Target { get; set; }
    public string? Expression { get; set; }
    public List<string> Arguments { get; } = [];
    public List<string> Returning { get; } = [];
    public List<FourGlStatement> Children { get; } = [];
    public List<FourGlBranch> Branches { get; } = [];
}

public sealed class FourGlBranch
{
    public required string Condition { get; init; }
    public required SourceSpan Span { get; init; }
    public List<FourGlStatement> Statements { get; } = [];
}

public sealed class FourGlRoutine
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required SourceSpan Span { get; set; }
    public List<string> Parameters { get; } = [];
    public List<FourGlStatement> Statements { get; } = [];
}

public sealed class FourGlSqlEvidence
{
    public required string Id { get; set; }
    public required string Command { get; init; }
    public required string RawSql { get; init; }
    public required SourceSpan Span { get; init; }
    public required string Routine { get; init; }
    public bool IsCommented { get; init; }
    public string Context { get; set; } = "直接 SQL";
    public string Path { get; set; } = "無額外條件";
    public List<string> Tables { get; } = [];
    public List<string> Into { get; } = [];
    public List<FourGlColumn> Columns { get; } = [];
    public bool HasWildcard { get; set; }
    public string Predicate { get; set; } = string.Empty;
    public string Effect { get; set; } = string.Empty;
}

public sealed record FourGlColumn(string Table, string Name, string Use, string Expression, SourceSpan Span);
public sealed record FourGlFlowEvidence(string Routine, string Kind, string Description, string Path, SourceSpan Span);
public sealed record FourGlValueEvidence(string Routine, string Name, string Expression, string Value, string Path, SourceSpan Span);
public sealed record FourGlCallEvidence(string Routine, string Target, IReadOnlyList<string> Arguments, IReadOnlyList<string> Returning, string Path, SourceSpan Span, bool IsLocal);
public sealed record FourGlCommandEvidence(string Routine, string Expression, string Template, string Path, SourceSpan Span, string Status);
public sealed record FourGlSymbolUse(string Routine, string Name, string Access, SourceSpan Span);
public sealed record FourGlRecordField(string Routine, string Record, string Table, string Name, string Expression, SourceSpan Span);

public sealed class FourGlEvidence
{
    public required SourceSnapshot Snapshot { get; init; }
    public List<FourGlRoutine> Routines { get; } = [];
    public List<FourGlStatement> TopLevel { get; } = [];
    public List<FourGlComment> Comments { get; } = [];
    public List<FourGlDeclaration> Declarations { get; } = [];
    public List<FourGlSqlEvidence> Sql { get; } = [];
    public List<FourGlDiagnostic> Diagnostics { get; } = [];
    public List<FourGlFlowEvidence> Flow { get; } = [];
    public List<FourGlValueEvidence> Values { get; } = [];
    public List<FourGlCallEvidence> Calls { get; } = [];
    public List<FourGlCommandEvidence> Commands { get; } = [];
    public List<FourGlSymbolUse> SymbolUses { get; } = [];
    public List<FourGlRecordField> RecordFields { get; } = [];
    public int TokenCount { get; set; }
    public int RecognizedStatements { get; set; }
    public int UnknownStatements { get; set; }
    public bool IsAnalyzed { get; set; }
}
