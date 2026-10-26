using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Parsers;

internal sealed class FourGlSyntaxParser(FourGlEvidence evidence, FourGlLexed lexed, CancellationToken cancellationToken)
{
    private readonly List<FourGlToken> _tokens = lexed.Tokens;
    private int _index;
    private string _scope = "全域";
    private static readonly HashSet<string> Heads = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATABASE", "GLOBALS", "MAIN", "FUNCTION", "REPORT", "DEFINE", "IF", "ELSE", "CASE", "WHEN", "OTHERWISE", "END",
        "FOR", "WHILE", "FOREACH", "LET", "CALL", "RETURN", "RUN", "UNLOAD", "DECLARE", "SELECT", "INSERT", "UPDATE", "DELETE",
        "SET", "WHENEVER", "DISPLAY", "INPUT", "CONTINUE", "EXIT", "BEGIN", "COMMIT", "ROLLBACK", "OPEN", "CLOSE", "FETCH",
        "PREPARE", "EXECUTE", "FREE", "FINISH", "OUTPUT", "START", "PRINT", "NEED", "SKIP", "PAUSE", "PROMPT", "SLEEP",
        "INITIALIZE", "LOCATE", "ERROR", "MESSAGE", "MENU", "COMMAND", "ON", "CONSTRUCT", "OPTIONS", "STOP"
    };
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
    { "CHAR", "VARCHAR", "NCHAR", "NVARCHAR", "INTEGER", "INT", "SMALLINT", "BIGINT", "DECIMAL", "MONEY", "FLOAT", "SMALLFLOAT", "REAL", "DATE", "DATETIME", "INTERVAL", "RECORD", "LIKE", "ARRAY", "BYTE", "TEXT", "BOOLEAN", "SERIAL" };

    public void Parse()
    {
        while (_index < _tokens.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Is("MAIN") || Is("FUNCTION") || Is("REPORT")) ParseRoutine();
            else if (Is("GLOBALS") && !Peek(1, FourGlTokenKind.String))
            {
                _index++;
                var global = ParseSequence(() => End("GLOBALS"));
                evidence.TopLevel.AddRange(global);
                ConsumeEnd("GLOBALS");
            }
            else evidence.TopLevel.Add(ParseStatement());
        }
    }

    private void ParseRoutine()
    {
        var start = _tokens[_index++];
        var kind = start.Text.ToUpperInvariant();
        var name = kind == "MAIN" ? "MAIN" : _index < _tokens.Count ? _tokens[_index++].Text : "未知單元";
        var routine = new FourGlRoutine { Name = name, Kind = kind, Span = evidence.Snapshot.Span(start.Start, start.End) };
        if (Is("("))
        {
            _index++;
            while (_index < _tokens.Count && !Is(")"))
            {
                if (_tokens[_index].Kind == FourGlTokenKind.Word) routine.Parameters.Add(_tokens[_index].Text);
                _index++;
            }
            if (Is(")")) _index++;
        }
        _scope = name;
        routine.Statements.AddRange(ParseSequence(() => End(kind) || Is("MAIN") || Is("FUNCTION") || Is("REPORT")));
        var end = ConsumeEnd(kind);
        routine.Span = evidence.Snapshot.Span(start.Start, end);
        evidence.Routines.Add(routine);
        _scope = "全域";
    }

    private List<FourGlStatement> ParseSequence(Func<bool> stop, int depth = 0)
    {
        var statements = new List<FourGlStatement>();
        while (_index < _tokens.Count && !stop())
        {
            cancellationToken.ThrowIfCancellationRequested();
            statements.Add(ParseStatement(depth));
        }
        return statements;
    }

    private FourGlStatement ParseStatement(int depth = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 128)
        {
            var bad = ReadSimple("UNKNOWN");
            evidence.Diagnostics.Add(new("4GL-DEPTH", "巢狀深度超過128，保留原文並停止向下展開此語句。", bad.Span));
            return bad;
        }
        if (Is("IF")) return ReadIf(depth + 1);
        if (Is("CASE")) return ReadCase(depth + 1);
        if (Is("FOR") || Is("WHILE") || Is("FOREACH"))
        {
            var kind = _tokens[_index].Text.ToUpperInvariant();
            var node = ReadSimple(kind);
            node.Children.AddRange(ParseSequence(() => End(kind), depth + 1));
            node.Span = evidence.Snapshot.Span(node.Span.StartOffset, ConsumeEnd(kind));
            return node;
        }
        var command = _tokens[_index].Text.ToUpperInvariant();
        var result = ReadSimple(Heads.Contains(command) ? command : "UNKNOWN");
        if (command == "DEFINE") ReadDeclarations(result);
        if (command == "LET")
        {
            var equals = result.CodeText.IndexOf('=');
            if (equals > 0)
            {
                result.Target = result.CodeText[3..equals].Trim();
                result.Expression = result.CodeText[(equals + 1)..].Trim();
            }
        }
        if (command == "CALL") ReadCall(result);
        if (command is "RETURN" or "RUN") result.Expression = result.CodeText[command.Length..].Trim();
        if (command == "RUN") ReadRun(result);
        return result;
    }

    private FourGlStatement ReadIf(int depth)
    {
        var start = _index++;
        while (_index < _tokens.Count && !Is("THEN") && !AtNewHead(_index, start)) _index++;
        var conditionEnd = _index;
        if (Is("THEN")) _index++;
        else evidence.Diagnostics.Add(new("4GL-IF", "IF 未辨識到 THEN，以行界線保留條件。", Span(start, _index)));
        var node = Node("IF", start, _index);
        node.Expression = Text(start + 1, conditionEnd).Trim();
        var branch = new FourGlBranch { Condition = node.Expression, Span = node.HeaderSpan };
        branch.Statements.AddRange(ParseSequence(() => Is("ELSE") || End("IF"), depth));
        node.Branches.Add(branch);
        if (Is("ELSE"))
        {
            var token = _tokens[_index++];
            var other = new FourGlBranch { Condition = $"NOT ({node.Expression})", Span = evidence.Snapshot.Span(token.Start, token.End) };
            other.Statements.AddRange(ParseSequence(() => End("IF"), depth));
            node.Branches.Add(other);
        }
        node.Span = evidence.Snapshot.Span(node.Span.StartOffset, ConsumeEnd("IF"));
        return node;
    }

    private FourGlStatement ReadCase(int depth)
    {
        var node = ReadSimple("CASE");
        var selector = node.CodeText[4..].Trim();
        node.Expression = selector;
        var previous = new List<string>();
        while (_index < _tokens.Count && !End("CASE"))
        {
            if (Is("WHEN") || Is("OTHERWISE"))
            {
                var head = ReadSimple(_tokens[_index].Text.ToUpperInvariant());
                var value = head.CodeText[head.Kind.Length..].Trim();
                var condition = head.Kind == "OTHERWISE" ? $"前述 CASE 分支皆未匹配 ({selector})" : string.IsNullOrEmpty(selector) ? value : $"{selector} = {value}";
                var branch = new FourGlBranch { Condition = condition, Span = head.Span };
                branch.Statements.AddRange(ParseSequence(() => Is("WHEN") || Is("OTHERWISE") || End("CASE"), depth));
                node.Branches.Add(branch);
                previous.Add(condition);
            }
            else
            {
                var unknown = ParseStatement(depth);
                node.Children.Add(unknown);
                evidence.Diagnostics.Add(new("4GL-CASE", "CASE 內分支前出現語句，保留原文，路徑需人工確認。", unknown.Span));
            }
        }
        node.Span = evidence.Snapshot.Span(node.Span.StartOffset, ConsumeEnd("CASE"));
        return node;
    }

    private FourGlStatement ReadSimple(string kind)
    {
        var start = _index++;
        var nesting = 0;
        var record = kind == "DEFINE" && _tokens.Skip(_index).TakeWhile(t => t.Line == _tokens[start].Line).Any(t => t.Is("RECORD"))
            && !_tokens.Skip(_index).TakeWhile(t => t.Line == _tokens[start].Line).Any(t => t.Is("LIKE"));
        var sql = kind is "SELECT" or "INSERT" or "UPDATE" or "DELETE";
        var sqlCaseDepth = 0;
        while (_index < _tokens.Count)
        {
            var t = _tokens[_index];
            if (sql && t.Is("CASE"))
            {
                var prior = _tokens[_index - 1];
                var expressionPosition = prior.Is("SELECT") || prior.Is("DISTINCT") || prior.Is(",") || prior.Is("=") || prior.Is("(") || prior.Is("BY") || prior.Is("THEN") || prior.Is("ELSE") || prior.Is("AND") || prior.Is("OR") || prior.Text is "+" or "-" or "*" or "/";
                if (sqlCaseDepth == 0 && AtNewHead(_index, start) && !expressionPosition) break;
                sqlCaseDepth++; _index++; continue;
            }
            if (sql && sqlCaseDepth > 0)
            {
                if (t.Is("END")) sqlCaseDepth--;
                _index++;
                continue;
            }
            if (record && End("RECORD")) { _index += 2; break; }
            if (!record && nesting == 0)
            {
                // The cursor/unload context ends before its SELECT, even on the same line.
                if (kind is "DECLARE" or "UNLOAD" && t.Is("SELECT")) break;
                if (t.Is(";") ) { _index++; break; }
                if (!sql && _index > start && t.Line > _tokens[_index - 1].Line && !Heads.Contains(t.Text))
                {
                    var prior = _tokens[_index - 1];
                    var continuation = prior.Is(",") || prior.Is("=") || prior.Is("+") || prior.Is("-") || prior.Is("*") || prior.Is("/")
                        || kind == "CALL" && t.Is("RETURNING") || kind == "DEFINE" && Types.Contains(t.Text)
                        || kind == "DECLARE" && (t.Is("CURSOR") || t.Is("FOR"));
                    if (!continuation) break;
                }
                if (AtNewHead(_index, start))
                {
                    if (sql && kind == "INSERT" && t.Is("SELECT") && !_tokens.Skip(start).Take(_index - start).Any(x => x.Is("VALUES") || x.Is("SELECT"))) { _index++; continue; }
                    if (sql && kind == "UPDATE" && t.Is("SET")) { _index++; continue; }
                    // SQL CASE/WHEN are expressions. 4GL blocks occur after the SQL's line.
                    if (!(kind == "SET" && t.Is("ISOLATION"))) break;
                }
                if (t.Line == _tokens[start].Line && Heads.Contains(t.Text) && InlineBoundary(kind, t, _index)) break;
            }
            if (t.Is("(" ) || t.Is("[")) nesting++;
            else if (t.Is(")") || t.Is("]")) nesting = Math.Max(0, nesting - 1);
            _index++;
        }
        return Node(kind, start, _index);
    }

    private bool InlineBoundary(string kind, FourGlToken token, int index)
    {
        if (kind is "WHEN" or "OTHERWISE") return token.Is("LET") || token.Is("CALL") || token.Is("RUN") || token.Is("RETURN") || token.Is("IF");
        if (kind is "LET" or "RUN" or "CALL" or "RETURN" or "DISPLAY")
            return token.Is("END") || token.Is("ELSE") || token.Is("LET") || token.Is("RUN") || token.Is("RETURN") || token.Is("CALL");
        return false;
    }

    private bool AtNewHead(int index, int start) => index > start && _tokens[index].Line > _tokens[index - 1].Line && Heads.Contains(_tokens[index].Text);

    private FourGlStatement Node(string kind, int start, int end)
    {
        var span = Span(start, end);
        var trailing = evidence.Comments.FirstOrDefault(c => c.Span.StartLine == span.EndLine && c.Span.StartOffset >= span.EndOffset);
        if (trailing is not null && !lexed.Tokens.Any(t => t.Start >= span.EndOffset && t.Start < trailing.Span.StartOffset))
            span = evidence.Snapshot.Span(span.StartOffset, trailing.Span.EndOffset);
        if (kind == "UNKNOWN")
        {
            evidence.UnknownStatements++;
            evidence.Diagnostics.Add(new("4GL-UNKNOWN", "未支援語法，保留原文，未推斷其副作用。", span));
        }
        else evidence.RecognizedStatements++;
        return new() { Kind = kind, Text = span.Slice(evidence.Snapshot.Text), CodeText = span.Slice(lexed.Code), Span = span, HeaderSpan = span };
    }

    private void ReadDeclarations(FourGlStatement node)
    {
        var tokens = _tokens.Where(t => t.Start >= node.Span.StartOffset && t.End <= node.Span.EndOffset).Skip(1).ToList();
        var segments = Split(tokens, ",");
        var pending = new List<string>();
        foreach (var segment in segments)
        {
            if (segment.Count == 0) continue;
            var type = segment.FindIndex(t => Types.Contains(t.Text));
            if (type < 0) { pending.Add(string.Concat(segment.Select(t => t.Text))); continue; }
            var names = pending.Append(string.Concat(segment.Take(type).Select(t => t.Text))).Where(n => n.Length > 0).ToList();
            pending.Clear();
            var typeText = lexed.Code[segment[type].Start..segment[^1].End].Trim();
            foreach (var name in names) evidence.Declarations.Add(new(name, typeText, _scope, node.Text, node.Span));
        }
        foreach (var name in pending)
        {
            evidence.Declarations.Add(new(name, "未知型別", _scope, node.Text, node.Span));
            evidence.Diagnostics.Add(new("4GL-DEFINE", $"宣告 {name} 未辨識到型別。", node.Span));
        }
    }

    private void ReadCall(FourGlStatement node)
    {
        var tokens = _tokens.Where(t => t.Start >= node.Span.StartOffset && t.End <= node.Span.EndOffset).ToList();
        if (tokens.Count < 2) return;
        node.Target = tokens[1].Text;
        var open = tokens.FindIndex(t => t.Is("("));
        var close = MatchingClose(tokens, open);
        if (open >= 0 && close > open) node.Arguments.AddRange(Split(tokens.GetRange(open + 1, close - open - 1), ",").Select(Text));
        var returning = tokens.FindIndex(t => t.Is("RETURNING"));
        if (returning >= 0) node.Returning.AddRange(Split(tokens.Skip(returning + 1).ToList(), ",").Select(Text));
    }

    private void ReadRun(FourGlStatement node)
    {
        var tokens = _tokens.Where(t => t.Start >= node.HeaderSpan.StartOffset && t.End <= node.HeaderSpan.EndOffset).ToList();
        var modifier = tokens.FindIndex(t => t.Kind == FourGlTokenKind.Word && (t.Is("RETURNING") || t.Is("WITHOUT")));
        if (modifier >= 0 && tokens.Count > 1)
            node.Expression = lexed.Code[tokens[1].Start..tokens[modifier].Start].Trim();
        var returning = tokens.FindIndex(t => t.Kind == FourGlTokenKind.Word && t.Is("RETURNING"));
        if (returning >= 0)
        {
            var end = tokens.FindIndex(returning + 1, t => t.Kind == FourGlTokenKind.Word && t.Is("WITHOUT"));
            if (end < 0) end = tokens.Count;
            node.Returning.AddRange(Split(tokens.Skip(returning + 1).Take(end - returning - 1).ToList(), ",").Select(Text));
        }
    }

    internal static int MatchingClose(List<FourGlToken> tokens, int open)
    {
        if (open < 0) return -1;
        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Is("(")) depth++;
            if (tokens[i].Is(")") && --depth == 0) return i;
            if (!tokens[i].Is(")")) { /* only closing parentheses change depth */ }
        }
        return -1;
    }

    internal static List<List<FourGlToken>> Split(List<FourGlToken> tokens, string delimiter)
    {
        var result = new List<List<FourGlToken>> { new() };
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.Is("(") || token.Is("[")) depth++;
            if (token.Is(")") || token.Is("]")) depth--;
            if (depth == 0 && token.Is(delimiter)) result.Add([]);
            else result[^1].Add(token);
        }
        return result;
    }

    private int ConsumeEnd(string kind)
    {
        if (End(kind)) { var end = _tokens[_index + 1].End; _index += 2; return end; }
        var last = _index > 0 ? _tokens[_index - 1].End : 0;
        evidence.Diagnostics.Add(new("4GL-END", $"缺少 END {kind}，已保留讀取範圍。", evidence.Snapshot.Span(last, last)));
        return last;
    }
    private bool End(string kind) => Is("END") && _index + 1 < _tokens.Count && _tokens[_index + 1].Is(kind);
    private bool Is(string value) => _index < _tokens.Count && _tokens[_index].Is(value);
    private bool Peek(int offset, FourGlTokenKind kind) => _index + offset < _tokens.Count && _tokens[_index + offset].Kind == kind;
    private SourceSpan Span(int start, int end) => evidence.Snapshot.Span(_tokens[start].Start, end > start ? _tokens[end - 1].End : _tokens[start].End);
    private string Text(int start, int end) => end > start ? lexed.Code[_tokens[start].Start.._tokens[end - 1].End] : string.Empty;
    private string Text(List<FourGlToken> tokens) => tokens.Count > 0 ? lexed.Code[tokens[0].Start..tokens[^1].End].Trim() : string.Empty;
}
