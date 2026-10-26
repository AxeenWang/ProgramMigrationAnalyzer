using ProgramMigrationAnalyzer.Core;
using System.Text.RegularExpressions;

namespace ProgramMigrationAnalyzer.Parsers;

public sealed class Informix4GlParser : ISourceParser
{
    public bool CanParse(SourceDocument source) => source.Language switch
    {
        SourceLanguage.Informix4Gl => true,
        SourceLanguage.CSharp => false,
        _ => Path.GetExtension(source.FileName).Equals(".4gl", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(source.FileName).Equals(".per", StringComparison.OrdinalIgnoreCase)
    };

    public Task<AnalysisResult> ParseAsync(SourceDocument source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidence = new FourGlEvidence { Snapshot = new SourceSnapshot(source) };
        var result = new AnalysisResult { Source = source, Language = SourceLanguage.Informix4Gl, FourGl = evidence };
        var lexed = FourGlLexer.Lex(evidence, cancellationToken);
        new FourGlSyntaxParser(evidence, lexed, cancellationToken).Parse();
        FourGlSqlReader.Read(evidence, lexed, cancellationToken);
        foreach (var routine in evidence.Routines)
        {
            var records = evidence.Declarations.Where(d => d.Scope == routine.Name || d.Scope == "全域")
                .Where(d => Regex.IsMatch(d.Type, @"^RECORD\s+LIKE\b", RegexOptions.IgnoreCase))
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,
                    g => Regex.Replace(g.First().Type, @"^RECORD\s+LIKE\s+|\.\*\s*$", "", RegexOptions.IgnoreCase).Trim(), StringComparer.OrdinalIgnoreCase);
            var tokens = lexed.Tokens.Where(t => t.Start >= routine.Span.StartOffset && t.End <= routine.Span.EndOffset).ToList();
            for (var index = 0; index + 2 < tokens.Count; index++)
            {
                var record = tokens[index];
                if (!tokens[index + 1].Is(".") || tokens[index + 2].Kind != FourGlTokenKind.Word || !records.TryGetValue(record.Text, out var table)) continue;
                var field = tokens[index + 2];
                evidence.RecordFields.Add(new(routine.Name, record.Text, table, field.Text, lexed.Code[record.Start..field.End], evidence.Snapshot.Span(record.Start, field.End)));
            }
        }
        foreach (var routine in evidence.Routines)
        {
            var function = new FunctionInfo
            {
                Name = routine.Name, Kind = routine.Kind switch { "MAIN" => "Main", "REPORT" => "Report", _ => "Function" },
                StartLine = routine.Span.StartLine, EndLine = routine.Span.EndLine,
                ReturnType = routine.Kind is "MAIN" or "REPORT" ? "void" : "object?",
                Summary = routine.Kind == "MAIN" ? "4GL 程式進入點。" : "4GL 程式內定義的處理單元。"
            };
            foreach (var name in routine.Parameters)
                function.Parameters.Add(new() { Name = name, Type = evidence.Declarations.FirstOrDefault(d => d.Scope == routine.Name && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Type ?? "object?" });
            foreach (var call in Descendants(routine.Statements).Where(n => n.Kind == "CALL" && n.Target is not null))
                SqlParsing.AddUnique(function.CalledFunctions, call.Target!);
            foreach (var token in lexed.Tokens.Where(t => t.Start >= routine.Span.StartOffset && t.End <= routine.Span.EndOffset))
                if (evidence.Routines.Any(r => r.Name.Equals(token.Text, StringComparison.OrdinalIgnoreCase) && r.Name != routine.Name))
                    SqlParsing.AddUnique(function.CalledFunctions, token.Text);
            foreach (var table in evidence.Sql.Where(s => !s.IsCommented && s.Routine == routine.Name).SelectMany(s => s.Tables))
                SqlParsing.AddUnique(function.ReferencedTables, table);
            result.Functions.Add(function);
            var unit = new ProgramUnit { Name = routine.Name, Kind = function.Kind, StartLine = function.StartLine, EndLine = function.EndLine };
            unit.Members.AddRange(evidence.Declarations.Where(d => d.Scope == routine.Name).Select(d => $"DEFINE: {d.Name} {d.Type}"));
            result.ProgramUnits.Add(unit);
        }
        foreach (var sql in evidence.Sql.Where(s => !s.IsCommented).OrderBy(s => s.Span.StartOffset))
        {
            var common = new SqlStatementInfo { CommandType = sql.Command, RawSql = sql.RawSql, StartLine = sql.Span.StartLine, Risk = sql.Command == "SELECT" ? "Read" : "Write" };
            common.Tables.AddRange(sql.Tables);
            result.SqlStatements.Add(common);
        }
        foreach (var node in Descendants(evidence.TopLevel.Concat(evidence.Routines.SelectMany(r => r.Statements))))
        {
            if (node.Kind is "IF" or "CASE" or "FOR" or "FOREACH" or "WHILE" or "RETURN") result.DetectedBehaviors.Add($"{node.Kind} at line {node.Span.StartLine}");
            var dependency = node.Kind switch
            {
                "DATABASE" => ("Informix Database", "Database", "原始碼 DATABASE 宣告。"),
                "INPUT" or "DISPLAY" or "PROMPT" => ("4GL Screen Interaction", "UI", "終端畫面輸入或輸出。"),
                "BEGIN" or "COMMIT" or "ROLLBACK" => ("Transaction Control", "Database", "明示交易控制。"),
                "DECLARE" or "FOREACH" or "FETCH" => ("Database Cursor", "Database", "游標與逐列處理。"),
                "RUN" => ("External Command", "Process", "來源中的命令模板，分析器不執行。"),
                "UNLOAD" => ("UNLOAD Output", "File System", "查詢結果輸出目的地。"),
                "REPORT" or "OUTPUT" or "PRINT" => ("4GL Report", "Report", "4GL 報表輸出。"),
                _ => default
            };
            if (dependency.Item1 is not null && !result.Dependencies.Any(d => d.Name == dependency.Item1))
            {
                result.Dependencies.Add(new() { Name = dependency.Item1, Category = dependency.Item2, Description = dependency.Item3, Risk = "Review" });
                result.Tags.Add(new("Architecture", dependency.Item2));
            }
        }
        if (evidence.Declarations.Any(d => d.Scope == "全域")) result.Tags.Add(new("Architecture", "Global State"));
        if (evidence.Routines.Count == 0 && source.Content.Length > 0)
            evidence.Diagnostics.Add(new("4GL-STRUCTURE", "未辨識到 MAIN、FUNCTION 或 REPORT，保留已讀取語句與原文。", evidence.Snapshot.Span(0, source.Content.Length)));
        foreach (var diagnostic in evidence.Diagnostics)
            result.MigrationIssues.Add(new() { Code = diagnostic.Code, Title = "4GL 解析待確認", Description = diagnostic.Message, Severity = IssueSeverity.Medium, SuggestedAction = "核對附帶行號的來源證據。", Location = diagnostic.Span.Lines });
        result.Summary = $"單檔離線分析辨識 {evidence.Routines.Count} 個程式單元、{result.SqlStatements.Count} 個有效 SQL 與 {evidence.Sql.Count(s => s.IsCommented)} 個註解 SQL 區塊。";
        return Task.FromResult(result);
    }

    internal static IEnumerable<FourGlStatement> Descendants(IEnumerable<FourGlStatement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            foreach (var nested in Descendants(statement.Children)) yield return nested;
            foreach (var branch in statement.Branches)
                foreach (var nested in Descendants(branch.Statements)) yield return nested;
        }
    }
}
