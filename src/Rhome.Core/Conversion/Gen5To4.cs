using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// Downgrades a Gen 5 Pokémon (Black to White 2) to Gen 4 (Diamond to SoulSilver). PKHeX only converts
/// forward, so this one is rhome's own (PLAN.md §4). The two formats share most of their layout (PKHeX's
/// PK4 → PK5 copies the bytes and fixes a few), so this is that step in reverse, plus a decision for every
/// Gen 5 thing Gen 4 can't hold: refused, replaced or dropped, and reported.
/// </summary>
/// <remarks>
/// Gen 4 has no hidden abilities. A Pokémon with one gets a regular ability of its species instead; the bank
/// keeps the Gen 5 snapshot, so the hidden ability comes back when it returns to Gen 5 (PLAN.md §2).
/// </remarks>
public static class Gen5To4
{
    public const ushort MaxSpecies = 493;  // Arceus
    public const ushort MaxMove = 467;     // Shadow Force

    /// <summary>
    /// Converts <paramref name="pk5"/> for a Gen 4 game. <paramref name="target"/> is the game it's going
    /// into: Diamond and Pearl lack some forms and items that Platinum and HeartGold/SoulSilver have, and a
    /// Pokémon born in Gen 5 takes it as its origin game, since Gen 4 can't name Unova's games.
    /// </summary>
    public static ConversionResult<PK4> Convert(PK5 pk5, GameVersion target)
    {
        var game = Gen4Game(target)
            ?? throw new ArgumentException($"{target} isn't a Gen 4 game.", nameof(target));

        if (Refusal(pk5, game) is { } refusal)
            return ConversionResult<PK4>.Refused(refusal);

        var notes = new List<ConversionNote>();
        var pk4 = new PK4
        {
            Species = pk5.Species,
            Form = pk5.Form,
            Gender = pk5.Gender,
            TID16 = pk5.TID16,
            SID16 = pk5.SID16,
            Language = pk5.Language,
            EXP = pk5.EXP,
            OriginalTrainerFriendship = pk5.OriginalTrainerFriendship,
            OriginalTrainerGender = pk5.OriginalTrainerGender,
            MarkingValue = pk5.MarkingValue,
            FatefulEncounter = pk5.FatefulEncounter,
            PokerusState = pk5.PokerusState,
            IV32 = pk5.IV32 & 0x3FFFFFFF, // the IVs; the egg and nickname flags are set below
            EV_HP = pk5.EV_HP,
            EV_ATK = pk5.EV_ATK,
            EV_DEF = pk5.EV_DEF,
            EV_SPA = pk5.EV_SPA,
            EV_SPD = pk5.EV_SPD,
            EV_SPE = pk5.EV_SPE,
            ContestCool = pk5.ContestCool,
            ContestBeauty = pk5.ContestBeauty,
            ContestCute = pk5.ContestCute,
            ContestSmart = pk5.ContestSmart,
            ContestTough = pk5.ContestTough,
            ContestSheen = pk5.ContestSheen,
            MetLevel = pk5.MetLevel,
            MetYear = pk5.MetYear,
            MetMonth = pk5.MetMonth,
            MetDay = pk5.MetDay,
            EggYear = pk5.EggYear,
            EggMonth = pk5.EggMonth,
            EggDay = pk5.EggDay,
            GroundTile = pk5.GroundTile,
        };

        CopyRibbons(pk5, pk4);
        CopyNames(pk5, pk4, notes);
        MoveCopy.Copy(pk5, pk4, MaxMove, 4, notes);
        CopyHeldItem(pk5, pk4, game, notes);
        CopyPidAndAbility(pk5, pk4, game, notes);
        CopyOrigin(pk5, pk4, game, notes);
        CopyByte0x87(pk5, pk4, notes);
        NoteGen5Only(pk5, notes);

        pk4.ResetPartyStats();
        pk4.RefreshChecksum();
        return ConversionResult<PK4>.Converted(pk4, notes);
    }

    /// <summary>Why <paramref name="pk5"/> can't exist in the Gen 4 game <paramref name="target"/>, or null when it can.</summary>
    public static string? Refusal(PK5 pk5, GameVersion target)
    {
        var species = pk5.Species;
        var name = EnglishNames.Species(species);
        var game = Gen4Game(target) ?? throw new ArgumentException($"{target} isn't a Gen 4 game.", nameof(target));

        if (pk5.IsEgg)
            return "Eggs can't be moved to a Gen 4 game. Hatch it first.";
        if (species is 0 || species > MaxSpecies)
            return $"{name} doesn't exist in the Gen 4 games.";
        // Unown's 28 letters share one entry in the games' stat tables, so PKHeX knows them separately.
        if (!Personal(game)[species].IsFormWithinRange(pk5.Form) && !FormInfo.IsValidOutOfBoundsForm(species, pk5.Form, 4))
            return $"This form of {name} doesn't exist in {GameName(game)}.";
        if (pk5.Language is < (int)LanguageID.Japanese or > (int)LanguageID.Korean or (int)LanguageID.UNUSED_6)
            return $"This {name} has no valid language, so rhome can't tell how to write its name in Gen 4.";
        // Gen 5 stores the gender, but still picks the PID to match it, so this only catches odd data. (Its
        // nature doesn't follow the PID; Convert gives such a Pokémon a new PID instead of refusing it.)
        if (EntityGender.GetFromPID(species, pk5.PID) != pk5.Gender)
            return $"This {name}'s gender would change in Gen 4, where it comes from its personality value.";
        if (!MoveCopy.AnyUpTo(pk5, MaxMove))
            return $"None of this {name}'s moves exist in Gen 4, and a Pokémon needs at least one.";
        return null;
    }

