using Rhome.Core;

namespace Rhome.FileSystem;

/// <summary>
/// The app's own folder (PLAN.md §2 "Safety"): the bank, and the backups of saves and of the bank. On Android it's
/// the app's private storage, which needs no permission and is never an emulator's file.
/// </summary>
/// <remarks>
/// <code>
/// &lt;root&gt;/bank.json               the bank
/// &lt;root&gt;/setup.json              the save folders set up in first-run setup, and whether it's done
/// &lt;root&gt;/backups/saves/&lt;id&gt;/     each save as it was before rhome wrote it
/// &lt;root&gt;/backups/bank/            the bank as it was before each change
/// </code>
/// </remarks>
public sealed class AppStorage(string root, int keepBackups = 10)
{
    public string Root { get; } = Path.GetFullPath(root);

    public FileOnDisk BankFile => new(Path.Combine(Root, "bank.json"));

    public JsonBankStore Bank => new(BankFile);

    public FileOnDisk SetupFile => new(Path.Combine(Root, "setup.json"));

    public BackupFolder Backups => new(Path.Combine(Root, "backups"), keepBackups);
}
