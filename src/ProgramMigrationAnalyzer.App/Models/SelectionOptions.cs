using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.App.Models;

public sealed record LanguageOption(string Name, SourceLanguage Language)
{
    public override string ToString() => Name;
}

public sealed record TargetOption(string Name, MigrationTargetFramework Framework)
{
    public override string ToString() => Name;
}
