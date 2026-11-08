using System.Globalization;
using Rhome.Core;

namespace Rhome.FileSystem;

/// <summary>
/// Backups in a folder (PLAN.md §2 "Safety"): <c>saves/&lt;save ID&gt;/</c> holds the last <see cref="Keep"/>
/// copies of each save from before rhome wrote it, and <c>bank/</c> the last copies of the bank from before each
/// change. Files are named by time, oldest first when sorted, so the newest is easy to find by hand.
/// </summary>
public sealed class BackupFolder(string root, int keep = 10) : IBackups
{
    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>How many backups to keep per save, and of the bank. Older ones are deleted.</summary>
    public int Keep { get; } = keep >= 1 ? keep : throw new ArgumentOutOfRangeException(nameof(keep), "Keep at least one backup.");

    public void KeepSave(Guid saveId, ReadOnlySpan<byte> contents, DateTimeOffset when) =>
        Write(Path.Combine(Root, "saves", saveId.ToString()), ".sav", contents, when);

    public void KeepBank(ReadOnlySpan<byte> contents, DateTimeOffset when) =>
        Write(Path.Combine(Root, "bank"), ".json", contents, when);

    /// <summary>The backups of a save, oldest first.</summary>
    public IReadOnlyList<string> SaveBackups(Guid saveId) => List(Path.Combine(Root, "saves", saveId.ToString()));

    /// <summary>The bank backups, oldest first.</summary>
    public IReadOnlyList<string> BankBackups() => List(Path.Combine(Root, "bank"));

    void Write(string folder, string extension, ReadOnlySpan<byte> contents, DateTimeOffset when)
    {
        Directory.CreateDirectory(folder);
        var stamp = when.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        // A counter keeps two backups from the same millisecond apart, and still sorts in order.
        string path;
        var n = 1;
        do
            path = Path.Combine(folder, $"{stamp}-{n++:00}{extension}");
        while (File.Exists(path));

        new FileOnDisk(path).Replace(contents);

        var all = List(folder);
        foreach (var old in all.Take(Math.Max(0, all.Count - Keep)))
            File.Delete(old);
    }

    static IReadOnlyList<string> List(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder).Where(f => !Path.GetFileName(f).StartsWith('.')).Order(StringComparer.Ordinal).ToList()
            : [];
}