    /// <summary>The game to record as the origin, or null when <paramref name="game"/> isn't a Gen 4 game.</summary>
    static GameVersion? Gen4Game(GameVersion game) => game switch
    {
        GameVersion.D or GameVersion.P or GameVersion.Pt or GameVersion.HG or GameVersion.SS => game,
        // A save from a pair can't say which of the two it is; both show a Pokémon the same way.
        GameVersion.DP or GameVersion.DPPt => GameVersion.D,
        GameVersion.HGSS => GameVersion.HG,
        _ => null,
    };

    static PersonalTable4 Personal(GameVersion game) => game switch
    {
        GameVersion.D or GameVersion.P => PersonalTable.DP,
        GameVersion.Pt => PersonalTable.Pt,
        _ => PersonalTable.HGSS,
    };

    static string GameName(GameVersion game) => game switch
    {
        GameVersion.D or GameVersion.P => "Diamond and Pearl",
        GameVersion.Pt => "Platinum",
        _ => "HeartGold and SoulSilver",
    };

    // Blank saves, only to ask PKHeX which items each game lets a Pokémon hold.
    static readonly SaveFile DP = new SAV4DP(), Pt = new SAV4Pt(), HGSS = new SAV4HGSS();

    static ReadOnlySpan<ushort> HeldItems(GameVersion game) => game switch
    {
        GameVersion.D or GameVersion.P => DP.HeldItems,
        GameVersion.Pt => Pt.HeldItems,
        _ => HGSS.HeldItems,
    };

    static void CopyRibbons(PK5 pk5, PK4 pk4)
    {
        // Gen 5 added no ribbons, and keeps Gen 4's three ribbon blocks exactly where Gen 4 has them.
        foreach (var (start, length) in new[] { (0x24, 4), (0x3C, 4), (0x60, 4) })
            pk5.Data.Slice(start, length).CopyTo(pk4.Data.Slice(start, length));
    }

    static void CopyNames(PK5 pk5, PK4 pk4, List<ConversionNote> notes)
    {
        var language = pk5.Language;
        pk4.IsNicknamed = pk5.IsNicknamed;

        // An unnamed Pokémon gets the Gen 4 species name (all capitals in English, unlike Gen 5).
        var nickReplaced = "";
        var nickname = pk5.IsNicknamed
            ? GameText.Fit(pk5.Nickname, 10, c => GameText.Gen4CanShow(c, language), out nickReplaced)
            : SpeciesName.GetSpeciesNameGeneration(pk5.Species, language, 4);
        if (nickReplaced.Length > 0)
            notes.Add(new(ChangeKind.Replaced, ChangedField.Nickname,
                $"The nickname \"{pk5.Nickname}\" becomes \"{nickname}\": Gen 4 can't show {GameText.Quote(nickReplaced)}."));
        pk4.Nickname = nickname;

        var trainer = GameText.Fit(pk5.OriginalTrainerName, 7, c => GameText.Gen4CanShow(c, language), out var otReplaced);
        if (otReplaced.Length > 0)
            notes.Add(new(ChangeKind.Replaced, ChangedField.TrainerName,
                $"The trainer name \"{pk5.OriginalTrainerName}\" becomes \"{trainer}\": Gen 4 can't show {GameText.Quote(otReplaced)}."));
        pk4.OriginalTrainerName = trainer;
    }

    static void CopyHeldItem(PK5 pk5, PK4 pk4, GameVersion game, List<ConversionNote> notes)
    {
        // Gen 5 kept Gen 4's item numbers and added its own after them, so an item either means the same
        // thing in both or doesn't exist in Gen 4.
        var item = (ushort)pk5.HeldItem;
        if (item == 0)
            return;
        if (HeldItems(game).Contains(item))
        {
            pk4.HeldItem = item;
            return;
        }
        notes.Add(new(ChangeKind.Dropped, ChangedField.HeldItem,
            $"Dropped the held item {EnglishNames.Item(item)} (can't be held in {GameName(game)})."));
    }

