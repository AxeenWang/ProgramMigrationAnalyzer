using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Parsers;
using ProgramMigrationAnalyzer.Infrastructure;
using System.Text;
using System.Security.Cryptography;

internal static class FourGlChecks
{
    public static async Task RunAsync(string scratch)
    {
        await CriticalDataFlowChecks.RunAsync();
        await ConciseReportChecks.RunAsync();
        // A line-only parser loses the FROM/WHERE continuation and the quoted # value.
        const string text = """
            DATABASE localdb
            MAIN
              DEFINE msg CHAR(80), cnt INTEGER
              LET msg = "keep#--{literal}"
              SELECT COUNT(*)
                INTO cnt
                FROM warehouse@node:shipment s
               WHERE s.state = 'N'
              IF cnt > 0 THEN
                RUN msg
              END IF
            END MAIN
            # UPDATE ignored SET flag = 'Y'
            """;
        var result = await ParseAsync(text);
        Check(result.SqlStatements.Count == 1, "Comment SQL must not enter active SQL.");
        Check(result.SqlStatements[0].RawSql.Contains("FROM warehouse@node:shipment s", StringComparison.Ordinal),
            "4GL multiline SQL lost its FROM clause.");
        Check(result.SqlStatements[0].Tables.SequenceEqual(["warehouse@node:shipment"]),
            "Qualified Informix table identity was truncated.");
        var report = new MarkdownReportGenerator().Generate(result, MigrationTargetFramework.Net10);
        Check(result.FourGl!.Values.Any(v => v.Name == "msg" && v.Value == "keep#--{literal}"), "String comment markers were lost.");
        Check(report.Contains("UPDATE ignored SET flag = 'Y'", StringComparison.Ordinal), "Disabled SQL evidence was lost.");
        Console.WriteLine("PASS 4GL full SQL and comment evidence.");
        await CheckSqlStructureAsync();
        await CheckPathsAndReturnsAsync();
        await CheckSnapshotsAndDiagnosticsAsync();
        await CheckStatementBoundariesAsync();
        await CheckSourceBytesAsync(scratch);
        await CheckNestedSqlAndCommentsAsync();
        await CheckHostValuesAsync();
        await CheckSqlToFourGlCaseBoundaryAsync();
        await CheckQuotedKeywordsAsync();
    }

    private static async Task CheckQuotedKeywordsAsync()
    {
        var parsed = await ParseAsync("MAIN\n DEFINE state INTEGER\n IF 'A=B' = 'A=B' THEN\n RUN 'echo RETURNING payload WITHOUT WAITING'\n RUN 'tool.4ge' RETURNING state\n END IF\nEND MAIN");
        Check(parsed.FourGl!.Commands[0].Template == "echo RETURNING payload WITHOUT WAITING", "RUN qualifiers were incorrectly extracted from inside a string literal.");
        Check(parsed.FourGl.Commands[1].Template == "tool.4ge" && parsed.FourGl.Commands[1].Status.Contains("接收狀態", StringComparison.Ordinal), "RUN RETURNING status was not separated from the command expression.");
        Check(parsed.FourGl.Flow.First(f => f.Kind == "IF").Description.Contains("'A=B'", StringComparison.Ordinal), "Condition explanation rewrote an operator inside a literal.");
        Console.WriteLine("PASS quoted RUN modifier keywords and literal-preserving condition explanations.");
    }

    private static async Task CheckSqlToFourGlCaseBoundaryAsync()
    {
        var parsed = await ParseAsync("FUNCTION dispatch()\n DEFINE flag CHAR(1)\n SELECT status INTO flag FROM orders WHERE id = 1\n CASE flag\n WHEN 'N'\n LET flag = 'Y'\n END CASE\n RETURN flag\nEND FUNCTION");
        Check(!parsed.FourGl!.Diagnostics.Any(d => d.Code == "4GL-END"), "4GL CASE following SQL swallowed END FUNCTION.");
        Check(!parsed.SqlStatements.Single().RawSql.Contains("CASE flag", StringComparison.Ordinal), "4GL CASE was misclassified as a SQL expression.");
        Check(parsed.FourGl.Routines.Single().Statements.Any(n => n.Kind == "CASE"), "4GL CASE structure was lost after a SQL statement.");
        Console.WriteLine("PASS SQL expression CASE versus following 4GL CASE boundary.");
    }

