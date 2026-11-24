using System.Globalization;
using PKHeX.Core;
using Rhome.Core;

namespace Rhome.App;

/// <summary>A labelled fact, e.g. ("Nature", "Adamant (+Attack, −Sp. Atk)").</summary>
public sealed record Fact(string Label, string Value);

/// <summary>One stat at the Pokémon's level, with its IV and EV.</summary>
/// <param name="Nature">+1 when the nature raises it, −1 when it lowers it, 0 otherwise.</param>
public sealed record StatLine(string Name, int Value, int IV, int EV, int Nature);

/// <summary>A move it knows, with its PP now and at most (after PP Ups).</summary>
public sealed record MoveLine(string Name, int PP, int MaxPP);

/// <summary>One generation's data the bank holds for it: the game it was last in there, its level then, and when.</summary>
/// <param name="IsLatest">The newest data: the game it's in now, or was in last.</param>
public sealed record Visit(int Generation, string Game, int Level, DateTimeOffset LastSeen, bool IsLatest);

/// <summary>What its data from another generation holds that the game it's in can't: kept in the bank.</summary>
public sealed record KeptData(int Generation, string Game, IReadOnlyList<string> Lines);

/// <summary>
/// What the Pokémon detail view shows (issue #47, PLAN.md §3 M2): stats, moves, ability, nature, held item, ribbons,
/// where it came from, every generation the bank holds its data for ("visited games"), and what that data holds that
/// the game it's in can't. Works for a bank Pokémon and for a Pokémon in a game's slot.
/// </summary>
public sealed class PokemonDetails
{
    // The order the Gen 3–5 summary screens list stats in, as indexes into PKHeX's order (HP, Atk, Def, Spe, SpA, SpD).
    static readonly int[] ShownOrder = [0, 1, 2, 4, 5, 3];
    static readonly string[] StatNames = ["HP", "Attack", "Defense", "Speed", "Sp. Atk", "Sp. Def"];
    static readonly string[] ContestRanks = ["", "Normal Rank", "Super Rank", "Hyper Rank", "Master Rank"];

    /// <summary>Its nickname, or its species when it has none; "Egg" for an egg.</summary>
    public required string Title { get; init; }

    /// <summary>Species (when nicknamed), level, gender and shininess, e.g. "Kirlia · Lv20 · ♀ · ★ Shiny".</summary>
    public required string Subtitle { get; init; }

    /// <summary>Where it is: a game's slot, or a bank slot (and where it is while it's out).</summary>
    public required string Where { get; init; }

    /// <summary>A caveat about the data shown, or null.</summary>
    public string? Note { get; init; }

    public required IReadOnlyList<Fact> Facts { get; init; }

    public required IReadOnlyList<StatLine> Stats { get; init; }

    public required IReadOnlyList<MoveLine> Moves { get; init; }

    /// <summary>Every ribbon it has, by name.</summary>
    public required IReadOnlyList<string> Ribbons { get; init; }

    /// <summary>
    /// Its trainer, origin game, where and how it was met, and its ball, as its origin generation recorded them when the
    /// bank has that data; then how the game it's in records it, when that's different (a Gen 3 game can't name a Gen 4
    /// origin, and a transfer replaces where it was met).
    /// </summary>
    public required IReadOnlyList<Fact> Origin { get; init; }

    /// <summary>One line per generation the bank holds its data for, oldest generation first. Empty when it isn't in the bank.</summary>
    public required IReadOnlyList<Visit> Visits { get; init; }

    /// <summary>What its other generations' data holds that the game it's in can't. Empty when there's nothing.</summary>
    public required IReadOnlyList<KeptData> Kept { get; init; }

    /// <summary>The game whose format it's in, by name: what <see cref="Kept"/> is measured against.</summary>
    public required string InGame { get; init; }

