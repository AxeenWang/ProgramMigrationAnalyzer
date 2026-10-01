using ProgramMigrationAnalyzer.PublishPreparation;
try
{
    if (args is not ["--public-key", var path, "--staging", var staging]) return 2;
    Console.WriteLine(PublicKeySnapshot.Prepare(path, staging)); return 0;
}
catch (Exception) { Console.Error.WriteLine("Public key preparation failed. Use a valid company P-256 PUBLIC KEY PEM."); return 1; }
