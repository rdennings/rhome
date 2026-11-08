using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// Downgrades a Gen 4 Pokémon (Diamond to SoulSilver) to Gen 3 (Ruby to LeafGreen). PKHeX only converts
/// forward, so this one is rhome's own (PLAN.md §4). Ported from <c>prototypes/transfer_pt_to_em.py</c>,
/// whose output was checked in Emerald on the Thor, and hardened: everything Gen 3 can't hold is refused,
/// replaced or dropped on purpose, and each change is reported.
/// </summary>
/// <remarks>
/// The converter only knows the two formats. Restoring what Gen 3 dropped when the Pokémon comes back is
/// the bank's job: it keeps the Gen 4 snapshot (PLAN.md §5).
/// </remarks>
public static class Gen4To3
{
    // The GBA games' limits. PKHeX has the same numbers, but keeps them internal.
    public const ushort MaxSpecies = 386;  // Deoxys
    public const ushort MaxMove = 354;     // Psycho Boost
    public const byte MaxBall = 12;        // Premier Ball; Master to Premier share their IDs in Gen 3 and 4

    /// <summary>Gen 3's met location for "met in a fateful encounter", where a Pokémon from a game it doesn't know is placed.</summary>
    public const byte FatefulEncounterLocation = 0xFF;

    /// <summary>
    /// Converts <paramref name="pk4"/> for a Gen 3 game. <paramref name="target"/> is the game it's going
    /// into; a Pokémon born in Gen 4 takes it as its origin game, since Gen 3 can't name Sinnoh's games.
    /// </summary>
    public static ConversionResult<PK3> Convert(PK4 pk4, GameVersion target)
    {
        var originGame = Gen3Game(target)
            ?? throw new ArgumentException($"{target} isn't a Gen 3 game.", nameof(target));

        if (Refusal(pk4) is { } refusal)
            return ConversionResult<PK3>.Refused(refusal);

        var notes = new List<ConversionNote>();
        var pk3 = new PK3
        {
            PID = pk4.PID,
            Species = pk4.Species,
            TID16 = pk4.TID16,
            SID16 = pk4.SID16,
            Language = pk4.Language,
            EXP = pk4.EXP,
            OriginalTrainerFriendship = pk4.OriginalTrainerFriendship,
            OriginalTrainerGender = pk4.OriginalTrainerGender,
            MarkingValue = (byte)(pk4.MarkingValue & 0b1111), // Gen 3 has four markings, Gen 4 six
            PokerusDays = pk4.PokerusDays,
            PokerusStrain = pk4.PokerusStrain,
            IV_HP = pk4.IV_HP,
            IV_ATK = pk4.IV_ATK,
            IV_DEF = pk4.IV_DEF,
            IV_SPA = pk4.IV_SPA,
            IV_SPD = pk4.IV_SPD,
            IV_SPE = pk4.IV_SPE,
            EV_HP = pk4.EV_HP,
            EV_ATK = pk4.EV_ATK,
            EV_DEF = pk4.EV_DEF,
            EV_SPA = pk4.EV_SPA,
            EV_SPD = pk4.EV_SPD,
            EV_SPE = pk4.EV_SPE,
            ContestCool = pk4.ContestCool,
            ContestBeauty = pk4.ContestBeauty,
            ContestCute = pk4.ContestCute,
            ContestSmart = pk4.ContestSmart,
            ContestTough = pk4.ContestTough,
            ContestSheen = pk4.ContestSheen,
            MetLevel = pk4.MetLevel,
        };

        CopyNames(pk4, pk3, notes);
        CopyMoves(pk4, pk3, notes);
        CopyHeldItem(pk4, pk3, notes);
        CopyAbility(pk4, pk3, notes);
        CopyOrigin(pk4, pk3, originGame, notes);
        CopyRibbons(pk4, pk3, notes);
        NoteForm(pk4, notes);

        // Party-only byte: which of the save's letters it holds. The games use 0xFF for none; a 0 here would
        // point a Pokémon in the party at the first letter (found by the native diff, PLAN.md §6.4).
        pk3.HeldMailID = -1;
        pk3.ResetPartyStats();
        pk3.RefreshChecksum();
        return ConversionResult<PK3>.Converted(pk3, notes);
    }