    /// <summary>A bank Pokémon, from its newest data. One that's out in a game shows as it was when it left.</summary>
    /// <param name="saveName">What to call a save the bank knows, by ID.</param>
    public static PokemonDetails ForBank(Bank bank, BankPokemon record, Func<Guid, string> saveName)
    {
        var newest = record.Newest;
        var place = bank.Boxes.Describe(record.Slot);
        return record.CheckedOut is { } away
            ? Build(newest.ToPkm(), Transfers.GameOf(bank, newest), bank, record, saveName,
                where: $"Out in {saveName(away.SaveId)}, {away.Slot}. Its place in the bank, {place}, is kept for it.",
                note: $"Shown as it was when it left the bank on {Date(newest.CapturedAt)}. It may have changed in the game since.")
            : Build(newest.ToPkm(), Transfers.GameOf(bank, newest), bank, record, saveName,
                where: $"In the bank, {place}.", note: null);
    }

    /// <summary>
    /// The Pokémon in a game's slot, or null when the slot is empty. When it's a bank Pokémon that's out in this game,
    /// what the bank holds for it is shown too.
    /// </summary>
    /// <param name="saveTitle">What to call this save, e.g. "Platinum (Eve)".</param>
    public static PokemonDetails? ForSave(Bank bank, SaveFile save, Guid saveId, SaveSlot slot, string saveTitle,
        Func<Guid, string> saveName)
    {
        var pk = slot.IsParty
            ? slot.Slot >= 1 && slot.Slot <= save.PartyCount ? save.GetPartySlotAtIndex(slot.Slot - 1) : null
            : save.GetBoxSlotAtIndex((slot.Box!.Value - 1) * save.BoxSlotCount + slot.Slot - 1);
        if (pk is null || pk.Species == 0)
            return null;

        var record = Identity.Find(bank, pk, saveId).Match;
        var where = slot.IsParty
            ? $"In {saveTitle}, party slot {slot.Slot}."
            : $"In {saveTitle}, {SaveContents.BoxName(save, slot.Box!.Value)} slot {slot.Slot}.";
        if (record is not null)
            where += $" It's out from the bank, where {bank.Boxes.Describe(record.Slot)} is kept for it.";
        return Build(pk, SaveDetector.GameOf(save), bank, record, saveName, where, note: null);
    }

    static PokemonDetails Build(PKM pk, GameVersion game, Bank bank, BankPokemon? record, Func<Guid, string> saveName,
        string where, string? note)
    {
        var species = EnglishNames.Species(pk.Species);
        return new PokemonDetails
        {
            Title = pk.IsEgg ? "Egg" : pk.IsNicknamed ? pk.Nickname : species,
            Subtitle = SubtitleOf(pk, species),
            Where = where,
            Note = note,
            Facts = FactsOf(pk),
            Stats = StatsOf(pk),
            Moves = [.. Enumerable.Range(0, 4)
                .Where(i => pk.GetMove(i) != 0)
                .Select(i => Move(pk, i))],
            Ribbons = RibbonsOf(pk),
            Origin = OriginOf(pk, record, EnglishNames.Game(game)),
            Visits = record is null ? [] : VisitsOf(bank, record, saveName),
            Kept = record is null ? [] : KeptOf(bank, record, pk, game, saveName),
            InGame = EnglishNames.Game(game),
        };
    }

    static MoveLine Move(PKM pk, int i)
    {
        var (pp, ups) = i switch
        {
            0 => (pk.Move1_PP, pk.Move1_PPUps),
            1 => (pk.Move2_PP, pk.Move2_PPUps),
            2 => (pk.Move3_PP, pk.Move3_PPUps),
            _ => (pk.Move4_PP, pk.Move4_PPUps),
        };
        var move = pk.GetMove(i);
        return new MoveLine(EnglishNames.Move(move), pp, pk.GetMovePP(move, ups));
    }

