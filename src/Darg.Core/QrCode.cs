using System.Text;

namespace Darg.Core;

/// <summary>A QR code for a line of text (AND-0002: the phone pairing QR), made here rather than with a package:
/// byte mode, error correction level M, the smallest version (1 to 40) that holds it, and the mask with the lowest
/// penalty (runs, 2x2 blocks and dark balance; the finder-lookalike rule is left out, which only ever costs a
/// slightly worse mask). Follows the algorithm of Project Nayuki's QR Code generator (MIT).
/// <see cref="Modules"/>[y, x] is true for a dark module; draw it with a quiet zone of 4 modules around.</summary>
public sealed class QrCode
{
    public int Version { get; }
    public int Size { get; }
    public int Mask { get; private set; }
    public bool[,] Modules { get; }
    readonly bool[,] _function;

    static readonly int[] EccPerBlock = [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28];
    static readonly int[] Blocks = [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49];
    const int FormatBitsM = 0;

    public static QrCode Encode(string text) => Encode(Encoding.UTF8.GetBytes(text));

    public static QrCode Encode(byte[] data)
    {
        for (var v = 1; v <= 40; v++)
        {
            var countBits = v <= 9 ? 8 : 16;
            var capacity = DataCodewords(v) * 8;
            if (4 + countBits + data.Length * 8 > capacity) continue;
            var bits = new List<bool>();
            void Add(int value, int n) { for (var i = n - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
            Add(0b0100, 4);
            Add(data.Length, countBits);
            foreach (var b in data) Add(b, 8);
            Add(0, Math.Min(4, capacity - bits.Count));
            Add(0, (8 - bits.Count % 8) % 8);
            for (var pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Add(pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (var i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            return new QrCode(v, codewords);
        }
        throw new ArgumentException("Too long for a QR code.", nameof(data));
    }

    QrCode(int version, byte[] data)
    {
        Version = version;
        Size = version * 4 + 17;
        Modules = new bool[Size, Size];
        _function = new bool[Size, Size];
        DrawFunctionPatterns();
        DrawCodewords(AddEccAndInterleave(data));
        var best = -1;
        var bestPenalty = int.MaxValue;
        for (var m = 0; m < 8; m++)
        {
            ApplyMask(m); DrawFormatBits(m);
            var p = Penalty();
            if (p < bestPenalty) { best = m; bestPenalty = p; }
            ApplyMask(m); // masking twice undoes it
        }
        Mask = best;
        ApplyMask(best); DrawFormatBits(best);
    }

    /// <summary>The modules as SVG path data, one unit per module, offset by the quiet zone: "M5,4h1v1h-1z...".</summary>
    public string PathData(int border = 4)
    {
        var sb = new StringBuilder();
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (Modules[y, x]) sb.Append('M').Append(x + border).Append(',').Append(y + border).Append("h1v1h-1z");
        return sb.ToString();
    }

    static int RawDataModules(int v)
    {
        var result = (16 * v + 128) * v + 64;
        if (v >= 2)
        {
            var align = v / 7 + 2;
            result -= (25 * align - 10) * align - 55;
            if (v >= 7) result -= 36;
        }
        return result;
    }

    static int DataCodewords(int v) => RawDataModules(v) / 8 - EccPerBlock[v] * Blocks[v];

    void Set(int x, int y, bool dark) { Modules[y, x] = dark; _function[y, x] = true; }

    void DrawFunctionPatterns()
    {
        for (var i = 0; i < Size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
        Finder(3, 3); Finder(Size - 4, 3); Finder(3, Size - 4);
        var pos = AlignmentPositions();
        for (var i = 0; i < pos.Length; i++)
            for (var j = 0; j < pos.Length; j++)
                if (!(i == 0 && j == 0 || i == 0 && j == pos.Length - 1 || i == pos.Length - 1 && j == 0))
                    for (var dy = -2; dy <= 2; dy++)
                        for (var dx = -2; dx <= 2; dx++)
                            Set(pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        DrawFormatBits(0); // reserves the area; the real bits are drawn once the mask is picked
        if (Version >= 7)
        {
            var rem = Version;
            for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = (Version << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                int a = Size - 11 + i % 3, b = i / 3;
                Set(a, b, bit); Set(b, a, bit);
            }
        }
    }

    void Finder(int x, int y)
    {
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                int xx = x + dx, yy = y + dy, dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) Set(xx, yy, dist != 2 && dist != 4);
            }
    }

    int[] AlignmentPositions()
    {
        if (Version == 1) return [];
        var n = Version / 7 + 2;
        var step = (Version * 8 + n * 3 + 5) / (n * 4 - 4) * 2;
        var result = new int[n];
        result[0] = 6;
        for (var i = 0; i < n - 1; i++) result[n - 1 - i] = Size - 7 - i * step;
        return result;
    }

    void DrawFormatBits(int mask)
    {
        var data = (FormatBitsM << 3) | mask;
        var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (var i = 0; i <= 5; i++) Set(8, i, Bit(i));
        Set(8, 7, Bit(6)); Set(8, 8, Bit(7)); Set(7, 8, Bit(8));
        for (var i = 9; i < 15; i++) Set(14 - i, 8, Bit(i));
        for (var i = 0; i < 8; i++) Set(Size - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) Set(8, Size - 15 + i, Bit(i));
        Set(8, Size - 8, true); // the dark module
    }

    byte[] AddEccAndInterleave(byte[] data)
    {
        int blocks = Blocks[Version], eccLen = EccPerBlock[Version], raw = RawDataModules(Version) / 8;
        int shortBlocks = blocks - raw % blocks, shortLen = raw / blocks;
        var divisor = RsDivisor(eccLen);
        var all = new List<byte[]>();
        for (int i = 0, k = 0; i < blocks; i++)
        {
            var len = shortLen - eccLen + (i < shortBlocks ? 0 : 1);
            var dat = data[k..(k + len)];
            k += len;
            var ecc = RsRemainder(dat, divisor);
            var block = new byte[shortLen + 1];
            dat.CopyTo(block, 0);
            ecc.CopyTo(block, shortLen + 1 - eccLen);
            all.Add(block);
        }
        var result = new List<byte>(raw);
        for (var i = 0; i < shortLen + 1; i++)
            for (var j = 0; j < all.Count; j++)
                if (i != shortLen - eccLen || j >= shortBlocks) result.Add(all[j][i]);
        return [.. result];
    }

    static byte[] RsDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = Mul(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Mul(root, 0x02);
        }
        return result;
    }

    static byte[] RsRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++) result[i] ^= Mul(divisor[i], factor);
        }
        return result;
    }

    static byte Mul(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    void DrawCodewords(byte[] data)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < Size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vert : vert;
                    if (!_function[y, x] && i < data.Length * 8)
                    {
                        Modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
        }
    }

    void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                if (_function[y, x]) continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) Modules[y, x] = !Modules[y, x];
            }
    }

    int Penalty()
    {
        var p = 0;
        for (var line = 0; line < 2; line++) // rows, then columns: runs of five or more alike
            for (var a = 0; a < Size; a++)
            {
                var run = 1;
                for (var b = 1; b < Size; b++)
                {
                    bool cur = line == 0 ? Modules[a, b] : Modules[b, a], prev = line == 0 ? Modules[a, b - 1] : Modules[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                    else run = 1;
                }
            }
        for (var y = 0; y < Size - 1; y++) // 2x2 blocks alike
            for (var x = 0; x < Size - 1; x++)
            {
                var c = Modules[y, x];
                if (c == Modules[y, x + 1] && c == Modules[y + 1, x] && c == Modules[y + 1, x + 1]) p += 3;
            }
        var dark = 0;
        foreach (var m in Modules) if (m) dark++;
        var total = Size * Size;
        p += Math.Max(0, (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1) * 10; // dark share off 50%, per 5%
        return p;
    }
}