    /// <summary>Why <paramref name="pk4"/> can't exist in a Gen 3 game at all, or null when it can.</summary>
    public static string? Refusal(PK4 pk4)
    {
        var species = pk4.Species;
        var name = EnglishNames.Species(species);

        if (pk4.IsEgg)
            return "Eggs can't be moved to a Gen 3 game. Hatch it first.";
        if (species is 0 || species > MaxSpecies)
            return $"{name} doesn't exist in the Gen 3 games.";
        if (species is (ushort)PKHeX.Core.Species.Pichu && pk4.Form != 0)
            return "The Spiky-eared Pichu doesn't exist in the Gen 3 games.";
        if (species is (ushort)PKHeX.Core.Species.Unown && EntityPID.GetUnownForm3(pk4.PID) != pk4.Form)
            return "This Unown would show a different letter in Gen 3, where the letter comes from its personality value.";
        if (pk4.Language == (int)LanguageID.Korean)
            return "The Gen 3 games have no Korean text, so a Korean Pokémon can't be shown there.";
        if (pk4.Language is < (int)LanguageID.Japanese or > (int)LanguageID.Spanish or (int)LanguageID.UNUSED_6)
            return $"This {name} has no valid language, so rhome can't tell how to write its name in Gen 3.";
        if (EntityGender.GetFromPID(species, pk4.PID) != pk4.Gender)
            return $"This {name}'s gender would change in Gen 3, where it comes from its personality value.";
        if (!MoveCopy.AnyUpTo(pk4, MaxMove))
            return $"None of this {name}'s moves exist in Gen 3, and a Pokémon needs at least one.";
        return null;
    }

    /// <summary>The game to record as the origin, or null when <paramref name="game"/> isn't a Gen 3 game.</summary>
    static GameVersion? Gen3Game(GameVersion game) => game switch
    {
        GameVersion.R or GameVersion.S or GameVersion.E or GameVersion.FR or GameVersion.LG => game,
        // A save from a pair can't say which of the two it is; both show a Pokémon the same way.
        GameVersion.RS or GameVersion.RSE => GameVersion.R,
        GameVersion.FRLG => GameVersion.FR,
        _ => null,
    };

    static void CopyNames(PK4 pk4, PK3 pk3, List<ConversionNote> notes)
    {
        var language = pk4.Language;
        var japanese = language == (int)LanguageID.Japanese;

        // Gen 3 has no "nicknamed" flag: a Pokémon whose name isn't its species name is nicknamed. So an
        // unnamed one gets the Gen 3 species name (all capitals in English, like the GBA games).
        var nickReplaced = "";
        var nickname = pk4.IsNicknamed
            ? GameText.Fit(pk4.Nickname, japanese ? 5 : 10, c => GameText.Gen3CanShow(c, language), out nickReplaced)
            : SpeciesName.GetSpeciesNameGeneration(pk4.Species, language, 3);
        if (nickReplaced.Length > 0)
            notes.Add(new(ChangeKind.Replaced, ChangedField.Nickname,
                $"The nickname \"{pk4.Nickname}\" becomes \"{nickname}\": Gen 3 can't show {GameText.Quote(nickReplaced)}."));
        GameText.WriteGen3(pk3.NicknameTrash, nickname, language);

        var trainer = GameText.Fit(pk4.OriginalTrainerName, japanese ? 5 : 7, c => GameText.Gen3CanShow(c, language), out var otReplaced);
        if (otReplaced.Length > 0)
            notes.Add(new(ChangeKind.Replaced, ChangedField.TrainerName,
                $"The trainer name \"{pk4.OriginalTrainerName}\" becomes \"{trainer}\": Gen 3 can't show {GameText.Quote(otReplaced)}."));
        GameText.WriteGen3(pk3.OriginalTrainerTrash, trainer, language);
    }