    static string SubtitleOf(PKM pk, string species)
    {
        var parts = new List<string>();
        if (pk.IsEgg)
            parts.Add($"{species} egg");
        else
        {
            if (pk.IsNicknamed)
                parts.Add(species);
            parts.Add($"Lv{pk.CurrentLevel}");
            if (pk.Gender < 2)
                parts.Add(GameInfo.GenderSymbolUnicode[pk.Gender]);
        }
        if (pk.IsShiny)
            parts.Add("★ Shiny");
        return string.Join(" · ", parts);
    }

    static List<Fact> FactsOf(PKM pk)
    {
        var facts = new List<Fact>
        {
            new("Nature", Nature(pk.Nature)),
            new("Ability", EnglishNames.Ability(pk.Ability)),
            new("Held item", EnglishNames.HeldItem(pk) ?? "None"),
        };
        if (pk.Characteristic >= 0)
            facts.Add(new("Characteristic", EnglishNames.Characteristic(pk.Characteristic)));
        return facts;
    }

    static string Nature(Nature nature)
    {
        var (up, down) = NatureAmp.GetNatureModification(nature);
        var name = EnglishNames.Nature(nature);
        // NatureAmp counts from Attack: 0 Attack, 1 Defense, 2 Speed, 3 Sp. Atk, 4 Sp. Def.
        return up == down ? $"{name} (no effect)" : $"{name} (+{StatNames[up + 1]}, −{StatNames[down + 1]})";
    }

    static List<StatLine> StatsOf(PKM pk)
    {
        // Box data has no stats, so they're worked out the way the game does, from base stats, IVs, EVs, nature and level.
        var values = pk.GetStats(pk.PersonalInfo);
        Span<int> ivs = stackalloc int[6];
        Span<int> evs = stackalloc int[6];
        pk.GetIVs(ivs);
        pk.GetEVs(evs);
        var (up, down) = NatureAmp.GetNatureModification(pk.Nature);
        var lines = new List<StatLine>();
        foreach (var i in ShownOrder)
        {
            var effect = up == down || i == 0 ? 0 : i == up + 1 ? 1 : i == down + 1 ? -1 : 0;
            lines.Add(new StatLine(StatNames[i], values[i], ivs[i], evs[i], effect));
        }
        return lines;
    }

    static List<string> RibbonsOf(PKM pk)
    {
        var ribbons = new List<string>();
        foreach (var ribbon in RibbonInfo.GetRibbonInfo(pk))
        {
            // PKHeX works out the Gen 6 memory ribbons from Gen 3–5 contest and battle ribbons; those games have none.
            if (ribbon.Name.StartsWith("RibbonCountMemory", StringComparison.Ordinal))
                continue;
            if (ribbon.Type == RibbonValueType.Boolean)
            {
                if (ribbon.HasRibbon)
                    ribbons.Add(EnglishNames.Ribbon(ribbon.Name));
            }
            else if (ribbon.RibbonCount > 0)
            {
                // Gen 3 keeps each contest ribbon as the highest rank won, 1 (Normal) to 4 (Master).
                var name = EnglishNames.Ribbon(ribbon.Name);
                ribbons.Add(ribbon.Name.StartsWith("RibbonCountG3", StringComparison.Ordinal) && ribbon.RibbonCount < ContestRanks.Length
                    ? $"{name} ({ContestRanks[ribbon.RibbonCount]})"
                    : $"{name} ×{ribbon.RibbonCount}");
            }
        }
        return ribbons;
    }

    static List<Fact> OriginOf(PKM pk, BankPokemon? record, string inGame)
    {
        var generation = record is null ? 0 : Formats.GenerationOf(record.Fingerprint.OriginGame);
        if (record?.SnapshotIn($"PK{generation}") is not { } first || first.Format == pk.GetType().Name)
            return OriginOf(pk);

        var origin = OriginOf(first.ToPkm());
        var here = OriginOf(pk).Where(f => f.Label is "Origin game" or "Met" or "Hatched" or "Received").ToList();
        var there = origin.Where(f => f.Label is "Origin game" or "Met" or "Hatched" or "Received").ToList();
        if (!here.SequenceEqual(there))
            origin.Add(new($"In {inGame}", string.Join(" · ", here.Select(f => f.Label == "Origin game" ? $"from {f.Value}" : f.Value))));
        return origin;
    }