    /// <summary>
    /// The ability, and the PID when the nature needs a new one. Gen 4 has no hidden abilities, and picks a
    /// species' ability slot from bit 0 of the PID, so a hidden (or Gen 5-only) ability becomes the one in
    /// the slot the original PID points at, and a new PID is chosen to point at the same slot.
    /// </summary>
    static void CopyPidAndAbility(PK5 pk5, PK4 pk4, GameVersion game, List<ConversionNote> notes)
    {
        var personal = Personal(game).GetFormEntry(pk5.Species, pk5.Form);
        var regular = pk5.HiddenAbility ? -1 : personal.GetIndexOfAbility(pk5.Ability);
        var slot = regular != -1 ? regular : personal.HasSecondAbility ? (int)(pk5.PID & 1) : 0;

        pk4.PID = pk5.PID;
        if (pk5.Nature != (Nature)(pk5.PID % 25))
        {
            pk4.PID = PidSearch.Find(pk5, pk5.Nature, slot, personal.HasSecondAbility);
            var spinda = pk5.Species == (ushort)PKHeX.Core.Species.Spinda ? " Its spot pattern changes with it." : "";
            notes.Add(new(ChangeKind.Replaced, ChangedField.PersonalityValue,
                $"Gets a new personality value so it stays {pk5.Nature} (Gen 4 works the nature out from it; " +
                $"gender, shininess and ability stay the same).{spinda}"));
        }

        if (regular != -1)
        {
            pk4.Ability = pk5.Ability;
            return;
        }
        pk4.Ability = personal.GetAbilityAtIndex(slot);
        var why = pk5.HiddenAbility
            ? "Gen 4 has no hidden abilities"
            : $"{EnglishNames.Species(pk5.Species)} can't have it in Gen 4";
        notes.Add(new(ChangeKind.Replaced, ChangedField.Ability,
            $"The {(pk5.HiddenAbility ? "hidden ability" : "ability")} {EnglishNames.Ability(pk5.Ability)} " +
            $"becomes {EnglishNames.Ability(pk4.Ability)} ({why})."));
    }

    static void CopyOrigin(PK5 pk5, PK4 pk4, GameVersion game, List<ConversionNote> notes)
    {
        // Version first: where Gen 4 stores the ball and met location depends on the game it came from.
        var version = pk5.Version;
        var gen3 = version is GameVersion.R or GameVersion.S or GameVersion.E
            or GameVersion.FR or GameVersion.LG or GameVersion.CXD;
        var gen4 = version is GameVersion.D or GameVersion.P or GameVersion.Pt or GameVersion.HG or GameVersion.SS;
        pk4.Version = gen3 || gen4 ? version : game;

        pk4.Ball = pk5.Ball;
        if (pk4.Ball != pk5.Ball)
            notes.Add(new(ChangeKind.Replaced, ChangedField.Ball,
                $"The {EnglishNames.Ball(pk5.Ball)} becomes a {EnglishNames.Ball(pk4.Ball)} (not in {GameName(game)})."));

        // Gen 4 can't name a Unova place. Poké Transfer already replaced the Gen 4 met location of one
        // that came from Gen 4 (the bank's Gen 4 snapshot still has it); one from Gen 3 shows as Pal Park.
        if (gen3)
        {
            pk4.MetLocation = Locations.Transfer3;
            notes.Add(new(ChangeKind.Replaced, ChangedField.MetData,
                "Shows as arrived through Pal Park (its earlier met locations didn't survive the trip to Gen 5)."));
        }
        else
        {
            pk4.MetLocation = Locations.Faraway4;
            notes.Add(new(ChangeKind.Replaced, ChangedField.MetData, gen4
                ? "Shows as met in a faraway place (its Gen 4 met location didn't survive the trip to Gen 5)."
                : $"Shows as met in a faraway place, as if from {GameName(game)} (Gen 4 can't name the Gen 5 game or place it came from)."));
        }

        pk4.EggLocation = pk5.EggLocation switch
        {
            0 => 0,
            Locations.Daycare5 => Locations.Daycare4,
            Locations.LinkTrade5 => Locations.LinkTrade4,
            // Gen 4 egg locations survive Poké Transfer untouched; Unova ones have no Gen 4 equivalent.
            < 30000 and var gen4Location => gen4Location,
            _ => Locations.Faraway4,
        };
    }

    static void CopyByte0x87(PK5 pk5, PK4 pk4, List<ConversionNote> notes)
    {
        // Gen 4 keeps HeartGold/SoulSilver's walking mood here and Gen 5 its Pokéstar Studios fame. Poké
        // Transfer copies the byte as it is, so for a Pokémon from Gen 4 it's still the walking mood.
        if (pk5.Version is GameVersion.D or GameVersion.P or GameVersion.Pt or GameVersion.HG or GameVersion.SS)
            pk4.WalkingMood = (sbyte)pk5.PokeStarFame;
        else if (pk5.PokeStarFame > 0)
            notes.Add(new(ChangeKind.Dropped, ChangedField.Other, "Dropped its Pokéstar Studios fame (not in Gen 4)."));
    }

    static void NoteGen5Only(PK5 pk5, List<ConversionNote> notes)
    {
        if (pk5.NSparkle)
            notes.Add(new(ChangeKind.Dropped, ChangedField.Other, "Dropped N's sparkle (Gen 4 has no N's Pokémon)."));
    }
}
