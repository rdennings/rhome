using System.Text;
using PKHeX.Core;

namespace SpikeA.Report;

/// <summary>
/// Loads a save with PKHeX.Core and prints it in the same text layout as the Python readers in
/// prototypes/ (read_em_save.py, read_pt_save.py), so the two can be diffed line by line.
/// Works on bytes only; callers do the file I/O.
/// </summary>
public static class SaveReport
{
    public sealed record Result(bool Ok, string Text, string Game, TimeSpan LoadTime);

    public static Result Build(byte[] data, string displayName)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!SaveUtil.TryGetSaveFile(data, out var sav, displayName))
            return new Result(false, $"File: {displayName}\nnot recognized as a save file ({data.Length} bytes)\n", "?", sw.Elapsed);
        var load = sw.Elapsed;
        return new Result(true, Describe(sav, displayName), sav.Version.ToString(), load);
    }

    public static string Describe(SaveFile sav, string displayName)
    {
        var strings = GameInfo.Strings;
        var sb = new StringBuilder();
        sb.Append("File: ").Append(displayName).Append('\n');
        sb.Append($"game: {sav.Version} (gen {sav.Generation}, {sav.GetType().Name})\n\n");

        sb.Append($"Trainer: {sav.OT}  TID {sav.TID16:D5}  SID {sav.SID16:D5}\n");
        sb.Append($"Play time: {sav.PlayedHours}:{sav.PlayedMinutes:D2}:{sav.PlayedSeconds:D2}  Money: {sav.Money}\n");
        // The Python readers print the Pokédex line for Gen 3 only; Gen 5 has no Python reader, so print it there too.
        if (sav.Generation is 3 or 5)
            sb.Append($"Pokédex: seen {sav.SeenCount}  owned {sav.CaughtCount}\n");
        sb.Append('\n');

        int party = sav.PartyCount;
        sb.Append($"PARTY ({party}/6)\n");
        for (int i = 0; i < party; i++)
        {
            var line = DescribePokemon(sav.GetPartySlotAtIndex(i), strings, new string(' ', 5));
            sb.Append($"  {i + 1}. {line ?? "(empty?)"}\n");
        }
        sb.Append('\n');

        for (int box = 0; box < sav.BoxCount; box++)
        {
            string name = sav is IBoxDetailName n ? n.GetBoxName(box) : "";
            if (string.IsNullOrEmpty(name))
                name = $"Box {box + 1}";
            var lines = new List<string>();
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                var line = DescribePokemon(sav.GetBoxSlotAtIndex(box, slot), strings, new string(' ', 6));
                if (line is not null)
                    lines.Add($"  {slot + 1,2}. {line}");
            }
            sb.Append($"BOX {box + 1}: {name} ({lines.Count}/{sav.BoxSlotCount})\n");
            sb.Append(lines.Count > 0 ? string.Join("\n", lines) : "  (empty)").Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static string? DescribePokemon(PKM pk, GameStrings strings, string indent)
    {
        if (pk.Species == 0)
            return null;
        if (!pk.ChecksumValid)
            return "[corrupt data: checksum mismatch]";

        string species = strings.specieslist[pk.Species];
        if (pk.IsEgg)
            return $"Egg ({species})";

        string gender = pk.Gender switch { 0 => " ♂", 1 => " ♀", _ => "" };
        var line = new StringBuilder($"{species}{gender}  Lv {pk.CurrentLevel}");
        if (pk.IsNicknamed)
            line.Append($"  \"{pk.Nickname}\"");
        if (pk.Form != 0)
            line.Append($"  (form {pk.Form})");
        if (pk.IsShiny)
            line.Append("  ★ shiny");

        var moves = new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 }
            .Where(m => m != 0)
            .Select(m => m < strings.movelist.Length ? strings.movelist[m] : $"[move #{m}]")
            .ToList();
        return $"{line}\n{indent}{(moves.Count > 0 ? string.Join(", ", moves) : "(no moves)")}";
    }
}
