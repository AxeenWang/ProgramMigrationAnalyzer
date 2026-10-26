using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Infrastructure;
using ProgramMigrationAnalyzer.Parsers;
using ProgramMigrationAnalyzer.Translation;

namespace ProgramMigrationAnalyzer.App.Services;

internal sealed class MainWindowFactory(string? outputDirectory = null)
{
    public MainWindow Create()
    {
        var viewModel = new MainViewModel(new SourceFileService(),
            [new CSharpSourceParser(), new Informix4GlParser()], new MigrationAnalyzer(),
            new MarkdownReportGenerator(), new MigrationSourceTranslator(),
            new OutputWriter(outputDirectory), new FileDialogService());
        try { return new MainWindow(viewModel); }
        catch { viewModel.Dispose(); throw; }
    }
}
