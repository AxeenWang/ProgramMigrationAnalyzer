using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Analysis;

/// <summary>Report-level conclusions from same-file evidence, without executing source or assuming a schema.</summary>
internal sealed class FourGlReportNarrator(FourGlEvidence evidence)
{
    public List<FourGlSqlEvidence> Sql { get; } = evidence.Sql.Where(s => !s.IsCommented).OrderBy(s => s.Span.StartOffset).ToList();
    public IEnumerable<string[]> CriticalDataFlows(FourGlRoutine routine) => new FourGlFlowInsights(evidence, Sql).DataFlows(routine);
    public static IEnumerable<FourGlStatement> Nodes(IEnumerable<FourGlStatement> statements)
    {
        foreach (var node in statements)
        {
            yield return node;
            foreach (var child in Nodes(node.Children)) yield return child;
            foreach (var branch in node.Branches) foreach (var child in Nodes(branch.Statements)) yield return child;
        }
    }
    public static string Flat(string value) => string.Concat(Regex.Split(value, @"(""(?:\\.|""""|[^""])*""|'(?:\\.|''|[^'])*')")
        .Select((part, index) => index % 2 == 1 ? part : Regex.Replace(part, @"\s+", " "))).Trim();
    public static string BaseTable(string value) => value.Split(':').Last();
    public string Code(FourGlSqlEvidence sql) => evidence.Routines.SelectMany(r => Nodes(r.Statements))
        .Concat(Nodes(evidence.TopLevel)).FirstOrDefault(n => n.HeaderSpan.StartOffset == sql.Span.StartOffset)?.CodeText ?? sql.RawSql;
    public static string Condition(string value)
    {
        value = Regex.Replace(value, @"^FOREACH\b.*?\bINTO\s+[\w.,* ]+?(?=\s+AND\s|$)", "游標每筆", RegexOptions.IgnoreCase);
        return Flat(FourGlEvidenceAnalyzer.ReadableCondition(value));
    }
    public string Purpose(FourGlRoutine routine)
    {
        var nodes = Nodes(routine.Statements).ToList();
        var sql = Sql.Where(s => s.Routine == routine.Name).ToList();
        var tables = string.Join("、", sql.SelectMany(s => s.Tables).Select(BaseTable).Distinct(StringComparer.OrdinalIgnoreCase));
        var timeDifference = nodes.FirstOrDefault(n => n.Kind == "LET" && n.Expression is not null && Regex.IsMatch(n.Expression, @"^\w+\s*-\s*\w+$") &&
            Regex.Matches(n.Expression, @"\w+").All(m => evidence.Declarations.Any(d => d.Scope == routine.Name && d.Name == m.Value && d.Type.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase))));
        if (timeDifference is not null && nodes.Any(n => n.Kind == "RUN"))
            return "計算兩個來源時間戳的間隔，依 " + string.Join("、", nodes.Where(n => n.Kind == "IF").Select(n => Condition(n.Expression ?? ""))) + " 決定是否交付提醒命令。這個差值不自動代表外部程式執行耗時。";
        if (nodes.Any(n => n.Kind == "FOREACH") && nodes.Any(n => n.Kind == "RUN"))
            return $"從 {tables} 讀取候選資料，逐筆檢查條件後派送外部工作" + (sql.Any(s => s.Command == "UPDATE") ? "，部分分支直接更新資料狀態。" : "。") + (nodes.Any(n => n.Kind == "UNLOAD") ? "另外輸出查詢清單與通知命令。" : "");
        if (sql.Any(s => Regex.IsMatch(Code(s), @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase)) && nodes.Any(n => n.Kind == "RETURN"))
            return $"查詢 {tables} 並分組計數，依 CASE 轉換顯示文字，累積後組合回傳內容。";
        if (sql.Count > 0)
            return $"查詢 {tables}，" + (sql.All(s => Regex.IsMatch(Code(s), @"\bCOUNT\s*\(", RegexOptions.IgnoreCase)) ? "以符合條件的列數決定後續分支或回傳值。" : "將查詢結果用於後續判斷、輸出或資料異動。") ;
        if (nodes.Any(n => n.Kind == "RUN")) return "依條件組合並交付外部命令，觸發條件與參數見規則及命令表。";
        var calls = evidence.Calls.Where(c => c.Routine == routine.Name).Select(c => c.Target).Distinct().ToList();
        if (calls.Count > 0) return "準備輸入與控制條件，依原碼分支呼叫 " + string.Join("、", calls) + "，各分支是否執行見下方階段。";
        return nodes.Any(n => n.Kind == "RETURN") ? "依有效條件計算並回傳結果。" : "處理本單元的變數與輸出，具體操作見下方階段。";
    }
    public string SqlPurpose(FourGlSqlEvidence sql)
    {
        if (sql.Command != "SELECT") return Mutation(sql);
        var code = Code(sql);
        var distinct = Regex.Match(code, @"\bCOUNT\s*\(\s*DISTINCT\s+([^)]*)\)", RegexOptions.IgnoreCase);
        if (distinct.Success) return $"計算 {Flat(distinct.Groups[1].Value)} 去重後的筆數" + Receiver(sql) + (Regex.IsMatch(code, @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase) ? "，按 GROUP BY 分組，各組去重後計數。" : "。");
        if (Regex.IsMatch(code, @"\bCOUNT\s*\(", RegexOptions.IgnoreCase)) return "計算符合 WHERE 的資料列數" + Receiver(sql) + "，不是數量欄位的加總。";
        if (sql.Context.StartsWith("UNLOAD", StringComparison.OrdinalIgnoreCase)) return "輸出符合條件的查詢清單，" + Destination(sql) + "。";
        if (sql.Context.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase)) return "定義游標的候選資料，供對應 FOREACH／FETCH 逐筆接收，不代表宣告時已完成所有處理。";
        return "讀取明示投影值" + Receiver(sql) + "。";
    }
    private static string Receiver(FourGlSqlEvidence sql) => sql.Into.Count > 0 ? "，接至 " + string.Join("、", sql.Into) : "";
    private string Destination(FourGlSqlEvidence sql)
    {
        var target = Regex.Match(sql.Context, @"\bTO\s+([\w.]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var value = evidence.Values.LastOrDefault(v => v.Routine == sql.Routine && v.Name == target && v.Span.StartOffset < sql.Span.StartOffset);
        return value is null ? "目的地見 UNLOAD 原碼" : $"目的地由 {target} 提供：{value.Value}";
    }
    public string Mutation(FourGlSqlEvidence sql)
    {
        if (sql.Command != "UPDATE") return sql.Command == "DELETE" ? "刪除符合 WHERE 的資料列" : "新增 VALUES／SELECT 提供的資料列";
        var assignments = sql.Columns.Where(c => c.Use.StartsWith("更新目標 ← ", StringComparison.Ordinal)).GroupBy(c => c.Use["更新目標 ← ".Length..]);
        var values = assignments.Select(g => string.Join("、", g.Select(c => c.Name).Distinct()) + " ← " + g.Key).ToList();
        return values.Count > 0 ? "更新 " + string.Join("，", values) : "更新符合 WHERE 的資料列，指定值見完整 SQL";
    }
    public IEnumerable<string[]> Phases(FourGlRoutine routine)
    {
        var list = routine.Statements.Where(n => n.Kind != "DEFINE").ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var n = list[i];
            if (n.Kind is "LET" or "DISPLAY")
            {
                var group = new List<FourGlStatement> { n };
                while (i + 1 < list.Count && list[i + 1].Kind is "LET" or "DISPLAY") group.Add(list[++i]);
                var targets = group.Where(x => x.Kind == "LET").Select(x => x.Target).Distinct().ToList();
                yield return ["準備資料／輸出", (targets.Count > 0 ? "準備 " + string.Join("、", targets) : "輸出畫面內容") + $"（{group.Count} 個語句，宣告及重要關聯另列）", Range(group[0].Span, group[^1].Span)];
            }
            else if (n.Kind is "FOREACH" or "FOR" or "WHILE")
            {
                var children = Nodes(n.Children).ToList();
                yield return ["逐筆／重複處理", Flat(n.CodeText) + $"。迴圈包含 {children.Count(x => x.Kind == "IF")} 個 IF、{children.Count(x => x.Kind == "CASE")} 個 CASE、{children.Count(x => x.Kind == "RUN")} 個 RUN 位置。條件與例外分開列於規則／路由表，保留來源順序。", n.Span.Lines];
            }
            else if (n.Kind is "IF" or "CASE") yield return ["條件分派", n.Kind == "IF" ? Condition(n.Expression ?? "") + "。" + string.Join("。", n.Branches.Select((b, index) => (index == 0 ? "成立時：" : "否則：") + Outcome(b.Statements))) : "依 " + n.Expression + $" 選擇 {n.Branches.Count} 個分支，見規則／命令路由。", n.Span.Lines];
            else
            {
                var sql = Sql.FirstOrDefault(s => s.Span.StartOffset == n.HeaderSpan.StartOffset);
                yield return [sql?.Id ?? n.Kind, sql is not null ? SqlPurpose(sql) : n.Kind == "CALL" ? $"呼叫 {n.Target}({string.Join(", ", n.Arguments)})" + (n.Returning.Count > 0 ? "，接回 " + string.Join("、", n.Returning) : "") : Flat(FourGlEvidenceAnalyzer.Describe(n)), n.HeaderSpan.Lines];
            }
        }
    }
    public string Outcome(IEnumerable<FourGlStatement> statements)
    {
        var parts = new List<string>();
        foreach (var n in statements.Where(x => x.Kind != "DEFINE"))
        {
            var sql = Sql.FirstOrDefault(s => s.Span.StartOffset == n.HeaderSpan.StartOffset);
            if (sql is not null) parts.Add(sql.Id + " " + SqlPurpose(sql));
            else if (n.Kind is "CONTINUE" or "STOP" or "RETURN" or "EXIT") parts.Add(FourGlEvidenceAnalyzer.Describe(n));
            else if (n.Kind == "CALL") parts.Add("呼叫 " + n.Target);
            else if (n.Kind == "RUN") parts.Add("交付外部命令（" + n.HeaderSpan.Lines + "，完整模板與參數見命令路由）");
            else if (n.Kind == "LET" && n.Expression is not null && n.Expression.Length < 100 && n.Expression is not "0" and not "\"\"" and not "''") parts.Add(n.Target + " ← " + n.Expression);
            else if (n.Kind == "CASE") parts.Add("依 " + n.Expression + " 分支處理，詳細命令另列");
            else if (n.Kind == "IF") parts.Add("再判斷 " + Condition(n.Expression ?? ""));
            else if (n.Kind == "FOREACH") parts.Add("巡覽游標並處理各筆資料");
        }
        return parts.Count == 0 ? "進行分支內資料準備／輸出，原文可依行號回查" : string.Join("。", parts.Take(6).Select(p => p.TrimEnd('。'))) + (parts.Count > 6 ? $"。另有 {parts.Count - 6} 個後續操作，見來源範圍" : "");
    }
    public IEnumerable<string[]> Rules()
    {
        var rows = new List<string[]>();
        foreach (var r in evidence.Routines)
        foreach (var n in Nodes(r.Statements).Where(n => n.Kind is "IF" or "CASE"))
        {
            var flow = evidence.Flow.FirstOrDefault(f => f.Routine == r.Name && f.Span.StartOffset == n.HeaderSpan.StartOffset);
            var path = flow?.Path ?? "無額外條件";
            if (n.Kind == "CASE")
            {
                if (n.Branches.All(b => b.Statements.All(x => x.Kind == "LET")))
                    rows.Add([r.Name, "CASE " + n.Expression, $"依 {n.Branches.Count} 個分支將代碼轉換為程式值，對照表見下方。", n.Span.Lines]);
                else rows.Add([r.Name, "CASE " + n.Expression, "依分類值派送工作。分支內各 IF 仍各自判斷，須保留所有 RUN 及先後次序，不能把整個分支當成只執行一個命令。", n.Span.Lines]);
                continue;
            }
            if (n.Branches.Count == 2 && n.Branches.All(b => b.Statements.Count == 1 && b.Statements[0].Kind == "LET") && n.Branches[0].Statements[0].Target == n.Branches[1].Statements[0].Target)
            {
                rows.Add([r.Name, "於列示來源位置判斷 " + Condition(n.Expression ?? ""), $"{n.Branches[0].Statements[0].Target}：成立時 {n.Branches[0].Statements[0].Expression}，否則 {n.Branches[1].Statements[0].Expression}", n.Span.Lines]);
                continue;
            }
            foreach (var b in n.Branches)
                rows.Add([r.Name, Condition((path == "無額外條件" ? "" : path + " AND ") + b.Condition), Outcome(b.Statements), b.Span.Lines]);
        }
        // Identical choice rules (for example NORMAL/REPACKING) retain every source location once.
        return rows.GroupBy(r => (r[0], r[1], r[2])).Select(g => new[] { g.Key.Item1, g.Key.Item2, g.Key.Item3, string.Join("、", g.Select(r => r[3])) });
    }
    public IEnumerable<string[]> Mappings()
    {
        foreach (var r in evidence.Routines)
        foreach (var n in Nodes(r.Statements).Where(n => n.Kind == "CASE" && n.Branches.All(b => b.Statements.Count == 1 && b.Statements[0].Kind == "LET")))
            yield return [r.Name, n.Expression ?? "", string.Join("、", n.Branches.Select(b => Flat(b.Condition) + " → " + b.Statements[0].Expression)), n.Span.Lines];
    }
    public IEnumerable<(string Text, SourceSpan Span)> Findings()
    {
        foreach (var finding in new FourGlFlowInsights(evidence, Sql).Findings()) yield return finding;
        var nodes = evidence.Routines.SelectMany(r => Nodes(r.Statements)).Concat(Nodes(evidence.TopLevel)).ToList();
        foreach (var sql in Sql.Where(s => s.Command == "UPDATE"))
            yield return ($"{sql.Id} 的 UPDATE 邊界只有原碼 WHERE：{Flat(sql.Predicate)}。本檔沒有資料表唯一鍵證據，不能假定只影響一列，也不能以分支中的限制取代 UPDATE 的 WHERE。", sql.Span);
        if (Sql.Any(s => s.Command == "UPDATE") && evidence.Commands.Count > 0 && !nodes.Any(n => n.Kind is "BEGIN" or "COMMIT" or "ROLLBACK"))
            yield return ("本檔未見明示交易將資料異動與外部命令包成同一原子操作，RUN 也不代表外部工作成功或已回寫完成狀態。", nodes.First().HeaderSpan);
        foreach (var call in evidence.Calls.Where(c => c.IsLocal && c.Returning.Count > 0))
        foreach (var name in call.Returning)
            if (!evidence.SymbolUses.Any(u => u.Routine == call.Routine && u.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && u.Access == "讀取" && u.Span.StartOffset > call.Span.StartOffset))
                yield return ($"{call.Routine} 呼叫 {call.Target} 接回 {name}，但未見後續有效讀取。不能將註解中的舊判斷視為現行商業閘門。", call.Span);
        foreach (var r in evidence.Routines)
        {
            var local = Nodes(r.Statements).ToList();
            var hasTimeSlices = local.Any(n => n.Expression is not null && Regex.IsMatch(n.Expression, @"\[12,\s*13\].*\[15,\s*16\].*\[18,\s*19\]"));
            if (hasTimeSlices)
            {
                foreach (var n in local.Where(n => n.Kind == "IF" && n.Branches.Any(b => b.Statements.Any(x => x.Kind == "LET" && x.Expression == "0")) && n.Branches.Any(b => b.Statements.Any(x => x.Expression is not null && Regex.IsMatch(x.Expression, @"\w+\s*-\s*\d{5,6}$")))))
                    yield return ($"本單元先將時間拼成 HHMMSS 數值，再依 {Condition(n.Expression ?? "")} 做數值減法，另一分支設為 0。這段沒有跨日回推處理，不能概括成任何時刻都完整往前同一時間窗。時間門檻只影響實際引用它的 SQL，不能套用到所有游標。", n.HeaderSpan);
            }
            foreach (var diff in local.Where(n => n.Kind == "LET" && n.Expression is not null))
            {
                var m = Regex.Match(diff.Expression!, @"^(\w+)\s*-\s*(\w+)$");
                if (!m.Success || !m.Groups.Cast<Group>().Skip(1).All(g => evidence.Declarations.Any(d => d.Scope == r.Name && d.Name == g.Value && d.Type.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase)))) continue;
                string Origin(string name) => local.LastOrDefault(n => n.Kind == "LET" && n.Target == name && n.Span.StartOffset < diff.Span.StartOffset)?.Expression ?? name;
                var end = Origin(m.Groups[1].Value); var start = Origin(m.Groups[2].Value);
                yield return ($"時間差 {diff.Target} = {end} - {start}。這是兩個來源時間戳之差，不是自動量測外部命令耗時。若來源時間戳在 RUN 前設定，這段不包含該命令完成時刻。", diff.HeaderSpan);
            }
        }
        foreach (var n in nodes.Where(n => n.Kind == "SET" && n.CodeText.Contains("DIRTY READ", StringComparison.OrdinalIgnoreCase)))
            yield return ("來源明示 DIRTY READ，不能假定候選清單、計數與後續派送使用已提交的固定快照。", n.HeaderSpan);
        foreach (var finding in MappingFindings()) yield return finding;
        var groupedDistinct = Sql.Where(s => Regex.IsMatch(Code(s), @"COUNT\s*\(\s*DISTINCT\b", RegexOptions.IgnoreCase) && Regex.IsMatch(Code(s), @"GROUP\s+BY", RegexOptions.IgnoreCase)).ToList();
        if (groupedDistinct.Count > 0 && Sql.Any(s => Regex.IsMatch(Code(s), @"COUNT\s*\(\s*DISTINCT\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(Code(s), @"GROUP\s+BY", RegexOptions.IgnoreCase)))
            yield return ("本檔同時有整體與分組的 COUNT(DISTINCT)。各組先去重再相加，不保證等於跨所有群組只去重一次，是否相等取決於資料及鍵的唯一性。", groupedDistinct[0].Span);
        foreach (var v in evidence.Values.Where(v => v.Value.Contains("超過有界", StringComparison.Ordinal)).GroupBy(v => (v.Routine, v.Name)).Select(g => g.First()))
            yield return ($"{v.Routine} 的 {v.Name} 已實際碰到符號分析上限，候選不足。這是分析資料流限制，不等同來源沒有規則。", v.Span);
    }
    private IEnumerable<(string Text, SourceSpan Span)> MappingFindings()
    {
        foreach (var r in evidence.Routines)
        foreach (var loop in Nodes(r.Statements).Where(n => n.Kind == "FOREACH"))
        {
            var into = Regex.Match(loop.CodeText, @"FOREACH\s+(\w+)\s+INTO\s+(\w+)\s*,", RegexOptions.IgnoreCase);
            if (!into.Success) continue;
            var query = Sql.FirstOrDefault(s => s.Routine == r.Name && Regex.IsMatch(s.Context, @"^DECLARE\s+" + Regex.Escape(into.Groups[1].Value) + @"\b", RegexOptions.IgnoreCase));
            if (query is null) continue;
            var firstColumn = Regex.Match(Code(query), @"^SELECT\s+(?:DISTINCT\s+)?(\w+(?:\.\w+)?)\s*,", RegexOptions.IgnoreCase).Groups[1].Value;
            if (firstColumn.Length == 0) continue;
            var values = Regex.Matches(Code(query), @"\b" + Regex.Escape(firstColumn) + "\\s*=\\s*(['\"])(.*?)\\1", RegexOptions.IgnoreCase).Select(m => m.Groups[2].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var map in Nodes(loop.Children).Where(n => n.Kind == "CASE" && string.Equals(n.Expression, into.Groups[2].Value, StringComparison.OrdinalIgnoreCase) && n.Branches.All(b => b.Statements.Count == 1 && b.Statements[0].Kind == "LET")))
            {
                var keys = map.Branches.Where(b => !b.Condition.StartsWith("前述", StringComparison.Ordinal)).Select(b => Regex.Match(b.Condition, "(['\"])(.*?)\\1$").Groups[2].Value).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (values.Count == 0 || keys.Count == 0) continue;
                var missing = values.Except(keys, StringComparer.OrdinalIgnoreCase).ToList();
                var extra = keys.Except(values, StringComparer.OrdinalIgnoreCase).ToList();
                var fallback = map.Branches.FirstOrDefault(b => b.Condition.StartsWith("前述", StringComparison.Ordinal))?.Statements[0].Expression ?? "未提供預設值";
                if (missing.Count > 0) yield return ($"查詢篩選包含 {string.Join("、", missing)}，但 {r.Name} 的 CASE 對照未明列，會使用預設分支 {fallback}。", map.Span);
                if (extra.Count > 0) yield return ($"{r.Name} 保留 {string.Join("、", extra)} 的 CASE 對照，但本游標查詢的明示等值篩選未包含這些值，須區分保留對照與目前會選入的資料。", map.Span);
            }
        }
    }
    public string FieldRole(string table, string name, IEnumerable<FourGlColumn> columns)
    {
        var list = columns.ToList();
        var roles = new List<string>();
        var refs = evidence.RecordFields.Where(f => BaseTable(f.Table).Equals(BaseTable(table), StringComparison.OrdinalIgnoreCase) && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (refs.Any(f => evidence.Routines.Where(r => r.Name == f.Routine).SelectMany(r => Nodes(r.Statements)).Any(n => n.Kind == "CASE" && n.Expression == f.Expression))) roles.Add("用作分類／派送對象識別（由 CASE 使用推定）");
        foreach (var g in list.Where(c => c.Use.StartsWith("更新目標 ← ", StringComparison.Ordinal)).GroupBy(c => c.Use["更新目標 ← ".Length..])) roles.Add("更新時接收 " + g.Key);
        if (list.Any(c => c.Use.Contains("含子字串", StringComparison.Ordinal))) roles.Add("依字元前綴／切片分類或篩選");
        if (list.Any(c => c.Use.Contains("→", StringComparison.Ordinal))) roles.Add("查詢結果交付程式接收變數，計數投影不等於直接複製欄位值");
        if (roles.Count == 0) roles.Add(string.Join("、", list.Select(c => c.Use.Split('：')[0]).Distinct()));
        return string.Join("。", roles);
    }
    public static string Range(SourceSpan first, SourceSpan last) => first.StartLine == last.EndLine ? $"L{first.StartLine}" : $"L{first.StartLine}–L{last.EndLine}";
}
