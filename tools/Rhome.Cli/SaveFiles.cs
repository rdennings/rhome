using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Cli;

/// <summary>
/// Desktop file access for the CLI: the core works on bytes, and this is where they come from. Writing goes
/// through a changeset and <c>Rhome.FileSystem.SaveOnDisk</c>, which write in place after a backup.
/// </summary>
static class SaveFiles
{
    /// <summary>Reads and recognizes a save, or says in <paramref name="problem"/> why it can't.</summary>
    public static DetectedSave? Read(FileInfo file, out string problem)
    {
        if (!file.Exists)
        {
            problem = "no such file";
            return null;
        }

        // Check the size before reading, so pointing at a ROM or a disc image doesn't load gigabytes.
        if (file.Length > int.MaxValue || !SaveUtil.IsSizeValid((int)file.Length))
        {
            problem = "not a Pokémon save (wrong size for one)";
            return null;
        }

        var detected = SaveDetector.Detect(File.ReadAllBytes(file.FullName));
        problem = detected is null ? "not a Pokémon save rhome recognizes" : "";
        return detected;
    }
}