    static List<Fact> OriginOf(PKM pk)
    {
        var gender = pk.OriginalTrainerGender < 2 ? " " + GameInfo.GenderSymbolUnicode[pk.OriginalTrainerGender] : "";
        var origin = new List<Fact>
        {
            new("Trainer", $"{pk.OriginalTrainerName}{gender} · ID {pk.TID16:D5}"),
            new("Origin game", EnglishNames.OriginGame(pk.Version)),
        };

        // Gen 3 has no egg location: a hatched Pokémon is one met at level 0, where it hatched.
        var hatched = pk.Format == 3 ? pk.MetLevel == 0 : pk.EggLocation != 0;
        var met = EnglishNames.Location(pk, egg: false) + On(pk.MetDate);
        if (pk.IsEgg)
            origin.Add(new("Received", pk.Format == 3 ? met : EnglishNames.Location(pk, egg: true) + On(pk.EggMetDate)));
        else if (hatched)
        {
            if (pk.Format > 3)
                origin.Add(new("Egg received", EnglishNames.Location(pk, egg: true) + On(pk.EggMetDate)));
            origin.Add(new("Hatched", met));
        }
        else
            origin.Add(new("Met", $"{met}, at Lv{pk.MetLevel}"));

        origin.Add(new("Ball", EnglishNames.Ball(pk.Ball)));
        if (pk.FatefulEncounter)
            origin.Add(new("Fateful encounter", "Yes (from an event)"));
        return origin;
    }

    static List<Visit> VisitsOf(Bank bank, BankPokemon record, Func<Guid, string> saveName)
    {
        var newest = record.Newest;
        return [.. record.Snapshots
            .Where(s => Snapshot.KnownFormats.Contains(s.Format))
            .OrderBy(s => s.Format)
            .Select(s => new Visit(Generation(s), GameName(s, saveName, bank), s.ToPkm().CurrentLevel, s.CapturedAt, s == newest))];
    }

    /// <summary>
    /// Each other generation's data, converted to the game it's in: what the conversion drops or replaces is what that
    /// data holds and this game can't. The bank keeps it, and it can come back in a game of that generation.
    /// </summary>
    static List<KeptData> KeptOf(Bank bank, BankPokemon record, PKM pk, GameVersion game, Func<Guid, string> saveName)
    {
        var format = pk.GetType().Name;
        var kept = new List<KeptData>();
        foreach (var snapshot in record.Snapshots.Where(s => s.Format != format && Snapshot.KnownFormats.Contains(s.Format)).OrderBy(s => s.Format))
        {
            var converted = Formats.Convert(snapshot.ToPkm(), game, snapshot.CapturedAt);
            if (converted.Succeeded && converted.Notes.Count > 0)
                kept.Add(new KeptData(Generation(snapshot), GameName(snapshot, saveName, bank), [.. converted.Notes.Select(n => n.Message)]));
        }
        return kept;
    }

    static int Generation(Snapshot snapshot) => snapshot.Format[^1] - '0';

    static string GameName(Snapshot snapshot, Func<Guid, string> saveName, Bank bank) =>
        snapshot.FromSave is { } id && bank.Saves.Any(s => s.Id == id) ? saveName(id) : $"a Gen {Generation(snapshot)} game";

    static string On(DateOnly? date) => date is { } d ? $", {Date(d)}" : "";

    /// <summary>A date the way the app writes one, e.g. "Nov 9, 2026".</summary>
    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateTimeOffset when) => Date(DateOnly.FromDateTime(when.ToLocalTime().Date));
}
