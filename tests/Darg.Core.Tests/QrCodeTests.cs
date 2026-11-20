using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Darg.Core;
using Xunit.Abstractions;

namespace Darg.Core.Tests;

/// <summary>AND-0002: the pairing QR is drawn by Darg's own encoder, so it is checked against a real decoder: OpenCV's,
/// from a Python that has it, named by DARG_TEST_QR_PYTHON (on Philip's PC, Forge's venv). Without one, only the shape is
/// checked, and the test says so in its output.</summary>
public sealed class QrCodeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 21)]
    [InlineData(14, 21)]
    [InlineData(15, 25)]
    [InlineData(200, 57)]
    public void Picks_the_smallest_version_and_draws_the_finders(int length, int size)
    {
        var qr = QrCode.Encode(new string('a', length));
        Assert.Equal(size, qr.Size);
        foreach (var (x, y) in new[] { (0, 0), (qr.Size - 7, 0), (0, qr.Size - 7) })
        {
            for (var i = 0; i < 7; i++) // a finder's dark outer ring and its light inner one
            {
                Assert.True(qr.Modules[y, x + i] && qr.Modules[y + 6, x + i] && qr.Modules[y + i, x] && qr.Modules[y + i, x + 6]);
                if (i is >= 1 and <= 5) Assert.False(qr.Modules[y + 1, x + i]);
            }
            Assert.True(qr.Modules[y + 3, x + 3]);
        }
        Assert.True(qr.Modules[qr.Size - 8, 8]); // the dark module
        Assert.InRange(qr.Mask, 0, 7);
        Assert.StartsWith("M", qr.PathData());
    }

    [Fact]
    public void Decodes_back_to_the_text_with_a_real_reader()
    {
        var python = Environment.GetEnvironmentVariable("DARG_TEST_QR_PYTHON");
        if (string.IsNullOrWhiteSpace(python) || !File.Exists(python))
        {
            output.WriteLine("DARG_TEST_QR_PYTHON (a Python with opencv) isn't set: the decode check didn't run.");
            return;
        }
        var dir = Directory.CreateTempSubdirectory("darg-qr-").FullName;
        var texts = new List<string>
        {
            "x", "HELLO WORLD 123", RemoteAccess.PairUri("PC-1", ["192.168.1.20", "100.101.1.2"], 47818, "ABCDEFGH", new string('f', 64)),
            new string('q', 100) + "-end", string.Concat(Enumerable.Range(0, 60).Select(i => $"{i:D3}/")), string.Concat(Enumerable.Repeat("darg remote pairing ", 50)),
        };
        var files = new List<string>();
        for (var i = 0; i < texts.Count; i++)
        {
            var f = Path.Combine(dir, $"qr{i}.bmp");
            File.WriteAllBytes(f, Bmp(QrCode.Encode(texts[i])));
            files.Add(f);
        }
        const string script = "import sys, json, cv2\nfor f in sys.argv[1:]:\n    v, _, _ = cv2.QRCodeDetector().detectAndDecode(cv2.imread(f))\n    print(json.dumps(v))\n";
        var psi = new ProcessStartInfo(python) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-I"); psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(script);
        foreach (var f in files) psi.ArgumentList.Add(f);
        using var p = Process.Start(psi)!;
        var lines = p.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(lines.Length == texts.Count, err);
        for (var i = 0; i < texts.Count; i++)
            Assert.Equal(texts[i], JsonSerializer.Deserialize<string>(lines[i]));
        output.WriteLine($"Decoded {texts.Count} codes, versions {string.Join(", ", texts.Select(t => QrCode.Encode(t).Version))}.");
        Directory.Delete(dir, true);
    }

    /// <summary>The code as a 24-bit BMP, 6 pixels a module with the 4-module quiet zone.</summary>
    static byte[] Bmp(QrCode qr, int scale = 6, int border = 4)
    {
        var px = (qr.Size + border * 2) * scale;
        var stride = (px * 3 + 3) & ~3;
        var bytes = new byte[54 + stride * px];
        void I32(int at, int v) => BitConverter.GetBytes(v).CopyTo(bytes, at);
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        I32(2, bytes.Length); I32(10, 54); I32(14, 40); I32(18, px); I32(22, px);
        bytes[26] = 1; bytes[28] = 24; I32(34, stride * px);
        for (var row = 0; row < px; row++)
            for (var col = 0; col < px; col++)
            {
                int my = (px - 1 - row) / scale - border, mx = col / scale - border; // BMP rows go bottom up
                var dark = my >= 0 && mx >= 0 && my < qr.Size && mx < qr.Size && qr.Modules[my, mx];
                var at = 54 + row * stride + col * 3;
                bytes[at] = bytes[at + 1] = bytes[at + 2] = dark ? (byte)0 : (byte)255;
            }
        return bytes;
    }
}
