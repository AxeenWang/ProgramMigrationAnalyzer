using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Infrastructure;
using ProgramMigrationAnalyzer.Parsers;
using ProgramMigrationAnalyzer.Translation;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if (!File.Exists(Path.Combine(root, "ProgramMigrationAnalyzer.sln")))
{
    throw new InvalidOperationException("Regression checks must run from the ProgramMigrationAnalyzer project.");
}

var scratch = Path.Combine(root, ".codex-tmp", "2026-09-23_phase3-regression", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);

try
{
    await RunRegressionAsync();
    Console.WriteLine("Phase 3 regression passed.");
}
finally
{
    Directory.Delete(scratch, recursive: true);
}

async Task RunRegressionAsync()
{
    var files = new SourceFileService();
    var parsers = new ISourceParser[] { new CSharpSourceParser(), new Informix4GlParser() };
    var analyzer = new MigrationAnalyzer();
    var reports = new MarkdownReportGenerator();
    var translator = new MigrationSourceTranslator();
    var writer = new OutputWriter(Path.Combine(scratch, "output"));
    var samples = await files.LoadFolderAsync(Path.Combine(root, "samples"));
    Check(samples.Count == 10, $"Expected 10 samples, got {samples.Count}.");

    foreach (var source in samples)
    {
        var parser = parsers.Single(candidate => candidate.CanParse(source));
        var result = await analyzer.AnalyzeAsync(await parser.ParseAsync(source));
        Check(result.Language == source.Language, $"Language mismatch: {source.FileName}");
        Check(result.Functions.Count > 0, $"No functions: {source.FileName}");
        Check(result.Assessment.MigrationScore is >= 0 and <= 100, $"Invalid score: {source.FileName}");
        Check(result.Tags.Count > 0, $"No analysis tags: {source.FileName}");

        if (source.FileName.Equals("CustomerQuery.4gl", StringComparison.OrdinalIgnoreCase))
        {
            Check(result.SqlStatements.Count > 0, "Scenario A has no SQL.");
            Check(result.Tags.Any(tag => tag.Name.Contains("Customer", StringComparison.OrdinalIgnoreCase)),
                "Scenario A has no customer tag.");
        }

        if (source.FileName.Equals("LegacyApiClient.cs", StringComparison.OrdinalIgnoreCase))
        {
            Check(result.MigrationIssues.Any(issue => issue.Code == "CS-WEBREQUEST"),
                "Scenario B has no WebRequest issue.");
            Check(result.Dependencies.Any(dependency =>
                dependency.Name.Contains("XML", StringComparison.OrdinalIgnoreCase) ||
                dependency.Category.Contains("XML", StringComparison.OrdinalIgnoreCase)),
                "Scenario B has no XML dependency.");
        }

        foreach (var target in new[] { MigrationTargetFramework.Net8, MigrationTargetFramework.Net10 })
        {
            var markdown = reports.Generate(result, target);
            Check(markdown.Contains("## Migration Assessment", StringComparison.Ordinal) &&
                markdown.Contains(source.FileName, StringComparison.Ordinal),
                $"Incomplete Markdown: {source.FileName} {target}");
            var reportPath = await writer.WriteAnalysisAsync(source, markdown);
            Check(File.ReadAllText(reportPath) == markdown, $"Report write mismatch: {source.FileName}");

            var translation = await translator.TranslateAsync(result, target);
            var expectedTarget = target == MigrationTargetFramework.Net8 ? ".NET 8" : ".NET 10";
            Check(translation.GeneratedCode.Contains($"Migration Target: {expectedTarget}", StringComparison.Ordinal),
                $"Translation target mismatch: {source.FileName} {target}");
            var outputPath = await writer.WriteTranslationAsync(translation);
            Check(File.ReadAllText(outputPath) == translation.GeneratedCode,
                $"Translation write mismatch: {source.FileName} {target}");
            Check(!CSharpSyntaxTree.ParseText(translation.GeneratedCode).GetDiagnostics().Any(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error),
                $"Generated syntax error: {source.FileName} {target}");

            if (source.Language == SourceLanguage.Informix4Gl)
            {
                CheckGeneratedCompiles(translation.GeneratedCode, source.FileName, target);
            }

            if (source.FileName.Equals("CustomerQuery.4gl", StringComparison.OrdinalIgnoreCase) &&
                target == MigrationTargetFramework.Net10)
            {
                Check(outputPath.EndsWith(".net10.cs", StringComparison.OrdinalIgnoreCase),
                    "Scenario C output suffix is wrong.");
            }
        }

        Console.WriteLine($"PASS {source.FileName}: {result.Functions.Count} functions, " +
            $"{result.SqlStatements.Count} SQL, score {result.Assessment.MigrationScore}.");
    }

    await CheckErrorCasesAsync(files, parsers, analyzer);
}

async Task CheckErrorCasesAsync(SourceFileService files, ISourceParser[] parsers, MigrationAnalyzer analyzer)
{
    var emptyPath = Path.Combine(scratch, "empty.4gl");
    await File.WriteAllTextAsync(emptyPath, string.Empty);
    var empty = await files.LoadFileAsync(emptyPath);
    var emptyResult = await analyzer.AnalyzeAsync(await parsers.Single(parser => parser.CanParse(empty)).ParseAsync(empty));
    Check(emptyResult.Assessment.MigrationScore is >= 0 and <= 100, "Empty source failed analysis.");

    var malformedPath = Path.Combine(scratch, "malformed.cs");
    await File.WriteAllTextAsync(malformedPath, "class Broken { void Run( {");
    var malformed = await files.LoadFileAsync(malformedPath);
    var malformedResult = await analyzer.AnalyzeAsync(await parsers.Single(parser => parser.CanParse(malformed)).ParseAsync(malformed));
    Check(malformedResult.MigrationIssues.Any(issue => issue.Code == "CS-PARSE"),
        "Malformed C# was not reported.");

    var big5Path = Path.Combine(scratch, "big5.4gl");
    await File.WriteAllBytesAsync(big5Path, Encoding.GetEncoding(950).GetBytes("-- 客戶\nMAIN\nEND MAIN"));
    var big5 = await files.LoadFileAsync(big5Path);
    Check(big5.Content.Contains("客戶", StringComparison.Ordinal), "Big5 fallback failed.");

    Check(SourceFileService.DetectLanguage("unsupported.txt") == SourceLanguage.Unknown,
        "Unsupported extension was misclassified.");
    Console.WriteLine("PASS empty, malformed C#, Big5, unsupported extension.");
}

static void CheckGeneratedCompiles(string code, string fileName, MigrationTargetFramework target)
{
    var platformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
        ?? throw new InvalidOperationException("Platform references are unavailable.");
    var references = platformAssemblies.Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path));
    var syntaxTree = CSharpSyntaxTree.ParseText(code);
    var compilation = CSharpCompilation.Create(
        $"Generated_{Path.GetFileNameWithoutExtension(fileName)}_{target}",
        [syntaxTree], references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var errors = compilation.GetDiagnostics()
        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        .Select(diagnostic => diagnostic.ToString())
        .ToList();
    Check(errors.Count == 0,
        $"Generated 4GL scaffold did not compile: {fileName} {target}\n{string.Join(Environment.NewLine, errors)}");
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