    private static async Task CheckHostValuesAsync()
    {
        var result = await ParseAsync("MAIN\n DEFINE marker CHAR(1), stamp INTEGER\n UPDATE warehouse SET (flag, changed) = (marker, stamp) WHERE id = stamp\n INSERT INTO warehouse(flag, changed) VALUES(marker, stamp)\nEND MAIN");
        var sql = result.FourGl!.Sql.Where(s => !s.IsCommented).ToList();
        Check(sql.All(s => !s.Columns.Any(c => c.Name is "marker" or "stamp")), "Host values in tuple UPDATE/INSERT VALUES were reported as database columns.");
        Check(sql[0].Columns.Any(c => c.Name == "flag" && c.Use.Contains("marker", StringComparison.Ordinal)), "UPDATE target-to-host value mapping was lost.");
        Console.WriteLine("PASS UPDATE/INSERT database targets versus host values.");
    }

    private static async Task CheckNestedSqlAndCommentsAsync()
    {
        const string text = """
            MAIN
              DEFINE amount DECIMAL(10,2)
              SELECT x.amount INTO amount FROM outer_table x
               WHERE x.id IN (SELECT x.id FROM inner_table x WHERE x.kind = 'A  B') # query note
              RUN 'done.4ge'
            END MAIN
            {
              SELECT old_id
                FROM retired_table
               WHERE flag = 'N'
              UPDATE retired_table
                 SET flag = 'Y'
            }
            """;
        var result = await ParseAsync(text);
        var active = result.FourGl!.Sql.Single(s => !s.IsCommented);
        Check(active.RawSql.EndsWith("# query note", StringComparison.Ordinal) && active.RawSql.Contains("'A  B'", StringComparison.Ordinal), "Trailing SQL comment or string whitespace changed.");
        Check(active.Columns.Any(c => c.Table == "outer_table" && c.Name == "amount") && active.Columns.Any(c => c.Table == "inner_table" && c.Name == "kind"), "Nested alias scopes were conflated.");
        Check(result.FourGl.Sql.Count(s => s.IsCommented) == 2, "Multiple DML statements inside a block comment were not separately preserved.");
        Check(result.FourGl.Sql.All(s => s.RawSql == s.Span.Slice(text)), "Comment SQL no longer matches source spans.");
        var recursive = await ParseAsync("MAIN\n CALL again() RETURNING x\nEND MAIN\nFUNCTION again()\n RETURN again()\nEND FUNCTION");
        Check(recursive.FourGl!.Diagnostics.Any(d => d.Code == "4GL-CALL-LIMIT"), "Recursive symbolic calls were not bounded and diagnosed.");
        Console.WriteLine("PASS nested SQL alias scopes, trailing comments, disabled block DML and recursion bound.");
    }

    private static async Task CheckStatementBoundariesAsync()
    {
        const string text = """
            MAIN
              DEFINE row RECORD LIKE one@host:orders.*
              DEFINE id INTEGER
              INSERT INTO one@host:orders(id) VALUES (1)
              SELECT MAX(id) INTO id FROM one@host:orders
              FOREACH cur INTO row.*
                IF row.flag = 'N' THEN
                  RUN 'worker.4ge ', row.id CLIPPED
                END IF
              END FOREACH
            END MAIN
            """;
        var parsed = await ParseAsync(text);
        Check(parsed.SqlStatements.Count == 2 && !parsed.SqlStatements[0].RawSql.Contains("SELECT", StringComparison.Ordinal), "INSERT VALUES swallowed the next standalone SELECT.");
        Check(parsed.FourGl!.RecordFields.Any(f => f.Table == "one@host:orders" && f.Name == "flag"), "Record fields read after SELECT * were omitted.");
        var report = new MarkdownReportGenerator().Generate(parsed, MigrationTargetFramework.Net10);
        Check(report.Contains("one@host:orders", StringComparison.Ordinal) && report.Contains("row.flag", StringComparison.Ordinal), "Concrete record dataflow was missing from the report.");
        Console.WriteLine("PASS 4GL INSERT boundary and RECORD LIKE field evidence.");
    }

