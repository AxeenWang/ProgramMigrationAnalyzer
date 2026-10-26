using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Analysis;

/// <summary>Small, source-backed relations that must survive report summarization.</summary>
internal sealed class FourGlFlowInsights(FourGlEvidence evidence, IReadOnlyList<FourGlSqlEvidence> sql)
{
    private const string Literal = """(?:"(?:\\.|""|[^"\\])*"|'(?:\\.|''|[^'\\])*')""";
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<FourGlStatement> Nodes(IEnumerable<FourGlStatement> nodes) => FourGlReportNarrator.Nodes(nodes);
    private static string Flat(string text) => FourGlReportNarrator.Flat(text);
    private static string WithoutStrings(string text) => Regex.Replace(text, Literal, " ");
    private static bool Refers(string text, string name) => Regex.IsMatch(WithoutStrings(text), @"(?<![\w.])" + Regex.Escape(name) + @"(?![\w.])", RegexOptions.IgnoreCase);
    private static bool IsLiteral(string? value) => value is not null && Regex.IsMatch(value.Trim(), "^" + Literal + "$");

    public IEnumerable<string[]> DataFlows(FourGlRoutine routine)
    {
        var nodes = Nodes(routine.Statements).ToList();
        var timeNames = nodes.Where(n => n.Kind == "LET" && n.Expression is not null && Regex.IsMatch(n.Expression, @"\bINTERVAL\s*\(|\[12,\s*13\]|\[1,\s*4\]", RegexOptions.IgnoreCase))
            .Select(n => n.Target ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Preserve the short alias chain linking the adjusted clock to query/call parameters.
        for (var pass = 0; pass < 16; pass++)
        {
            var changed = false;
            foreach (var n in nodes.Where(n => n.Kind == "LET" && n.Target is not null && Regex.IsMatch(n.Expression ?? "", @"^\w+$")))
                if (timeNames.Contains(n.Target!) || timeNames.Contains(n.Expression!)) { changed |= timeNames.Add(n.Target!); changed |= timeNames.Add(n.Expression!); }
            if (!changed) break;
        }
        foreach (var n in nodes.Where(n => n.Kind == "LET" && n.Expression is not null))
        {
            var expression = Flat(n.Expression!);
            if (Regex.IsMatch(expression, @"\bINTERVAL\s*\(", RegexOptions.IgnoreCase))
            {
                var adjustment = Regex.Match(expression, @"^\w+\s*([+-])\s*INTERVAL\s*\((\d+)\)\s*(MINUTE|HOUR|DAY|SECOND)\s+TO\s*\3$", RegexOptions.IgnoreCase);
                var unit = adjustment.Success ? adjustment.Groups[3].Value.ToUpperInvariant() switch { "MINUTE" => "分鐘", "HOUR" => "小時", "DAY" => "天", _ => "秒" } : "";
                yield return [n.Target + " ← " + expression, (adjustment.Success ? "時間基準" + (adjustment.Groups[1].Value == "-" ? "往前 " : "往後 ") + adjustment.Groups[2].Value + " " + unit + "。" : "先調整時間基準。") + "後續由此值衍生的日期／時間須使用調整後結果。", n.HeaderSpan.Lines];
            }
            else if (Regex.IsMatch(expression, @"^(\w+)\[1,\s*4\]\s*,\s*\1\[6,\s*7\]\s*,\s*\1\[9,\s*10\]$"))
                yield return [n.Target + " ← " + expression, "以年、月、日字串片段組成 YYYYMMDD。", n.HeaderSpan.Lines];
            else if (Regex.IsMatch(expression, @"^(\w+)\[12,\s*13\]\s*,\s*\1\[15,\s*16\]\s*,\s*\1\[18,\s*19\]$"))
                yield return [n.Target + " ← " + expression, "以時、分、秒字串片段組成 HHMMSS 格式，後續數值減法另見規則。", n.HeaderSpan.Lines];
            else if (timeNames.Contains(n.Target ?? "") && Regex.IsMatch(expression, @"^\w+$"))
                yield return [n.Target + " ← " + expression, Same(expression, "CURRENT") ? "取得目前時間，供後續調整與時間條件使用。" : "傳遞時間基準／衍生日期時間，與前後切片及查詢參數相連。", n.HeaderSpan.Lines];
            else if (Regex.IsMatch(expression, @"^\w+\.\w+\[1,\s*\d+\]$") && nodes.Any(x => x.Span.StartOffset > n.Span.StartOffset && x.Kind is "IF" or "CASE" && Refers(x.Expression ?? "", n.Target ?? "")))
                yield return [n.Target + " ← " + expression, "取來源欄位前綴，作為後續條件／類別判斷輸入。", n.HeaderSpan.Lines];
        }
        foreach (var pattern in ResetPatterns(routine))
            yield return [pattern.Reset.Target + " ← " + pattern.Reset.Expression, "每筆迴圈先重設命令，避免沿用前筆值。後續僅在 " + Flat(pattern.Guard.Expression ?? "") + " 時呼叫 " + string.Join("、", Nodes(pattern.Guard.Branches.SelectMany(b => b.Statements)).Where(n => n.Kind == "CALL").Select(n => n.Target)) + "，取用當筆最後留下的命令。", pattern.Reset.HeaderSpan.Lines + "、" + pattern.Guard.HeaderSpan.Lines];
    }

    private IEnumerable<(FourGlStatement Reset, FourGlStatement Choice, FourGlStatement Guard)> ResetPatterns(FourGlRoutine routine)
    {
        foreach (var loop in Nodes(routine.Statements).Where(n => n.Kind is "FOREACH" or "WHILE" or "FOR"))
        {
            var list = loop.Children;
            for (var i = 0; i < list.Count; i++)
            {
                var reset = list[i];
                if (reset.Kind != "LET" || !IsLiteral(reset.Expression) || string.IsNullOrEmpty(reset.Target)) continue;
                if (!Nodes(loop.Children).Any(n => n.Kind == "RUN" && Same(n.Expression, reset.Target))) continue;
                var choice = list.Skip(i + 1).FirstOrDefault(n => n.Kind == "CASE");
                if (choice is null || list.Where(n => n.Span.StartOffset > reset.Span.StartOffset && n.Span.StartOffset < choice.Span.StartOffset).Any(n => MayWrite(n, reset.Target))) continue;
                var guard = list.FirstOrDefault(n => n.Span.StartOffset > choice.Span.EndOffset && n.Kind == "IF" &&
                    Regex.IsMatch(Flat(n.Expression ?? "").Trim('(', ')', ' '), "^" + Regex.Escape(reset.Target) + @"\s*(?:<>|!=)\s*" + Regex.Escape(reset.Expression!.Trim()) + "$", RegexOptions.IgnoreCase) &&
                    n.Branches.Count == 1 && Nodes(n.Branches[0].Statements).Any(c => c.Kind == "CALL" && c.Arguments.Any(a => Same(a, reset.Target))));
                if (guard is null || list.Where(n => n.Span.StartOffset > choice.Span.EndOffset && n.Span.StartOffset < guard.Span.StartOffset).Any(n => MayWrite(n, reset.Target))) continue;
                yield return (reset, choice, guard);
            }
        }
    }

    private static bool MayWrite(FourGlStatement node, string name) => Nodes([node]).Any(n => OwnWrite(n, name));

    // Keep only the branch containing the statement, but conservatively include other intervening nodes.
    private static IEnumerable<FourGlStatement> ChosenPath(IEnumerable<FourGlStatement> nodes, FourGlStatement target)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if (n.Span.StartOffset <= target.Span.StartOffset && n.Span.EndOffset >= target.Span.EndOffset && n.Branches.Count > 0)
            {
                foreach (var child in Nodes(n.Children)) yield return child;
                foreach (var b in n.Branches.Where(b => Nodes(b.Statements).Contains(target)))
                    foreach (var child in ChosenPath(b.Statements, target)) yield return child;
            }
            else foreach (var child in Nodes(n.Children.Concat(n.Branches.SelectMany(b => b.Statements)))) yield return child;
        }
    }

    public IEnumerable<(string Text, SourceSpan Span)> Findings()
    {
        foreach (var r in evidence.Routines)
        foreach (var p in ResetPatterns(r))
        foreach (var update in Nodes(p.Choice.Branches.SelectMany(b => b.Statements)).Where(n => n.Kind == "UPDATE"))
        {
            var path = ChosenPath([p.Choice], update).ToList();
            // Inspect the selected path's own effects rather than recursively revisiting excluded branches.
            if (path.Any(n => OwnWrite(n, p.Reset.Target!))) continue;
            var statement = sql.FirstOrDefault(s => s.Span.StartOffset == update.HeaderSpan.StartOffset);
            if (statement is null) continue;
            var call = Nodes(p.Guard.Branches[0].Statements).First(n => n.Kind == "CALL" && n.Arguments.Any(a => Same(a, p.Reset.Target)));
            yield return ($"{statement.Id} 路徑（{FourGlReportNarrator.Condition(statement.Path)}）未設定新命令，{p.Reset.Target} 維持 {p.Reset.Expression}，因此不呼叫 {call.Target}。此結論只限從當筆重設到該守衛之間的這條路徑。", update.HeaderSpan);
        }
        foreach (var f in TimeFilterFindings()) yield return f;
        foreach (var f in StateFindings()) yield return f;
        foreach (var r in evidence.Routines)
        foreach (var choice in Nodes(r.Statements).Where(n => n.Kind == "CASE"))
        foreach (var b in choice.Branches)
        {
            var nodes = Nodes(b.Statements).ToList();
            var commands = evidence.Commands.Where(c => Same(c.Routine, r.Name) && nodes.Any(n => n.Kind == "RUN" && n.HeaderSpan.StartOffset == c.Span.StartOffset)).ToList();
            if (commands.Count == 0 || commands.Any(c => !NotificationOnly(c.Template)) || nodes.Any(n => n.Kind is "CALL" or "EXECUTE" or "UNKNOWN")) continue;
            var gates = commands.SelectMany(c => Comparisons(c.Path)).Where(c => c.Name.Contains('.') && !Comparisons(b.Condition).Contains(c)).Distinct().ToList();
            yield return ($"分支 {Flat(b.Condition)} 的 RUN 只有郵件／提醒命令" + (gates.Count > 0 ? "，條件包含 " + string.Join("、", gates.Select(g => g.Name + " = " + g.Value)) : "") + "，未直接執行郵件文字中提到的工作。提醒送達及人工是否執行無法由本檔證明。", b.Span);
        }
        foreach (var f in CommentFindings()) yield return f;
    }

    private static bool OwnWrite(FourGlStatement n, string name) => n.Kind == "LET" && (Same(n.Target, name) || Regex.IsMatch(WithoutStrings(n.Expression ?? ""), @"\b\w+\s*\(")) ||
        n.Kind is "CALL" or "RUN" or "EXECUTE" or "UNKNOWN" or "INITIALIZE" or "INPUT" or "FETCH" || n.Returning.Any(x => Same(x, name)) || n.Kind == "SELECT" && Refers(n.CodeText, name);

    private IEnumerable<(string Text, SourceSpan Span)> TimeFilterFindings()
    {
        var temporal = evidence.Declarations.Where(d => Regex.IsMatch(d.Type, @"^(?:DATE\b|DATETIME\b|INTERVAL\b)", RegexOptions.IgnoreCase)).Select(d => (d.Scope, d.Name)).ToHashSet();
        bool Has(string scope, string expression) => temporal.Any(t => (Same(t.Scope, scope) || t.Scope == "全域") && Refers(expression, t.Name));
        for (var pass = 0; pass < 16; pass++)
        {
            var changed = false;
            foreach (var r in evidence.Routines)
            foreach (var n in Nodes(r.Statements).Where(n => n.Kind == "LET" && n.Target is not null && n.Expression is not null))
                if (Regex.IsMatch(WithoutStrings(n.Expression!), @"\bCURRENT\b|\[12,\s*13\]|\[1,\s*4\]", RegexOptions.IgnoreCase) || Has(r.Name, n.Expression!)) changed |= temporal.Add((r.Name, n.Target!));
            foreach (var call in evidence.Calls.Where(c => c.IsLocal))
            {
                var callee = evidence.Routines.FirstOrDefault(r => Same(r.Name, call.Target));
                if (callee is null) continue;
                for (var i = 0; i < Math.Min(call.Arguments.Count, callee.Parameters.Count); i++)
                    if (Has(call.Routine, call.Arguments[i])) changed |= temporal.Add((callee.Name, callee.Parameters[i]));
            }
            if (!changed) break;
        }
        foreach (var cursor in sql.Where(s => s.Context.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase) && !Has(s.Routine, s.Predicate)))
        {
            if (!evidence.Commands.Any(c => Same(c.Routine, cursor.Routine))) continue;
            var timed = sql.Where(s => Same(s.Routine, cursor.Routine) && s.Command == "SELECT" && Has(s.Routine, s.Predicate) && s.Tables.Intersect(cursor.Tables, StringComparer.OrdinalIgnoreCase).Any()).ToList();
            if (timed.Count > 0) yield return ($"{cursor.Id} 未使用這組時間篩選。相同資料表的時間條件見 {string.Join("、", timed.Select(s => s.Id))}，不能將那些計數／清單的時間門檻套用到這個派送游標。", cursor.Span);
        }
    }

    private IEnumerable<(string Text, SourceSpan Span)> StateFindings()
    {
        foreach (var group in sql.Where(s => s.Command == "UPDATE").SelectMany(s => s.Columns.Where(c => c.Use.StartsWith("更新目標 ← ", StringComparison.Ordinal)).Select(c => (Sql: s, Column: c)))
            .GroupBy(x => (Table: x.Column.Table.ToLowerInvariant(), Name: x.Column.Name.ToLowerInvariant())))
        {
            var writes = group.Select(x => x.Column.Use["更新目標 ← ".Length..].Trim()).Distinct().ToList();
            if (writes.Any(x => !IsLiteral(x))) continue;
            var compared = sql.Where(s => s.Tables.Contains(group.Key.Table, StringComparer.OrdinalIgnoreCase)).SelectMany(s => Comparisons(s.Predicate))
                .Where(m => Same(m.Name.Split('.').Last(), group.Key.Name)).Select(m => m.Value).Distinct().ToList();
            var absent = compared.Where(v => !writes.Any(w => Unquote(w) == Unquote(v))).ToList();
            if (absent.Count > 0) yield return ($"{group.First().Column.Table}.{group.First().Column.Name} 的有效查詢比較 {string.Join("、", absent)}，但本檔靜態 UPDATE 僅寫入 {string.Join("、", writes)}，未寫入上述查詢值。完成狀態由何處回寫須查外部程式或動態 SQL，不能由 RUN 推定已完成。", group.First().Sql.Span);
        }
    }

    private static IEnumerable<(string Name, string Value)> Comparisons(string text)
    {
        var tokens = Regex.Matches(text, Literal + @"|[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*|<>|!=|==|[=()<>]").Select(m => m.Value).ToList();
        for (var i = 0; i + 2 < tokens.Count; i++)
            if (Regex.IsMatch(tokens[i], @"^[A-Za-z_]\w*(?:\.\w+)*$") && tokens[i + 1] is "=" or "==" && IsLiteral(tokens[i + 2])) yield return (tokens[i], tokens[i + 2]);
    }
    private static string Unquote(string text) => text.Trim()[1..^1].Replace("''", "'").Replace("\"\"", "\"");

    private static bool NotificationOnly(string command)
    {
        // A quoted mail subject can contain program names or shell operators without executing them.
        var code = WithoutStrings(command).Trim();
        if (code.IndexOfAny([';', '&', '\n', '\r']) >= 0 || command.Contains('`') || command.Contains("$(", StringComparison.Ordinal)) return false;
        var executables = code.Split('|').Select(p => Regex.Match(p.Trim(), @"^(?:[\w/.-]+/)?([\w.-]+)\b").Groups[1].Value).ToList();
        return executables.Count > 0 && executables.All(x => x is "echo" or "printf" or "mailx" or "mutt") && executables.Any(x => x is "mailx" or "mutt");
    }

    private IEnumerable<(string Text, SourceSpan Span)> CommentFindings()
    {
        var stateFields = sql.Where(s => s.Command == "UPDATE").SelectMany(s => s.Columns.Where(c => c.Use.StartsWith("更新目標 ← ", StringComparison.Ordinal))).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in evidence.Comments)
        {
            var claim = Regex.Match(comment.Content, @"\b([\w.-]+\.4ge)\b.*?\bflag\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (!claim.Success) continue;
            var fields = Regex.Matches(claim.Groups[2].Value, @"\b[A-Za-z_]\w*\b").Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var command in evidence.Commands.Where(c => Regex.IsMatch(c.Template.TrimStart(), @"^(?:[\w/.-]+/)?" + Regex.Escape(claim.Groups[1].Value) + @"(?:\s|$)", RegexOptions.IgnoreCase)))
            {
                var gates = Comparisons(command.Path).Where(c => stateFields.Contains(c.Name.Split('.').Last()) && evidence.RecordFields.Any(f => Same(f.Record + "." + f.Name, c.Name))).Select(c => c.Name.Split('.').Last()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (gates.Count > 0 && !gates.Intersect(fields, StringComparer.OrdinalIgnoreCase).Any())
                    yield return ($"註解與有效碼不一致：{claim.Groups[1].Value} 註解宣稱 flag({string.Join(", ", fields)})，但 {command.Span.Lines} 的 RUN 路徑實際以 {string.Join("、", gates)} 判斷。移植應以有效碼為準，旗標業務對應仍待確認。", comment.Span);
            }
        }
    }
}
