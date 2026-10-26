using System.Net;
using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Parsers;

internal static class CriticalDataFlowChecks
{
    public static async Task RunAsync()
    {
        const string source = """
            # workerA.4ge uses flag(flagB, stampB)
            MAIN
              DEFINE basis DATETIME YEAR TO SECOND, stamp CHAR(19), dayValue INTEGER, clockValue INTEGER
              LET basis = CURRENT
              LET basis = basis - INTERVAL (7) MINUTE TO MINUTE
              LET stamp = basis
              LET dayValue = stamp[1,4], stamp[6,7], stamp[9,10]
              LET clockValue = stamp[12,13], stamp[15,16], stamp[18,19]
              CALL dispatch(dayValue, clockValue)
            END MAIN
            FUNCTION dispatch(dayKey, timeKey)
              DEFINE dayKey INTEGER, timeKey INTEGER, countRows INTEGER, branchCode CHAR(2), jobCommand CHAR(500)
              DEFINE row RECORD LIKE queue.*
              SELECT COUNT(*) INTO countRows FROM queue
               WHERE createdDay < dayKey AND flagA = 'R'
              DECLARE jobs CURSOR FOR SELECT * FROM queue WHERE flagA = 'P'
              FOREACH jobs INTO row.*
                LET branchCode = row.ticket[1,2]
                LET jobCommand = 'NONE'
                CASE row.customer
                  WHEN 'M1'
                    IF (row.flagA == 'P') THEN
                      LET jobCommand = '/reports/workerA.4ge ', row.ticket CLIPPED
                      RUN jobCommand
                    END IF
                  WHEN 'M2'
                    UPDATE queue SET (flagA, flagB) = ('S', 'S') WHERE stage = 'skip'
                  WHEN 'M3'
                    LET jobCommand = "echo '' | mailx -s 'Please run the manual job' user@example.test"
                    RUN jobCommand
                  WHEN 'M4'
                    LET jobCommand = '/reports/already.4ge'
                    IF branchCode = 'AA' THEN
                      UPDATE queue SET (flagA, flagB) = ('S', 'S') WHERE stage = 'later'
                    END IF
                END CASE
                IF branchCode = 'BB' THEN DISPLAY branchCode END IF
                IF (jobCommand <> 'NONE') THEN
                  CALL checktime(jobCommand)
                END IF
              END FOREACH
            END FUNCTION
            """;
        var analysis = await new MigrationAnalyzer().AnalyzeAsync(await new Informix4GlParser().ParseAsync(new SourceDocument
        {
            FileName = "flows.4gl", FilePath = Path.GetFullPath("flows.4gl"), Content = source, Language = SourceLanguage.Informix4Gl
        }));
        var markdown = new MarkdownReportGenerator().Generate(analysis, MigrationTargetFramework.Net10);
        var plain = WebUtility.HtmlDecode(markdown).Replace("\\[", "[").Replace("\\]", "]");
        Check(plain.Contains("INTERVAL (7) MINUTE TO MINUTE", StringComparison.Ordinal), "Critical temporal adjustment was hidden by preparation summarization.");
        Check(plain.Contains("YYYYMMDD", StringComparison.Ordinal) && plain.Contains("HHMMSS", StringComparison.Ordinal), "Date/time source transformations were omitted.");
        Check(plain.Contains("stamp ← basis", StringComparison.Ordinal) && plain.Contains("basis ← CURRENT", StringComparison.Ordinal), "Clock-to-slice alias chain was omitted.");
        Check(plain.Contains("branchCode ← row.ticket[1,2]", StringComparison.Ordinal), "Prefix-derived branch input was omitted.");
        Check(plain.Contains("jobCommand ← 'NONE'", StringComparison.Ordinal) && plain.Contains("每筆", StringComparison.Ordinal), "Per-row command sentinel reset was omitted.");
        Check(plain.Split('\n').Any(l => l.Contains("SQL-03 路徑", StringComparison.Ordinal) && l.Contains("不呼叫 checktime", StringComparison.Ordinal)), "State-only branch did not explain why the guarded call is skipped.");
        Check(!plain.Split('\n').Any(l => l.Contains("SQL-04 路徑", StringComparison.Ordinal) && l.Contains("不呼叫 checktime", StringComparison.Ordinal)), "A previously assigned command was incorrectly treated as the sentinel.");
        Check(plain.Split('\n').Any(l => l.Contains("SQL-02", StringComparison.Ordinal) && l.Contains("未使用這組時間篩選", StringComparison.Ordinal)), "Time-filtered query was not distinguished from the dispatch cursor.");
        Check(plain.Split('\n').Any(l => l.Contains("flagA", StringComparison.Ordinal) && l.Contains("'R'", StringComparison.Ordinal) && l.Contains("未寫入", StringComparison.Ordinal)), "Compared state was not distinguished from static UPDATE values.");
        Check(plain.Split('\n').Any(l => l.Contains("M3", StringComparison.Ordinal) && l.Contains("只有郵件／提醒", StringComparison.Ordinal)), "Notification-only route was not explained.");
        Check(plain.Split('\n').Any(l => l.Contains("workerA.4ge", StringComparison.Ordinal) && l.Contains("flagB", StringComparison.Ordinal) && l.Contains("flagA", StringComparison.Ordinal) && l.Contains("不一致", StringComparison.Ordinal)), "Comment/active flag contradiction was not reported.");
        Check(markdown.Length < 23000, "Critical insights re-expanded the report excessively.");
        foreach (var sql in analysis.SqlStatements)
            Check(markdown.Split(sql.RawSql, StringSplitOptions.None).Length - 1 == 1, "Full SQL was lost or duplicated while adding insights.");
        Check(analysis.SqlStatements.Count == 4, "Same-line cursor SELECT was lost.");

        var uncertain = source.Replace("IF (jobCommand <> 'NONE') THEN", "CALL refreshCommand()\n                IF (jobCommand <> 'NONE') THEN", StringComparison.Ordinal)
            .Replace("'Please run the manual job'", "$(other-job)", StringComparison.Ordinal);
        var uncertainAnalysis = await new MigrationAnalyzer().AnalyzeAsync(await new Informix4GlParser().ParseAsync(new SourceDocument
        {
            FileName = "uncertain.4gl", FilePath = Path.GetFullPath("uncertain.4gl"), Content = uncertain, Language = SourceLanguage.Informix4Gl
        }));
        var uncertainReport = WebUtility.HtmlDecode(new MarkdownReportGenerator().Generate(uncertainAnalysis, MigrationTargetFramework.Net10));
        Check(!uncertainReport.Contains("因此不呼叫", StringComparison.Ordinal), "Intervening external side effects were ignored when proving the sentinel.");
        Check(!uncertainReport.Contains("只有郵件／提醒", StringComparison.Ordinal), "Possible shell substitution was incorrectly classified as a pure notification.");
        Console.WriteLine("PASS critical time/prefix/sentinel flows, cursor scope, state writes, notifications and comment contradictions.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
