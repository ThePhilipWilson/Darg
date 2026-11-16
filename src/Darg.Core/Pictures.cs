using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>Pictures on notes and cards. A note's picture is a file in &lt;notes folder&gt;/images named by its
/// <c>image:</c> property; a card's are markdown images in its description (<c>![](images/x.png)</c>), the files in
/// &lt;board folder&gt;/images. Adding one copies it in, so the wall and the board own what they show.</summary>
public static class Pictures
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];
    public const string Folder = "images";

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Copy a file into &lt;folder&gt;/images under a name that isn't taken; returns "images/name".</summary>
    public static string Add(string folder, string source)
    {
        if (!File.Exists(source)) throw new InvalidOperationException($"No file at {source}.");
        if (!IsImage(source)) throw new InvalidOperationException("That isn't a picture (png, jpg, gif, bmp or webp).");
        var dir = Path.Combine(folder, Folder);
        Directory.CreateDirectory(dir);
        var stem = Regex.Replace(Path.GetFileNameWithoutExtension(source), @"[^\w\-]+", "-").Trim('-');
        if (stem.Length == 0) stem = "picture";
        var ext = Path.GetExtension(source).ToLowerInvariant();
        var name = stem + ext;
        for (var n = 2; File.Exists(Path.Combine(dir, name)); n++) name = $"{stem}-{n}{ext}";
        File.Copy(source, Path.Combine(dir, name));
        return $"{Folder}/{name}";
    }

    /// <summary>Where a picture is on disk: a relative one is under <paramref name="folder"/> (for a note, the
    /// notes folder, never the archive's), a full path is itself. Null for nothing or a web address.</summary>
    public static string? Resolve(string folder, string image)
    {
        image = image.Trim();
        if (image.Length == 0 || image.Contains("://")) return null;
        return Path.GetFullPath(Path.IsPathRooted(image) ? image : Path.Combine(folder, image));
    }

    static readonly Regex Md = new(@"!\[[^\]]*\]\(\s*<?([^)>\s]+)>?[^)]*\)", RegexOptions.Compiled);

    /// <summary>The images a card description shows, in order, as files that exist (under the board folder).</summary>
    public static List<string> InDescription(string boardFolder, string description) =>
        Md.Matches(description ?? "").Select(m => Resolve(boardFolder, Uri.UnescapeDataString(m.Groups[1].Value))).OfType<string>()
            .Where(p => IsImage(p) && File.Exists(p)).ToList();
}
