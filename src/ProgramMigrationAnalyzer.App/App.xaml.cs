using System.IO;
using System.Windows;
using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Infrastructure;
using ProgramMigrationAnalyzer.Parsers;
using ProgramMigrationAnalyzer.Translation;

namespace ProgramMigrationAnalyzer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var outputDirectory = ResolveOutputDirectory();
        var parsers = new ISourceParser[]
        {
            new CSharpSourceParser(),
            new Informix4GlParser()
        };
        var viewModel = new MainViewModel(
            new SourceFileService(),
            parsers,
            new MigrationAnalyzer(),
            new MarkdownReportGenerator(),
            new MigrationSourceTranslator(),
            new OutputWriter(outputDirectory),
            new FileDialogService());

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }

    private static string ResolveOutputDirectory()
    {
        var candidates = new[]
        {
            new DirectoryInfo(Directory.GetCurrentDirectory()),
            new DirectoryInfo(AppContext.BaseDirectory)
        };

        foreach (var candidate in candidates)
        {
            for (var directory = candidate; directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ProgramMigrationAnalyzer.sln")))
                {
                    return Path.Combine(directory.FullName, "output");
                }
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "output");
    }
}
