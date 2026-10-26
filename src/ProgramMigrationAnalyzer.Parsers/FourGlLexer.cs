using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Parsers;

internal enum FourGlTokenKind { Word, Number, String, Symbol }
internal sealed record FourGlToken(string Text, FourGlTokenKind Kind, int Start, int End, int Line)
{
    public bool Is(string value) => Text.Equals(value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Never normalizes source. Comments occupy their original offsets in Code.</summary>
internal sealed class FourGlLexed
{
    public required string Code { get; init; }
    public List<FourGlToken> Tokens { get; } = [];
}

internal static class FourGlLexer
{
    public static FourGlLexed Lex(FourGlEvidence evidence, CancellationToken cancellationToken)
    {
        var text = evidence.Snapshot.Text;
        var code = text.ToCharArray();
        var result = new FourGlLexed { Code = text };
        var line = 1;
        var i = 0;
        while (i < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (c == '\r' || (c == '\n' && (i == 0 || text[i - 1] != '\r'))) line++;
                i++;
                continue;
            }
            var start = i;
            var startLine = line;
            if (c == '#' || (c == '-' && i + 1 < text.Length && text[i + 1] == '-') || c == '{')
            {
                var block = c == '{';
                i += block || c == '#' ? 1 : 2;
                var contentStart = i;
                if (block)
                {
                    while (i < text.Length && text[i] != '}')
                    {
                        if (text[i] == '\r' || (text[i] == '\n' && text[i - 1] != '\r')) line++;
                        i++;
                    }
                }
                else while (i < text.Length && text[i] is not '\r' and not '\n') i++;
                var contentEnd = i;
                if (block && i < text.Length) i++;
                else if (block) evidence.Diagnostics.Add(new("4GL-LEX-COMMENT", "區塊註解未結束，剩餘文字列為註解證據。", evidence.Snapshot.Span(start, i)));
                evidence.Comments.Add(new(text[start..i], text[contentStart..contentEnd], evidence.Snapshot.Span(start, i)));
                for (var offset = start; offset < i; offset++) if (code[offset] is not '\r' and not '\n') code[offset] = ' ';
                continue;
            }
            var kind = FourGlTokenKind.Symbol;
            if (c is '\'' or '"')
            {
                kind = FourGlTokenKind.String;
                var quote = c;
                var closed = false;
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\r' || (text[i] == '\n' && text[i - 1] != '\r')) line++;
                    if (text[i] == '\\' && i + 1 < text.Length) { i += 2; continue; }
                    if (text[i] == quote)
                    {
                        if (i + 1 < text.Length && text[i + 1] == quote) { i += 2; continue; }
                        i++;
                        closed = true;
                        break;
                    }
                    i++;
                }
                if (!closed) evidence.Diagnostics.Add(new("4GL-LEX-STRING", "字串未結束，無法可靠辨識其後語法。", evidence.Snapshot.Span(start, i)));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                kind = FourGlTokenKind.Word;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$')) i++;
            }
            else if (char.IsDigit(c))
            {
                kind = FourGlTokenKind.Number;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
            }
            else
            {
                i++;
                if (i < text.Length && ((c is '<' or '>' or '!' or '=' && text[i] == '=') || c == '<' && text[i] == '>')) i++;
            }
            result.Tokens.Add(new(text[start..i], kind, start, i, startLine));
        }
        evidence.TokenCount = result.Tokens.Count;
        var final = new FourGlLexed { Code = new string(code) };
        final.Tokens.AddRange(result.Tokens);
        return final;
    }

    public static string Unquote(string text) => text.Length >= 2 && text[0] is '\'' or '"'
        ? text[1..^1].Replace(new string(text[0], 2), text[0].ToString()).Replace("\\\"", "\"").Replace("\\'", "'")
        : text;
}