    private static async Task CheckSourceBytesAsync(string scratch)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(950).GetBytes("# 測試\r\nMAIN\r\nEND MAIN\r\n");
        var path = Path.Combine(scratch, "encoding.4gl");
        await File.WriteAllBytesAsync(path, bytes);
        var loaded = await new SourceFileService().LoadFileAsync(path);
        Check(loaded.EncodingName.Contains("CP950", StringComparison.Ordinal) && loaded.ByteSha256 == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "Source encoding/hash metadata changed.");
        await File.WriteAllBytesAsync(path, [0x81]);
        try { await new SourceFileService().LoadFileAsync(path); throw new InvalidOperationException("Undecodable bytes were silently replaced."); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS lossless CP950 metadata and malformed-byte rejection.");
    }

    private static async Task CheckSqlStructureAsync()
    {
        const string text = """
            MAIN
              DEFINE result CHAR(10), total INTEGER
              SELECT CASE WHEN a.state = 'N' THEN 'new'
                          ELSE 'old' END INTO result
                FROM alpha@host:orders a, alpha@host:detail b
               WHERE a.id = b.order_id AND b.code[1,3] = 'ABC'
              LET total = 1
              SELECT a.price * a.qty INTO total FROM alpha@host:detail a
              SELECT COUNT(*) INTO total FROM alpha@host:detail
              SELECT * FROM alpha@host:detail
              UPDATE alpha@host:orders
                 SET (state, stamp) = ('F', TODAY)
               WHERE id = total
              RUN 'report.4ge'
            END MAIN
            """;
        var result = await ParseAsync(text);
        var sql = result.FourGl!.Sql.Where(s => !s.IsCommented).ToList();
        Check(sql.Count == 5, "SQL CASE swallowed following 4GL statements.");
        Check(sql[0].Tables.SequenceEqual(["alpha@host:orders", "alpha@host:detail"]), "Comma-separated tables or aliases were lost.");
        Check(sql[0].Columns.Any(c => c.Table == "alpha@host:detail" && c.Name == "code"), "Column alias did not resolve to the joined table.");
        Check(!sql[1].HasWildcard && !sql[2].HasWildcard && sql[3].HasWildcard, "Multiplication/COUNT(*) must not become schema wildcards.");
        Check(sql[4].RawSql.Contains("SET (state, stamp) = ('F', TODAY)", StringComparison.Ordinal), "Tuple UPDATE was truncated.");
        foreach (var item in sql) Check(item.RawSql == item.Span.Slice(text), "SQL no longer matches exact source span.");
        Check(result.FourGl.Commands.Count == 1, "RUN following multiline SQL CASE was lost.");
        Console.WriteLine("PASS 4GL SQL CASE, comma joins, aliases, tuple UPDATE, wildcard and exact spans.");
    }

