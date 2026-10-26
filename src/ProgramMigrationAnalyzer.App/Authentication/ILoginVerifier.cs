namespace ProgramMigrationAnalyzer.App.Authentication;

public interface ILoginVerifier
{
    bool Verify(string username, string password);
}
