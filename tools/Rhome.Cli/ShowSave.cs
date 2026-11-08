using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Cli;

/// <summary><c>rhome save show &lt;file&gt;</c>: what a save holds. Read only.</summary>
static class ShowSave
{
    public static int Run(FileInfo file, TextWriter output, TextWriter error)
    {
        var detected = SaveFiles.Read(file, out var problem);
        if (detected is null)
        {
            error.WriteLine($"{file.Name}: {problem}");
            return Cli.Failed;
        }

        return Print(file.Name, detected, output, error);
    }

    /// <summary>Prints a recognized save. Separate from reading it so tests can hand it any save.</summary>
    internal static int Print(string name, DetectedSave detected, TextWriter output, TextWriter error)
    {
        var save = detected.Save;
        if (!detected.IsSupported)
        {
            error.WriteLine($"{name}: a Gen {detected.Generation} save ({Names.Game(detected.Game)}). " +
                            "rhome only supports the GBA and DS games so far.");
            return Cli.Failed;
        }

        output.WriteLine(name);
        output.WriteLine($"{Names.Game(detected.Game)} · Gen {detected.Generation} · " +
                         $"trainer {detected.Trainer} {Names.Gender(save.Gender)} (ID {save.DisplayTID}) · " +
                         $"{save.PlayedHours}:{save.PlayedMinutes:00} played");
        if (!save.ChecksumsValid)
            output.WriteLine("Warning: the save's checksums don't match. The game may treat it as corrupt.");

        var party = SaveContents.Party(save);
        output.WriteLine();
        output.WriteLine(party.Count == 0 ? "Party: empty" : $"Party ({party.Count})");
        foreach (var slot in party)
            output.WriteLine(Line(save, slot));

        var boxes = SaveContents.Boxes(save);
        foreach (var box in boxes.GroupBy(s => s.Where.Box!.Value))
        {
            output.WriteLine();
            output.WriteLine($"Box {box.Key} · {SaveContents.BoxName(save, box.Key)} ({box.Count()})");
            foreach (var slot in box)
                output.WriteLine(Line(save, slot));
        }

        var empty = save.BoxCount - boxes.Select(s => s.Where.Box).Distinct().Count();
        output.WriteLine();
        output.WriteLine(empty == save.BoxCount ? $"All {save.BoxCount} boxes are empty." : $"{empty} of {save.BoxCount} boxes are empty.");
        return Cli.Ok;
    }

    /// <summary>One Pokémon: slot, species, level, gender, shiny, nickname, held item, and its OT when it isn't this trainer.</summary>
    static string Line(SaveFile save, SlotContents slot)
    {
        var pk = slot.Pokemon;
        var parts = new List<string>();

        if (pk.IsEgg)
        {
            parts.Add($"Egg ({Names.Species(pk)})");
        }
        else
        {
            parts.Add($"{Names.Species(pk),-14} Lv {pk.CurrentLevel,3} {Names.Gender(pk.Gender),1}{(pk.IsShiny ? " ★" : "")}");
            if (pk.IsNicknamed)
                parts.Add($"\"{pk.Nickname}\"");
        }

        if (pk.HeldItem != 0)
            parts.Add($"holds {Names.Item(pk.HeldItem)}");
        if (!SaveContents.IsFromThisTrainer(save, pk))
            parts.Add($"OT {pk.OriginalTrainerName} (ID {pk.DisplayTID})");

        return $"  {slot.Where.Slot,2}  {string.Join("  ", parts).TrimEnd()}";
    }
}
