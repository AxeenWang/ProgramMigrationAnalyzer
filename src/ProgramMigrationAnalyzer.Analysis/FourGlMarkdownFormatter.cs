using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Analysis;

internal sealed class FourGlMarkdownFormatter(AnalysisResult result)
{
    private readonly FourGlEvidence _evidence = result.FourGl!;
    private readonly StringBuilder _text = new();

    public string Generate()
    {
        var e = _evidence;
        var narrator = new FourGlReportNarrator(e);
        var sql = narrator.Sql;
        var nodes = Nodes(e.TopLevel.Concat(e.Routines.SelectMany(r => r.Statements))).ToList();
        H(1, result.Source.FileName + " 程式分析報告");
        P("單支來源的離線靜態分析，不執行 SQL、Shell 或其他程式。有效碼、作者註解及推論分開標示，外部資料庫結構與執行結果未知。");
        H(2, "1. 分析範圍與來源");
        RowTable(["項目", "內容"], [
            ["來源", result.Source.FileName], ["編碼／行數", $"{e.Snapshot.EncodingName}，{e.Snapshot.LineCount} 行"],
            ["原始位元組 SHA-256", e.Snapshot.ByteSha256 ?? "未提供，記憶體來源無法重建原始位元組"],
            ["解碼文字 SHA-256", e.Snapshot.TextSha256],
            ["結構", $"{e.Routines.Count(r => r.Kind == "MAIN")} MAIN、{e.Routines.Count(r => r.Kind == "FUNCTION")} FUNCTION、{e.Routines.Count(r => r.Kind == "REPORT")} REPORT"],
            ["SQL", $"{sql.Count} 個有效 DML、{e.Sql.Count(s => s.IsCommented)} 個停用區塊，按來源位置計數"],
            ["證據位置", "行欄從1起算，UTF-16字元偏移的結束位置不包含在範圍內。SQL保留原文、換行與尾行註解。"]
        ]);
        H(2, "2. 程式用途與輸入輸出");
        if (nodes.Any(n => n.Kind == "FOREACH") && e.Commands.Count > 0)
            P("依有效碼歸納，本程式從資料庫選取候選資料，逐筆檢查並依條件派送外部工作" + (sql.Any(s => s.Command == "UPDATE") ? "，部分例外路徑會直接更新資料狀態。" : "。") + "外部程式內部處理及成功回寫不由本檔證明。");
        else P(string.Join(" ", e.Routines.Where(r => r.Kind == "MAIN").Select(narrator.Purpose).DefaultIfEmpty("本檔提供下列處理單元，由呼叫端決定執行方式。")));
        var first = e.Routines.Select(r => r.Span.StartOffset).DefaultIfEmpty(int.MaxValue).Min();
        var background = e.Comments.Where(c => c.Span.StartOffset < first && Regex.IsMatch(c.Content, @"[\p{L}]")).Select(c => FourGlReportNarrator.Flat(c.Content).TrimStart('#', '*', ' '))
            .Where(c => c.Length is > 3 and < 200 && !Regex.IsMatch(c, @"^(?:Modify\s+Date|Author|Created|Version)\b|^(?:Description|CRON):?$|^[A-Za-z ]+:$|^\d+\.\s*(?:call|run)\b", RegexOptions.IgnoreCase)).Take(3).ToList();
        if (background.Count > 0) P("作者檔頭描述（註解背景，不代表已驗證排程或現行行為）：" + string.Join("。", background));
        var io = nodes.Where(n => n.Kind is "DATABASE" or "INPUT" or "PROMPT").Select(n => new[] { n.Kind, FourGlReportNarrator.Flat(n.CodeText), n.HeaderSpan.Lines }).ToList();
        io.AddRange(sql.Where(s => s.Context.StartsWith("UNLOAD", StringComparison.OrdinalIgnoreCase)).Select(s => new[] { "查詢清單", s.Id + " " + narrator.SqlPurpose(s), s.Span.Lines }));
        if (e.Commands.Count > 0) io.Add(["外部工作", $"{e.Commands.Count} 個有效 RUN 位置，命令與條件統一列於第5節。位置數不是單次執行次數。", string.Join("、", e.Commands.Select(c => c.Routine).Distinct())]);
        RowTable(["類別", "內容", "證據"], io);
        H(2, "3. 各單元用途與處理階段");
        foreach (var r in e.Routines)
        {
            H(3, r.Name + "，" + r.Span.Lines);
            P(narrator.Purpose(r));
            P("參數順序：" + (r.Parameters.Count > 0 ? string.Join("、", r.Parameters) : "無") + "。宣告型別統一列於第8節。");
            RowTable(["階段", "處理說明", "來源"], narrator.Phases(r));
            var critical = narrator.CriticalDataFlows(r).ToList();
            if (critical.Count > 0) RowTable(["關鍵資料流", "處理意義", "來源"], critical);
        }
        if (e.Routines.Count == 0) P("未辨識到程式單元，請先閱讀解析診斷，不能視為已完整分析。");
        H(2, "4. 商業規則、狀態及例外");
        P("以下由條件及後果推導處理規則，正式商業名稱仍需確認。重複的值選擇合併列出來源位置，不代表不經前置分支即可執行。");
        RowTable(["單元", "成立條件", "結果", "證據"], narrator.Rules());
        H(3, "4.1 狀態異動與影響邊界");
        RowTable(["SQL", "觸發條件", "指定值", "篩選邊界"], sql.Where(s => s.Command != "SELECT").Select(s => new[] { s.Id + " " + s.Span.Lines, FourGlReportNarrator.Condition(s.Path), narrator.Mutation(s), s.Predicate.Length > 0 ? FourGlReportNarrator.Flat(s.Predicate) : "未見 WHERE，需確認影響範圍" }));
        P("旗標意義須連同前置查詢及 CONTINUE／分支判斷解讀。查到某完成值不代表本檔寫入該值，RUN 未接收狀態也不代表外部工作成功。");
        H(3, "4.2 程式內對照與固定值選擇");
        RowTable(["單元", "分類值", "原碼對照", "證據"], narrator.Mappings().GroupBy(r => (r[0], r[1], r[2])).Select(g => new[] { g.Key.Item1, g.Key.Item2, g.Key.Item3, string.Join("、", g.Select(r => r[3])) }));
        H(2, "5. 外部命令與同檔呼叫");
        P("每個 RUN 的模板與條件均保留，相同模板及條件合併來源位置，執行順序以行號為準。未知參數不推定實際值，同分支多個 IF／RUN 須保留各自判斷與順序。");
        RowTable(["單元", "命令模板", "條件", "證據／狀態"], e.Commands.GroupBy(c => (c.Template, Path: FourGlReportNarrator.Condition(c.Path), c.Status)).Select(g => new[] { string.Join("、", g.Select(c => c.Routine).Distinct()), g.Key.Template, g.Key.Path, string.Join("、", g.Select(c => c.Span.Lines)) + "。" + g.Key.Status }));
        RowTable(["呼叫者 → 被呼叫者", "參數 → 接收值", "條件", "證據"], e.Calls.Select(c => new[] { c.Routine + " → " + c.Target + (c.IsLocal ? "（同檔）" : "（外部／內建）"), string.Join("、", c.Arguments) + " → " + (c.Returning.Count > 0 ? string.Join("、", c.Returning) : "未接收"), FourGlReportNarrator.Condition(c.Path), c.Span.Lines }));
        H(2, "6. 完整有效 SQL");
        P("每個有效 SQL 來源位置完整列出一次。WHERE 原文即為完整條件，下方只補用途與上下文，不再逐字複製一份翻譯。");
        foreach (var s in sql)
        {
            H(3, $"{s.Id}：{s.Command}，{s.Routine}，{s.Span.Lines}");
            P(narrator.SqlPurpose(s));
            P("上下文：" + FourGlReportNarrator.Flat(s.Context) + "。路徑：" + FourGlReportNarrator.Condition(s.Path));
            Code(s.RawSql);
        }
        H(3, "6.1 資料庫、交易與動態 SQL 上下文");
        RowTable(["操作", "原碼", "證據"], nodes.Where(n => n.Kind is "DATABASE" or "SET" or "BEGIN" or "COMMIT" or "ROLLBACK" or "WHENEVER" or "DECLARE" or "FOREACH" or "PREPARE" or "EXECUTE" or "OPEN" or "CLOSE" or "FETCH").Select(n => new[] { n.Kind, FourGlReportNarrator.Flat(n.CodeText), n.HeaderSpan.Lines }));
        foreach (var v in e.Values.Where(v => v.Expression.Contains("動態 SQL", StringComparison.Ordinal))) { P(v.Routine + " " + v.Span.Lines + "，" + v.Expression); Code(v.Value); }
        H(2, "7. 資料表與欄位用途");
        P("同一邏輯表的欄位用法合併說明，實際限定位置另列，未推定不同平台 schema 相同。未限定欄位保留歧義，所有資料庫型別、鍵與可空性均待確認。SELECT *／RECORD LIKE 不展開外部 schema，COUNT(*) 是計數。");
        RowTable(["資料位置", "操作", "來源 SQL"], sql.SelectMany(s => s.Tables.Select(t => (Table: t, Sql: s))).GroupBy(x => x.Table, StringComparer.OrdinalIgnoreCase).Select(g => new[] { g.Key, string.Join("、", g.Select(x => x.Sql.Command).Distinct()), string.Join("、", g.Select(x => x.Sql.Id)) }));
        var fields = sql.SelectMany(s => s.Columns.Select(c => (Column: c, Sql: s))).GroupBy(x => (Table: FourGlReportNarrator.BaseTable(x.Column.Table).ToLowerInvariant(), Name: x.Column.Name.ToLowerInvariant()));
        RowTable(["邏輯表／來源", "欄位", "程式內用途或推論", "證據"], fields.Select(g => new[] { g.Key.Table, g.Key.Name, narrator.FieldRole(g.Key.Table, g.Key.Name, g.Select(x => x.Column)), string.Join("、", g.Select(x => x.Sql.Id).Distinct()) }));
        var recordOnly = e.RecordFields.Where(f => !sql.SelectMany(s => s.Columns).Any(c => FourGlReportNarrator.BaseTable(c.Table).Equals(FourGlReportNarrator.BaseTable(f.Table), StringComparison.OrdinalIgnoreCase) && c.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase)));
        RowTable(["RECORD 來源", "額外有效欄位引用", "證據"], recordOnly.GroupBy(f => (Table: FourGlReportNarrator.BaseTable(f.Table), f.Name)).Select(g => new[] { g.Key.Table, g.Key.Name + "（程式引用，型別未知）", string.Join("、", g.Select(f => f.Span.Lines).Distinct()) }));
        H(2, "8. 宣告與重要資料流");
        RowTable(["作用域", "變數", "原碼型別", "宣告證據"], e.Declarations.GroupBy(d => (d.Scope, d.Type)).Select(g => new[] { g.Key.Scope, string.Join("、", g.Select(d => d.Name)), g.Key.Type, string.Join("、", g.Select(d => d.Span.Lines).Distinct()) }));
        P("第3節另列時間基準、日期時間切片、分支前綴與每筆命令重設等關鍵資料流。此處列全域資料及呼叫回傳關聯，一般暫存賦值由來源行號回查。");
        var globals = e.Declarations.Where(d => d.Scope == "全域").Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        RowTable(["單元／變數", "來源", "證據"], e.Values.Where(v => globals.Contains(v.Name) || v.Expression.Contains("RETURNING", StringComparison.Ordinal)).GroupBy(v => (v.Routine, v.Name, v.Expression)).Select(g => new[] { g.Key.Routine + " " + g.Key.Name, g.Key.Expression, string.Join("、", g.Select(v => v.Span.Lines)) }));
        P("CASE 內同一命令變數可多次覆寫，後續 CALL 取得該路徑最後留下的值，並非全部已執行命令。有界候選不等於已證明可執行路徑。");
        H(2, "9. 具體發現、限制與待確認事項");
        RowTable(["發現／影響", "證據"], narrator.Findings().GroupBy(f => f.Text).Select(g => new[] { g.Key, string.Join("、", g.Select(f => f.Span.Lines)) }));
        RowTable(["完整度", "結果"], [
            ["辨識／未知語句", $"{e.RecognizedStatements}／{e.UnknownStatements}"], ["解析診斷", e.Diagnostics.Count.ToString()],
            ["分析界線", "迴圈零次／一次合併，同檔呼叫最多8層、200000步，字串與條件8192字元，一般值16候選、命令變數256候選。超限保留未知，不代表沒有商業規則。"],
            ["外部未知", "不跨檔、不建立字典，不推導外部工作成功、重試、郵件或 FTP 結果，不含轉譯。辨識語句數不等於完整語意理解。"]
        ]);
        foreach (var d in e.Diagnostics) { P(d.Code + "，" + d.Span.Lines + "：" + d.Message); if (d.Span.Length > 0) Code(d.Span.Slice(e.Snapshot.Text)); }
        H(2, "10. 完整停用 SQL");
        P("只保留停用 SQL 原文，不計入有效流程、依賴或欄位。其他作者註解不逐段重印，可由來源行號回查，註解不覆蓋有效碼。");
        foreach (var s in e.Sql.Where(s => s.IsCommented).OrderBy(s => s.Span.StartOffset)) { H(3, $"{s.Id}：{s.Command}，{s.Routine}，{s.Span.Lines}"); Code(s.RawSql); }
        _text.AppendLine("## Migration Assessment").AppendLine();
        P($"共同模型相容指標：Functions {result.Assessment.FunctionCount}，SQL Statements {result.Assessment.SqlStatementCount}。不作為商業語意完整理解或移植工時證據。");
        return _text.ToString();
    }

    private void H(int level, string title) => _text.Append('#', level).Append(' ').AppendLine(E(title)).AppendLine();
    private void P(string text) => _text.AppendLine(E(text)).AppendLine();
    private void RowTable(string[] headers, IEnumerable<string[]> rows)
    {
        _text.AppendLine("| " + string.Join(" | ", headers.Select(E)) + " |");
        _text.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");
        var any = false;
        foreach (var row in rows) { _text.AppendLine("| " + string.Join(" | ", row.Select(E)) + " |"); any = true; }
        if (!any) _text.AppendLine("| " + string.Join(" | ", headers.Select((_, i) => i == 0 ? "本檔未辨識到對應證據" : "")) + " |");
        _text.AppendLine();
    }
    private void Code(string raw)
    {
        var length = Regex.Matches(raw, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, length + 1));
        _text.Append(fence).AppendLine("4gl").Append(raw);
        if (!raw.EndsWith('\n') && !raw.EndsWith('\r')) _text.AppendLine();
        _text.AppendLine(fence).AppendLine();
    }
    private static string E(string text)
    {
        var escaped = text.Replace("\\", "\\\\");
        foreach (var c in new[] { '`', '*', '_', '|', '[', ']', '#', '~' }) escaped = escaped.Replace(c.ToString(), "\\" + c);
        return WebUtility.HtmlEncode(escaped).ReplaceLineEndings("<br>");
    }
    private static IEnumerable<FourGlStatement> Nodes(IEnumerable<FourGlStatement> statements)
    {
        foreach (var node in statements)
        {
            yield return node;
            foreach (var child in Nodes(node.Children)) yield return child;
            foreach (var branch in node.Branches) foreach (var child in Nodes(branch.Statements)) yield return child;
        }
    }
}
