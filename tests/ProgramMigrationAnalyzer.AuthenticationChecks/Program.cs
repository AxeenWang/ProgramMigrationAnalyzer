namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Test-host routing only. App receives the actual normal/admin/invalid command line.
        if (Environment.GetEnvironmentVariable("PMA_AUTH_CHECK_SCENARIO") is { } scenario)
            return StartupChecks.RunChild(scenario);
        if (args is ["--admin-window-check"])
            return AdministrationChecks.RunWindowChild();
        if (args is ["--login-window-check"])
            return LoginChecks.RunWindowChild();
        var suites = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
        {
            ["contracts"] = ContractChecks.Run,
            ["crypto"] = CryptoChecks.Run,
            ["store"] = StoreChecks.Run,
            ["admin"] = AdministrationChecks.Run,
            ["login"] = LoginChecks.Run,
            ["startup"] = StartupChecks.Run
        };
        var selected = args.Length == 0 ? "all"
            : args.Length == 2 && args[0] == "--suite" ? args[1] : string.Empty;

        if (selected != "all" && !suites.ContainsKey(selected))
        {
            Console.Error.WriteLine("Usage: AuthenticationChecks --suite contracts|crypto|store|admin|login|startup|all. Unimplemented suites cannot pass.");
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
