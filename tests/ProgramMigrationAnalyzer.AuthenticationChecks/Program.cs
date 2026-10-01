namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var suites = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
        {
            ["contracts"] = ContractChecks.Run,
            ["crypto"] = CryptoChecks.Run,
            ["store"] = StoreChecks.Run
        };
        var selected = args.Length == 0 ? "all"
            : args.Length == 2 && args[0] == "--suite" ? args[1] : string.Empty;

        if (selected != "all" && !suites.ContainsKey(selected))
        {
            Console.Error.WriteLine("Usage: AuthenticationChecks --suite contracts|crypto|store|all. Unimplemented suites cannot pass.");
            return 2;
        }

        try
        {
            foreach (var suite in suites.Where(item => selected == "all" || item.Key.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            {
                suite.Value();
                Console.WriteLine($"Authentication {suite.Key} checks passed.");
            }

            if (selected == "all")
            {
                Console.WriteLine($"Authentication all checks passed ({string.Join(", ", suites.Keys)}).");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
