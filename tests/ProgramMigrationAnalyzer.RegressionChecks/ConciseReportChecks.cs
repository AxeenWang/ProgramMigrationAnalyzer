using System.Text;
using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Parsers;

internal static class ConciseReportChecks
{
    public static async Task RunAsync()
    {
        var source = new StringBuilder("# Batch delivery coordinator\nDATABASE warehouse\nMAIN\n DEFINE row RECORD LIKE dispatch_queue.*\n DEFINE count_rows INTEGER, command CHAR(500), unused_flag CHAR(1)\n");
        for (var i = 0; i < 140; i++) source.AppendLine($" LET count_rows = {i}\n # Initialization note {i}");
        source.AppendLine("DECLARE jobs CURSOR FOR SELECT * FROM dispatch_queue WHERE state = 'N'");
        source.AppendLine("FOREACH jobs INTO row.*\n SELECT COUNT(*) INTO count_rows FROM detail WHERE id = row.id AND location[1,4] = 'GOOD'\n IF count_rows <= 0 THEN\n UPDATE dispatch_queue SET state = 'F' WHERE id = row.id\n CONTINUE FOREACH\n END IF\n CASE row.owner");
        for (var i = 0; i < 22; i++) source.AppendLine($" WHEN 'C{i}'\n LET command = 'worker{i}.4ge ', row.id CLIPPED\n RUN command");
        source.AppendLine("END CASE\n CALL notify(command)\n LET unused_flag = probe(row.id)\n END FOREACH\nEND MAIN\nFUNCTION notify(value)\n DEFINE value CHAR(500)\n DISPLAY value\nEND FUNCTION\nFUNCTION probe(value)\n DEFINE value CHAR(20)\n RETURN 'Y'\nEND FUNCTION");
        var analysis = await new MigrationAnalyzer().AnalyzeAsync(await new Informix4GlParser().ParseAsync(new SourceDocument
        {
            FilePath = Path.GetFullPath("compact.4gl"), FileName = "compact.4gl", Content = source.ToString(), Language = SourceLanguage.Informix4Gl
        }));
        var report = new MarkdownReportGenerator().Generate(analysis, MigrationTargetFramework.Net10);
        Check(report.Length < 25000, $"Report repeats low-level assignments and comments instead of summarizing: {report.Length} characters.");
        Check(report.Contains("處理階段", StringComparison.Ordinal) && report.Contains("派送", StringComparison.Ordinal), "Report lacks concrete purpose and stages.");
        Check(report.Contains("列數", StringComparison.Ordinal) && report.Contains("略過", StringComparison.Ordinal), "COUNT-to-update-to-CONTINUE business rule was not explained.");
        Check(report.Contains("未見後續有效讀取", StringComparison.Ordinal), "Unused call result was not distinguished from an active rule.");
        foreach (var sql in analysis.SqlStatements)
            Check(report.Split(sql.RawSql, StringSplitOptions.None).Length - 1 == 1, "Full active SQL must appear exactly once.");
        var binding = analysis.FourGl!.Values.Single(v => v.Expression == "CALL notify 參數綁定");
        Check(binding.Value.Contains("worker21.4ge", StringComparison.Ordinal) && !binding.Value.Contains("超過有界", StringComparison.Ordinal), "Last-command candidates were lost with more than sixteen routes.");
        Console.WriteLine("PASS concise report, complete SQL once, concrete stages/rules and bounded command routes.");
        await CheckSemanticSummaryAsync();
    }

    private static async Task CheckSemanticSummaryAsync()
    {
        const string text = """
            MAIN
              DEFINE choice INTEGER, code CHAR(10), label CHAR(30)
              DEFINE clock_text CHAR(19), numeric_time INTEGER, threshold INTEGER
              LET numeric_time = clock_text[12,13], clock_text[15,16], clock_text[18,19]
              IF numeric_time >= 10000 THEN
                LET threshold = numeric_time - 10000
              ELSE
                LET threshold = 0
              END IF
              IF choice = 1 THEN
                CALL lefttask()
              ELSE
                CALL righttask()
              END IF
              IF 'A  B' = 'A B' THEN
                DISPLAY 'literal comparison'
              END IF
              DECLARE names CURSOR FOR
                SELECT a.owner, COUNT(DISTINCT a.id) FROM jobs a
                 WHERE a.owner = 'A' OR a.owner = 'B' GROUP BY a.owner
              FOREACH names INTO code, choice
                CASE code
                  WHEN 'A' LET label = 'Alpha'
                  WHEN 'C' LET label = 'Gamma'
                  OTHERWISE LET label = 'OTHER'
                END CASE
              END FOREACH
            END MAIN
            """;
        var result = await new MigrationAnalyzer().AnalyzeAsync(await new Informix4GlParser().ParseAsync(new SourceDocument
        {
            FilePath = Path.GetFullPath("semantic.4gl"), FileName = "semantic.4gl", Content = text, Language = SourceLanguage.Informix4Gl
        }));
        var report = new MarkdownReportGenerator().Generate(result, MigrationTargetFramework.Net10);
        Check(report.Contains("成立時：呼叫 lefttask", StringComparison.Ordinal) && report.Contains("否則：呼叫 righttask", StringComparison.Ordinal), "Condensed phases merged mutually exclusive branches into a sequence.");
        Check(report.Contains("沒有跨日回推", StringComparison.Ordinal), "HHMMSS arithmetic boundary was not explained.");
        Check(System.Net.WebUtility.HtmlDecode(report).Contains("'A  B'", StringComparison.Ordinal), "Report shortening changed whitespace inside a comparison literal.");
        Check(report.Contains("查詢篩選包含 B", StringComparison.Ordinal) && report.Contains("保留 C 的 CASE 對照", StringComparison.Ordinal), "Qualified query projection was not linked to the same-file CASE mapping.");
        Console.WriteLine("PASS branch-preserving stages, temporal boundary and query/CASE mapping comparison.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
