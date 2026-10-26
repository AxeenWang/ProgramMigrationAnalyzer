using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Parsers;

internal static class FourGlSqlReader
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "DISTINCT", "UNIQUE", "ALL", "INTO", "FROM", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS", "ON",
        "WHERE", "AND", "OR", "NOT", "IS", "NULL", "IN", "LIKE", "MATCHES", "BETWEEN", "EXISTS", "GROUP", "BY", "ORDER", "HAVING",
        "ASC", "DESC", "UNION", "FOR", "UPDATE", "DELETE", "INSERT", "SET", "VALUES", "AS", "COUNT", "SUM", "MAX", "MIN", "AVG",
        "TODAY", "CURRENT", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "TO", "CASE", "WHEN", "THEN", "ELSE", "END",
        "CLIPPED", "USING", "TRIM", "NVL", "DECODE", "SUBSTR", "SUBSTRING", "COALESCE", "ROUND", "LENGTH", "MDY", "DATE"
    };

    public static void Read(FourGlEvidence evidence, FourGlLexed lexed, CancellationToken token)
    {
        void Walk(IEnumerable<FourGlStatement> statements, string routine, string path)
        {
            FourGlStatement? previous = null;
            foreach (var node in statements)
            {
                token.ThrowIfCancellationRequested();
                if (node.Kind is "SELECT" or "INSERT" or "UPDATE" or "DELETE")
                {
                    var sql = new FourGlSqlEvidence
                    {
                        Id = "", Command = node.Kind, RawSql = node.HeaderSpan.Slice(evidence.Snapshot.Text), Span = node.HeaderSpan,
                        Routine = routine, Path = path,
                        Context = previous?.Kind is "DECLARE" or "UNLOAD" ? previous.Text.Trim() : "直接 SQL"
                    };
                    var tokens = lexed.Tokens.Where(t => t.Start >= sql.Span.StartOffset && t.End <= sql.Span.EndOffset).ToList();
                    Extract(sql, tokens, lexed.Code, evidence);
                    evidence.Sql.Add(sql);
                }
                Walk(node.Children, routine, Join(path, node.Kind is "FOR" or "WHILE" or "FOREACH" ? node.Text.Trim() : ""));
                foreach (var branch in node.Branches) Walk(branch.Statements, routine, Join(path, branch.Condition));
                previous = node;
            }
        }
        Walk(evidence.TopLevel, "全域", "無額外條件");
        foreach (var routine in evidence.Routines) Walk(routine.Statements, routine.Name, "無額外條件");
        ReadComments(evidence);
        var active = 0;
        var disabled = 0;
        foreach (var sql in evidence.Sql.OrderBy(s => s.Span.StartOffset))
            sql.Id = sql.IsCommented ? $"停用 SQL-{++disabled:00}" : $"SQL-{++active:00}";
    }

    private static string Join(string path, string condition) => string.IsNullOrWhiteSpace(condition) ? path : path == "無額外條件" ? condition : $"{path} AND ({condition})";

    private static void Extract(FourGlSqlEvidence sql, List<FourGlToken> tokens, string code, FourGlEvidence evidence)
    {
        var aliases = new Dictionary<int, Dictionary<string, string>>();
        var scopeTables = new Dictionary<int, List<string>>();
        var scopes = new int[tokens.Count];
        var depths = new int[tokens.Count];
        var parents = new Dictionary<int, int>();
        var scopeStack = new Stack<(int Depth, int Parent)>();
        var currentScope = 0;
        var nextScope = 0;
        var nesting = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Is("("))
            {
                nesting++;
                if (index + 1 < tokens.Count && tokens[index + 1].Is("SELECT"))
                {
                    scopeStack.Push((nesting, currentScope));
                    parents[++nextScope] = currentScope;
                    currentScope = nextScope;
                }
            }
            scopes[index] = currentScope;
            depths[index] = nesting;
            if (tokens[index].Is(")"))
            {
                if (scopeStack.Count > 0 && scopeStack.Peek().Depth == nesting) currentScope = scopeStack.Pop().Parent;
                nesting--;
            }
        }
        var tablePositions = new HashSet<int>();
        var targets = new HashSet<int>();
        var intoStart = -1;
        var fromContexts = new HashSet<(int Scope, int Depth)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Is("FROM")) fromContexts.Add((scopes[i], depths[i]));
            if (t.Is("WHERE") || t.Is("GROUP") || t.Is("ORDER") || t.Is("HAVING") || t.Is("UNION")) fromContexts.Remove((scopes[i], depths[i]));
            var isTable = t.Is("FROM") || t.Is("JOIN") || t.Is(",") && fromContexts.Contains((scopes[i], depths[i])) || (t.Is("UPDATE") && i == 0) || (t.Is("INTO") && sql.Command == "INSERT" && i == 1);
            if (isTable && i + 1 < tokens.Count && tokens[i + 1].Kind == FourGlTokenKind.Word)
            {
                var start = i + 1;
                var end = start;
                while (end + 2 < tokens.Count && tokens[end + 1].Text is "." or ":" or "@" && tokens[end + 2].Kind == FourGlTokenKind.Word) end += 2;
                var name = string.Concat(tokens.Skip(start).Take(end - start + 1).Select(x => x.Text));
                Add(sql.Tables, name);
                if (!scopeTables.TryGetValue(scopes[i], out var tables)) scopeTables[scopes[i]] = tables = [];
                Add(tables, name);
                if (!aliases.TryGetValue(scopes[i], out var names)) aliases[scopes[i]] = names = new(StringComparer.OrdinalIgnoreCase);
                for (var p = start; p <= end; p++) tablePositions.Add(p);
                names[name.Split(':').Last().Split('.').Last()] = name;
                var alias = end + 1;
                if (alias < tokens.Count && tokens[alias].Is("AS")) { tablePositions.Add(alias); alias++; }
                if (alias < tokens.Count && tokens[alias].Kind == FourGlTokenKind.Word && !Reserved.Contains(tokens[alias].Text))
                {
                    names[tokens[alias].Text] = name;
                    tablePositions.Add(alias);
                }
            }
            if (t.Is("INTO") && sql.Command == "SELECT")
            {
                intoStart = i + 1;
                var end = tokens.FindIndex(i + 1, x => x.Is("FROM"));
                if (end < 0) end = tokens.Count;
                foreach (var group in FourGlSyntaxParser.Split(tokens.Skip(i + 1).Take(end - i - 1).ToList(), ","))
                    if (group.Count > 0) sql.Into.Add(code[group[0].Start..group[^1].End].Trim());
                for (var p = i + 1; p < end; p++) targets.Add(p);
            }
        }
        var where = tokens.FindIndex(t => t.Is("WHERE"));
        if (where >= 0)
        {
            var end = tokens.FindIndex(where + 1, t => t.Is("GROUP") || t.Is("ORDER") || t.Is("HAVING") || t.Is("UNION"));
            if (end < 0) end = tokens.Count;
            if (end > where + 1) sql.Predicate = code[tokens[where + 1].Start..tokens[end - 1].End].Trim();
        }
        sql.HasWildcard = tokens.Where((t, i) => t.Is("*") && i > 0 && (tokens[i - 1].Is("SELECT") || tokens[i - 1].Is("DISTINCT") || tokens[i - 1].Is(".") || tokens[i - 1].Is(",") && scopes[i] == 0 && (intoStart < 0 || i < intoStart))).Any();
        var set = tokens.FindIndex(t => t.Is("SET"));
        var valuesStart = tokens.FindIndex(t => t.Is("VALUES"));
        var setTargets = new HashSet<int>();
        var assignmentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (set >= 0)
        {
            var end = where >= 0 ? where : tokens.Count;
            var isTarget = true;
            for (var index = set + 1; index < end; index++)
            {
                if (depths[index] == 0 && tokens[index].Is("=")) isTarget = false;
                else if (depths[index] == 0 && tokens[index].Is(",")) isTarget = true;
                else if (isTarget) setTargets.Add(index);
            }
            var assignments = FourGlSyntaxParser.Split(tokens.Skip(set + 1).Take(end - set - 1).ToList(), ",");
            foreach (var assignment in assignments)
            {
                var equals = assignment.FindIndex(t => t.Is("="));
                if (equals <= 0 || equals == assignment.Count - 1) continue;
                List<FourGlToken> Strip(List<FourGlToken> group) => group.Count > 1 && group[0].Is("(") && FourGlSyntaxParser.MatchingClose(group, 0) == group.Count - 1 ? group.Skip(1).Take(group.Count - 2).ToList() : group;
                var left = FourGlSyntaxParser.Split(Strip(assignment.Take(equals).ToList()), ",");
                var right = FourGlSyntaxParser.Split(Strip(assignment.Skip(equals + 1).ToList()), ",");
                for (var index = 0; index < left.Count && index < right.Count; index++)
                    if (left[index].Count > 0 && right[index].Count > 0)
                        assignmentValues[string.Concat(left[index].Select(t => t.Text)).Split('.').Last()] = code[right[index][0].Start..right[index][^1].End];
            }
        }
        sql.Effect = sql.Command switch
        {
            "SELECT" => $"讀取{(tokens.Any(t => t.Is("COUNT")) ? "符合條件的列數或分組計數" : "查詢投影值")}，{(sql.Into.Count > 0 ? $"寫入 {string.Join("、", sql.Into)}" : "交付游標、UNLOAD 或查詢結果")}",
            "UPDATE" => $"更新符合 WHERE 的列{(set >= 0 ? $"，指定值：{code[tokens[set].End..(where >= 0 ? tokens[where].Start : tokens[^1].End)].Trim()}" : "")}",
            "INSERT" => "新增 VALUES 或 SELECT 提供的資料列",
            "DELETE" => "刪除符合 WHERE 的資料列",
            _ => "需人工確認"
        };
        var selectEnd = tokens.FindIndex(t => t.Is("FROM"));
        var selectFields = tokens.Count > 1 && sql.Command == "SELECT"
            ? FourGlSyntaxParser.Split(tokens.Skip(1).Take((intoStart >= 0 ? intoStart - 1 : selectEnd >= 0 ? selectEnd : tokens.Count) - 1).ToList(), ",")
            : [];
        var locals = evidence.Declarations.Where(d => d.Scope == sql.Routine || d.Scope == "全域").Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind != FourGlTokenKind.Word || Reserved.Contains(t.Text) || tablePositions.Contains(i) || targets.Contains(i)) continue;
            if (valuesStart >= 0 && i > valuesStart && scopes[i] == 0) continue;
            if (set >= 0 && i > set && (where < 0 || i < where) && !setTargets.Contains(i) && locals.Contains(t.Text)) continue;
            if (i + 1 < tokens.Count && tokens[i + 1].Is("(")) continue;
            if (i > 0 && tokens[i - 1].Is(".")) continue;
            var name = t.Text;
            string table;
            var end = i;
            if (i + 2 < tokens.Count && tokens[i + 1].Is(".") && tokens[i + 2].Kind == FourGlTokenKind.Word)
            {
                table = "";
                var searchScope = scopes[i];
                while (true)
                {
                    if (aliases.TryGetValue(searchScope, out var names) && names.TryGetValue(name, out var resolved)) { table = resolved; break; }
                    if (!parents.TryGetValue(searchScope, out searchScope)) break;
                }
                if (table.Length == 0) continue; // record.field is a host variable, not a database alias.
                name = tokens[i + 2].Text;
                end = i + 2;
            }
            else
            {
                // An identifier that is also a declared host variable remains ambiguous in SQL.
                var candidates = scopeTables.GetValueOrDefault(scopes[i]) ?? [];
                table = candidates.Count == 1 ? candidates[0] : "未限定，來源表待確認";
                if (locals.Contains(name) && !(i > 0 && tokens[i - 1].Is("(" ) && sql.Command == "INSERT"))
                {
                    if (where >= 0 && i > where && i > 0 && tokens[i - 1].Text is "=" or "==" or "<>" or ">" or "<" or ">=" or "<=") continue;
                }
            }
            var use = where >= 0 && i > where ? "篩選／比較" : set >= 0 && i > set ? "更新欄位／值" : sql.Command == "INSERT" ? "新增欄位／值" : "查詢投影／分組";
            if (setTargets.Contains(i) && assignmentValues.TryGetValue(name, out var assigned)) use = $"更新目標 ← {assigned}";
            var expression = code[t.Start..tokens[end].End];
            if (end + 1 < tokens.Count && tokens[end + 1].Is("["))
            {
                var close = tokens.FindIndex(end + 1, x => x.Is("]"));
                if (close >= 0) expression = code[t.Start..tokens[close].End];
                use += "（含子字串切片）";
            }
            var projection = selectFields.FindIndex(group => group.Any(x => x.Start == t.Start));
            if (projection >= 0 && projection < sql.Into.Count) use += $" → {sql.Into[projection]}";
            if (!sql.Columns.Any(c => c.Table.Equals(table, StringComparison.OrdinalIgnoreCase) && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && c.Use == use))
                sql.Columns.Add(new(table, name, use, expression, evidence.Snapshot.Span(t.Start, tokens[end].End)));
            i = end;
        }
    }

    private static void ReadComments(FourGlEvidence evidence)
    {
        var pending = new List<FourGlComment>();
        string command = "";
        void Finish()
        {
            if (pending.Count == 0) return;
            var span = evidence.Snapshot.Span(pending[0].Span.StartOffset, pending[^1].Span.EndOffset);
            evidence.Sql.Add(new()
            {
                Id = "", Command = command, RawSql = span.Slice(evidence.Snapshot.Text), Span = span, IsCommented = true,
                Routine = evidence.Routines.FirstOrDefault(r => span.StartOffset >= r.Span.StartOffset && span.EndOffset <= r.Span.EndOffset)?.Name ?? "全域",
                Context = "註解資料，未作為有效依賴或規則", Effect = "停用，沒有有效副作用"
            });
            pending.Clear();
        }
        var segments = new List<FourGlComment>();
        foreach (var comment in evidence.Comments)
        {
            if (!comment.Text.StartsWith('{')) { segments.Add(comment); continue; }
            foreach (Match line in Regex.Matches(comment.Content, @"[^\r\n]+"))
            {
                var span = evidence.Snapshot.Span(comment.Span.StartOffset + 1 + line.Index, comment.Span.StartOffset + 1 + line.Index + line.Length);
                segments.Add(new(span.Slice(evidence.Snapshot.Text), line.Value, span));
            }
        }
        foreach (var comment in segments)
        {
            var content = comment.Content.Trim().TrimStart('#').Trim();
            var match = Regex.Match(content, @"^(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var next = match.Groups[1].Value.ToUpperInvariant();
                if (!(command == "INSERT" && next == "SELECT" && pending.Count > 0)) Finish();
                if (pending.Count == 0) command = next;
                pending.Add(comment);
            }
            else if (pending.Count > 0)
            {
                var gap = comment.Span.StartLine - pending[^1].Span.EndLine;
                if (gap > 1 || content.Length == 0 || !Regex.IsMatch(content, @"^(FROM|WHERE|SET|AND|OR|INTO|VALUES|GROUP|ORDER|HAVING|UNION|JOIN|LEFT|RIGHT|INNER|\(|\)|[,'\""0-9])", RegexOptions.IgnoreCase)) Finish();
                else pending.Add(comment);
            }
        }
        Finish();
    }

    private static void Add(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
    }
}
