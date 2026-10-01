using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;
namespace ProgramMigrationAnalyzer.AuthenticationChecks;
internal static class PublishedBundleChecks
{
    // Inspect SDK output without launching it or touching production account storage.
    // Format references: source.dot.net Microsoft.NET.HostModel Bundle/{Bundler,Manifest,FileEntry}.cs.
    internal static int Verify(string exe, string publicPath)
    {
        using var stream = File.OpenRead(exe);
        var host = new byte[(int)Math.Min(stream.Length, 16 * 1024 * 1024)]; stream.ReadExactly(host);
        var signature = Convert.FromHexString("8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE");
        var index = host.AsSpan().IndexOf(signature); Check(index >= 8, "No SDK bundle header found.");
        var offset = BitConverter.ToInt64(host, index - 8); Check(offset > 0 && offset < stream.Length, "Invalid bundle header.");
        stream.Position = offset;
        using var binary = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var major = binary.ReadUInt32(); binary.ReadUInt32(); var count = binary.ReadInt32(); binary.ReadString();
        Check(major == 6 && count is > 0 and < 4096, "Unsupported SDK bundle format.");
        stream.Position += 40;
        var names = new List<string>(); long appOffset = 0, appSize = 0;
        for (var i = 0; i < count; i++)
        {
            var fileOffset = binary.ReadInt64(); var size = binary.ReadInt64(); var compressed = binary.ReadInt64(); binary.ReadByte();
            var name = binary.ReadString(); names.Add(name);
            if (name == "ProgramMigrationAnalyzer.App.dll") { Check(compressed == 0, "Unexpected compressed app assembly."); appOffset = fileOffset; appSize = size; }
        }
        Check(!names.Any(n => n.Contains("LicenseIssuer") || n.EndsWith(".pem") || n.EndsWith(".key") || n.EndsWith("users.json")),
            "Client bundle contains company issuer or credential files.");
        Check(appOffset > 0 && appSize is > 0 and < 32 * 1024 * 1024 && appOffset + appSize <= stream.Length, "No bounded client assembly in bundle.");
        stream.Position = appOffset; var assembly = new byte[(int)appSize]; stream.ReadExactly(assembly);
        using var image = new PEReader(new MemoryStream(assembly)); var metadata = image.GetMetadataReader(); string? actualKeyId = null;
        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            if (metadata.GetString(resource.Name) != "ProgramMigrationAnalyzer.AuthorizationPublicKey") continue;
            Check(resource.Implementation.IsNil, "Public key resource must be embedded.");
            var section = image.GetSectionData(image.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
            var reader = section.GetReader((int)resource.Offset, section.Length - (int)resource.Offset); var size = reader.ReadInt32();
            Check(size is > 0 and <= 8192, "Embedded public key size invalid.");
            actualKeyId = AuthorizationTrust.FromPublicKeyPem(new UTF8Encoding(false, true).GetString(reader.ReadBytes(size))).KeyId;
        }
        var expectedKeyId = AuthorizationTrust.FromPublicKeyPem(File.ReadAllText(publicPath)).KeyId;
        Check(actualKeyId == expectedKeyId && actualKeyId is not null, "Published embedded key differs from selected public key.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ProgramMigrationAnalyzer.sln"))) root = root.Parent;
        var compiled = File.ReadAllBytes(Path.Combine(root!.FullName, "src", "ProgramMigrationAnalyzer.App", "bin", "Release", "net10.0-windows", "win-x64", "ProgramMigrationAnalyzer.App.dll"));
        Check(SHA256.HashData(compiled).SequenceEqual(SHA256.HashData(assembly)), "Published assembly differs from the formal-root build.");
        Console.WriteLine("Published bundle verified. Public key: " + actualKeyId); return 0;
    }
}
