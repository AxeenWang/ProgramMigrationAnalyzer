using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProgramMigrationAnalyzer.WpfChecks;

internal static class ApplicationIconChecks
{
    internal static void Run(string root, string scratch)
    {
        var iconPath = Path.Combine(root, "src", "ProgramMigrationAnalyzer.App", "Assets", "ProgramMigrationAnalyzer.ico");
        var decoder = new IconBitmapDecoder(new Uri(iconPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var sizes = decoder.Frames.Select(frame => frame.PixelWidth).Order().ToArray();
        Check(sizes.SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }), "The application icon must contain all required Windows sizes.");
        var target = new DirectoryInfo(AppContext.BaseDirectory);
        var executable = Path.Combine(root, "src", "ProgramMigrationAnalyzer.App", "bin", target.Parent!.Name, target.Name, "ProgramMigrationAnalyzer.App.exe");
        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        try
        {
            Check(ExtractIconEx(executable, 0, out large, out small, 1) > 0 && large != IntPtr.Zero,
                "The built executable must expose an application icon to Windows.");
            var native = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var expected = decoder.Frames.Single(frame => frame.PixelWidth == native.PixelWidth && frame.PixelHeight == native.PixelHeight);
            var actualPixels = Pixels(native);
            var expectedPixels = Pixels(expected);
            var different = 0;
            for (var offset = 0; offset < actualPixels.Length; offset += 4)
            {
                if (Enumerable.Range(0, 4).Any(channel => Math.Abs(actualPixels[offset + channel] - expectedPixels[offset + channel]) > 3)) different++;
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(native));
            using (var output = File.Create(Path.Combine(scratch, "executable-icon.png"))) encoder.Save(output);
            Check(different <= native.PixelWidth * native.PixelHeight * 0.03,
                $"The executable still exposes a default or stale icon instead of the custom mark ({different} different pixels).");
        }
        finally
        {
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
        Console.WriteLine("PASS application icon: nine ICO sizes, native executable resource matches original custom artwork.");
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var normalized = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var bytes = new byte[normalized.PixelWidth * normalized.PixelHeight * 4];
        normalized.CopyPixels(bytes, normalized.PixelWidth * 4, 0);
        return bytes;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string path, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