    // Moves Gen 3 doesn't have are dropped, and the rest move up so there are no gaps, as in the games.
    static void CopyMoves(PK4 pk4, PK3 pk3, List<ConversionNote> notes) => MoveCopy.Copy(pk4, pk3, MaxMove, 3, notes);

    static void CopyHeldItem(PK4 pk4, PK3 pk3, List<ConversionNote> notes)
    {
        var item = (ushort)pk4.HeldItem;
        if (item == 0)
            return;

        var gen3 = ItemConverter.GetItemOld3(item);
        string? why = gen3 switch
        {
            0 => "not in Gen 3",
            // A Gen 3 Enigma Berry takes its effect from e-Reader data in the save, which this one doesn't have.
            // (Gen 4's mail has no Gen 3 counterpart, so it's caught by the 0 above.)
            175 => "Gen 3's Enigma Berry needs e-Reader data",
            _ => null,
        };
        if (why is null)
        {
            pk3.HeldItem = gen3;
            return;
        }
        notes.Add(new(ChangeKind.Dropped, ChangedField.HeldItem,
            $"Dropped the held item {EnglishNames.Item(item)} ({why})."));
    }

    /// <summary>
    /// Gen 4 stores the ability itself; Gen 3 stores which of the species' two abilities it has. When the
    /// Gen 4 ability isn't one of the Gen 3 species' abilities, it gets the one its personality value picks.
    /// </summary>
    static void CopyAbility(PK4 pk4, PK3 pk3, List<ConversionNote> notes)
    {
        var personal = PersonalTable.E[pk4.Species];
        var index = personal.GetIndexOfAbility(pk4.Ability);
        var pidSlot = (pk4.PID & 1) == 1;

        if (!personal.HasSecondAbility || index == -1)
        {
            // One ability: the games pick the slot from the PID, so do the same. Unknown ability: the
            // PID's slot is the one the species would have had in Gen 3.
            pk3.AbilityBit = pidSlot && personal.HasSecondAbility;
            if (index == -1)
                notes.Add(new(ChangeKind.Replaced, ChangedField.Ability,
                    $"The ability {EnglishNames.Ability(pk4.Ability)} becomes {EnglishNames.Ability(pk3.Ability)} " +
                    $"({EnglishNames.Species(pk4.Species)} can't have it in Gen 3)."));
            return;
        }
        pk3.AbilityBit = index == 1;
    }

    static void CopyOrigin(PK4 pk4, PK3 pk3, GameVersion target, List<ConversionNote> notes)
    {
        var ball = pk4.Ball;
        if (ball is 0 or > MaxBall)
        {
            notes.Add(new(ChangeKind.Replaced, ChangedField.Ball,
                $"The {EnglishNames.Ball(ball)} becomes a Poké Ball (not in Gen 3)."));
            ball = (byte)PKHeX.Core.Ball.Poke;
        }
        pk3.Ball = ball;

        // Gen 3 can't name a Sinnoh place or game, so the Pokémon shows as met in a fateful encounter, in the
        // game it's going to. One that came from Gen 3 through Pal Park keeps its own Gen 3 game, but its Gen 3
        // met location was already lost on the way to Gen 4.
        var fromGen3 = pk4.Version is GameVersion.R or GameVersion.S or GameVersion.E
            or GameVersion.FR or GameVersion.LG or GameVersion.CXD;
        pk3.Version = fromGen3 ? pk4.Version : target;
        pk3.MetLocation = FatefulEncounterLocation;
        notes.Add(new(ChangeKind.Replaced, ChangedField.MetData, fromGen3
            ? "Shows as met in a fateful encounter (its Gen 3 met location didn't survive the trip to Gen 4)."
            : "Shows as met in a fateful encounter, as if from this game (Gen 3 can't name the Gen 4 game or place it came from)."));

        // Mew and Deoxys only obey in Gen 3 when this flag is set; for the rest it's carried over as it was.
        pk3.FatefulEncounter = pk4.FatefulEncounter
            || pk4.Species is (ushort)PKHeX.Core.Species.Mew or (ushort)PKHeX.Core.Species.Deoxys;
    }

