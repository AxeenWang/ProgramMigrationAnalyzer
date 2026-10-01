using System.IO;
using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core.Authentication;
using ProgramMigrationAnalyzer.Infrastructure;
using ProgramMigrationAnalyzer.Parsers;
using ProgramMigrationAnalyzer.Translation;

namespace ProgramMigrationAnalyzer.App.Services;

internal sealed class MainWindowFactory
{
    public MainWindow Create(IUserSession session)
    {
        if (!session.IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        var viewModel = new MainViewModel(new SourceFileService(),
            [new CSharpSourceParser(), new Informix4GlParser()], new MigrationAnalyzer(),
            new MarkdownReportGenerator(), new MigrationSourceTranslator(),
            new OutputWriter(ResolveOutputDirectory()), new FileDialogService());
        return new MainWindow(viewModel);
    }
    private static string ResolveOutputDirectory()
    {
        var candidates = new[] { new DirectoryInfo(Directory.GetCurrentDirectory()), new DirectoryInfo(AppContext.BaseDirectory) };
        foreach (var candidate in candidates)
            for (var directory = candidate; directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "ProgramMigrationAnalyzer.sln")))
                    return Path.Combine(directory.FullName, "output");
        return Path.Combine(Directory.GetCurrentDirectory(), "output");
    }
}
