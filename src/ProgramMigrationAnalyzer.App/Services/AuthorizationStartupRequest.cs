namespace ProgramMigrationAnalyzer.App.Services;

internal enum AuthorizationStartupMode { Normal, ImportAuthorization }
internal sealed record AuthorizationStartupRequest(AuthorizationStartupMode Mode, string? AbsolutePath)
{
    internal static AuthorizationStartupRequest Parse(string[] args) => args switch
    {
        [] => new(AuthorizationStartupMode.Normal, null),
        ["--import-authorization", var path] when !string.IsNullOrWhiteSpace(path)
            && System.IO.Path.IsPathFullyQualified(path) => new(AuthorizationStartupMode.ImportAuthorization, System.IO.Path.GetFullPath(path)),
        _ => throw new ArgumentException("Unsupported startup arguments.")
    };
}