    private static async Task CheckPathsAndReturnsAsync()
    {
        const string text = """
            GLOBALS
              DEFINE final_command CHAR(500)
            END GLOBALS
            MAIN
              DEFINE mode CHAR(10), flag CHAR(20), query CHAR(500)
              CALL validate_mode(mode) RETURNING flag
              CASE mode
                WHEN 'A'
                  LET final_command = 'first.4ge ', flag CLIPPED
                  RUN final_command
                  LET final_command = 'second.4ge ', flag CLIPPED
                  RUN final_command
                OTHERWISE
                  LET final_command = 'fallback.4ge'
              END CASE
              CALL notify(final_command)
              LET query = 'SELECT  x  FROM ', mode CLIPPED
              PREPARE dyn FROM query
              EXECUTE dyn
              LET flag = 8 / 4 / 2
            END MAIN
            FUNCTION validate_mode(p)
              DEFINE p CHAR(10)
              IF p IS NULL THEN
                RETURN 'Invalid'
              END IF
              RETURN 'OK'
            END FUNCTION
            FUNCTION notify(command)
              DEFINE command CHAR(500)
              RUN command
              STOP
              RUN 'unreachable.4ge'
            END FUNCTION
            """;
        var result = await ParseAsync(text);
        var evidence = result.FourGl!;
        var returned = evidence.Values.Single(v => v.Name == "flag" && v.Expression.StartsWith("CALL", StringComparison.Ordinal));
        Check(returned.Value.Contains("Invalid", StringComparison.Ordinal) && returned.Value.Contains("OK", StringComparison.Ordinal), "Early return candidates were overwritten by the final RETURN.");
        Check(returned.Value.Contains("p IS NULL", StringComparison.Ordinal), "Return candidates lost their path condition.");
        var afterCase = evidence.Calls.Single(c => c.Target == "notify");
        var bindings = evidence.Values.Where(v => v.Expression == "CALL notify 參數綁定").ToList();
        Check(bindings.Any(v => v.Value.Contains("second.4ge", StringComparison.Ordinal) && v.Value.Contains("fallback.4ge", StringComparison.Ordinal) && !v.Value.Contains("first.4ge", StringComparison.Ordinal)), "CASE join lost the final command or retained an overwritten command.");
        Check(evidence.Commands.Count == 3 && !evidence.Commands.Any(c => c.Template.Contains("unreachable", StringComparison.Ordinal)), "STOP failed to end the path or CASE lost one of its RUN statements.");
        Check(evidence.Values.Any(v => v.Expression.Contains("動態 SQL EXECUTE", StringComparison.Ordinal) && v.Value.Contains("SELECT  x  FROM ", StringComparison.Ordinal)), "Dynamic SQL template whitespace or unknown binding was lost.");
        Check(!evidence.Values.Last(v => v.Name == "flag").Value.Equals("4", StringComparison.Ordinal), "Chained arithmetic was evaluated with incorrect associativity.");
        Console.WriteLine("PASS 4GL early returns, guarded values, CASE multiple RUN, final command, dynamic SQL and STOP.");
    }

    private static async Task CheckSnapshotsAndDiagnosticsAsync()
    {
        const string malformed = "MAIN\r\n  DEFINE x INTEGER\r\n  MYSTERY x\r\n  IF x > 0 THEN\r\n    RUN 'ok#--{x}'\r\n";
        var parsed = await ParseAsync(malformed);
        Check(parsed.FourGl!.Diagnostics.Any(d => d.Code == "4GL-UNKNOWN") && parsed.FourGl.Diagnostics.Any(d => d.Code == "4GL-END"), "Unknown/missing END syntax was silently accepted.");
        Check(parsed.FourGl.Snapshot.Span(8, 14).StartLine == 2, "CRLF source positions changed.");
        Check(parsed.FourGl.Commands.Single().Template == "ok#--{x}", "Quoted comment markers changed.");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try { await new Informix4GlParser().ParseAsync(parsed.Source, cancel.Token); throw new InvalidOperationException("Canceled parser completed."); }
        catch (OperationCanceledException) { }
        var malicious = await ParseAsync("MAIN\n LET x = '<script>alert(1)</script>```'\n RUN x\nEND MAIN");
        malicious.Source.Language = SourceLanguage.Informix4Gl;
        var report = new MarkdownReportGenerator().Generate(malicious, MigrationTargetFramework.Net10);
        Check(!report.Contains("| <script>", StringComparison.Ordinal) && report.Contains("&lt;script&gt;", StringComparison.Ordinal), "Source HTML escaped its evidence context.");
        Console.WriteLine("PASS 4GL malformed input, exact CRLF locations, cancellation and Markdown/HTML escaping.");
    }

    private static async Task<AnalysisResult> ParseAsync(string text) =>
        await new MigrationAnalyzer().AnalyzeAsync(await new Informix4GlParser().ParseAsync(new SourceDocument
        {
            FilePath = Path.GetFullPath("synthetic.4gl"), FileName = "synthetic.4gl", Content = text,
            Language = SourceLanguage.Informix4Gl
        }));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