    static void CopyRibbons(PK4 pk4, PK3 pk3, List<ConversionNote> notes)
    {
        // The reverse of PKHeX's PK3 → PK4: the Hoenn ribbons have a home in both formats.
        pk3.RibbonChampionG3 = pk4.RibbonChampionG3;
        pk3.RibbonWinning = pk4.RibbonWinning;
        pk3.RibbonVictory = pk4.RibbonVictory;
        pk3.RibbonArtist = pk4.RibbonArtist;
        pk3.RibbonEffort = pk4.RibbonEffort;
        pk3.RibbonChampionBattle = pk4.RibbonChampionBattle;
        pk3.RibbonChampionRegional = pk4.RibbonChampionRegional;
        pk3.RibbonChampionNational = pk4.RibbonChampionNational;
        pk3.RibbonCountry = pk4.RibbonCountry;
        pk3.RibbonNational = pk4.RibbonNational;
        pk3.RibbonEarth = pk4.RibbonEarth;
        pk3.RibbonWorld = pk4.RibbonWorld;

        // Gen 4 has one flag per Hoenn contest rank; Gen 3 counts how many ranks were won in each category.
        pk3.RibbonCountG3Cool = Count(pk4.RibbonG3Cool, pk4.RibbonG3CoolSuper, pk4.RibbonG3CoolHyper, pk4.RibbonG3CoolMaster);
        pk3.RibbonCountG3Beauty = Count(pk4.RibbonG3Beauty, pk4.RibbonG3BeautySuper, pk4.RibbonG3BeautyHyper, pk4.RibbonG3BeautyMaster);
        pk3.RibbonCountG3Cute = Count(pk4.RibbonG3Cute, pk4.RibbonG3CuteSuper, pk4.RibbonG3CuteHyper, pk4.RibbonG3CuteMaster);
        pk3.RibbonCountG3Smart = Count(pk4.RibbonG3Smart, pk4.RibbonG3SmartSuper, pk4.RibbonG3SmartHyper, pk4.RibbonG3SmartMaster);
        pk3.RibbonCountG3Tough = Count(pk4.RibbonG3Tough, pk4.RibbonG3ToughSuper, pk4.RibbonG3ToughHyper, pk4.RibbonG3ToughMaster);

        // Everything else is a Sinnoh ribbon, in the two ribbon blocks Gen 3 doesn't have (0x24 and 0x60).
        var data = pk4.Data;
        var sinnoh = System.Numerics.BitOperations.PopCount(BitConverter.ToUInt32(data[0x24..0x28]))
                     + System.Numerics.BitOperations.PopCount(BitConverter.ToUInt32(data[0x60..0x64]));
        if (sinnoh > 0)
            notes.Add(new(ChangeKind.Dropped, ChangedField.Ribbons,
                sinnoh == 1 ? "Dropped 1 Sinnoh ribbon (not in Gen 3)." : $"Dropped {sinnoh} Sinnoh ribbons (not in Gen 3)."));
    }

    static byte Count(params ReadOnlySpan<bool> ranks)
    {
        byte count = 0;
        foreach (var won in ranks)
            if (won)
                count++;
        return count;
    }

    static void NoteForm(PK4 pk4, List<ConversionNote> notes)
    {
        // Deoxys has no stored form in Gen 3: each game shows its own (Normal in Ruby/Sapphire, Attack in
        // FireRed, Defense in LeafGreen, Speed in Emerald). Unown and the Spiky-eared Pichu are refused above.
        if (pk4.Species == (ushort)PKHeX.Core.Species.Deoxys && pk4.Form != 0)
            notes.Add(new(ChangeKind.Replaced, ChangedField.Form,
                "Deoxys takes the form the Gen 3 game it's in shows (Gen 3 doesn't store a form)."));
    }
}
