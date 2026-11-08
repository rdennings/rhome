using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// Puts back what only one format holds when a Pokémon returns to a game of that format (PLAN.md §5:
/// "Format-only fields restore from that format's snapshot"). The Pokémon is first converted from its newest
/// snapshot, so its progress follows the newest game; this step then restores that format's own data from the
/// snapshot it had there.
/// </summary>
/// <remarks>
/// One rule decides the fields a player can change in the other game, or that rhome changed on the way out
/// (held item, nickname, the PID a Gen 5 Pokémon got for its nature): restore the old value only if the other game left the field as
/// rhome sent it out. So a Griseous Orb dropped on the way to Emerald comes back, but an item the player gave
/// or took in Emerald stays as Emerald left it, and nothing is duplicated.
/// Met data, ribbons, the ability slot and the format's extras (HeartGold/SoulSilver's walking mood, N's
/// sparkle…) can't be changed elsewhere, so they always come back; ribbons won in the other game are kept too.
/// </remarks>
public static class FormatRestore
{
    /// <param name="result">The Pokémon converted from <paramref name="newest"/> into the target format. Changed in place.</param>
    /// <param name="newest">The Pokémon as the game it was last in left it.</param>
    /// <param name="snapshot">Its snapshot in the target format, from the last time it was in a game of that format.</param>
    /// <param name="newestGame">The game <paramref name="newest"/> came from, to work out what rhome sent there.</param>
    public static void Apply(PKM result, PKM newest, PKM snapshot, GameVersion newestGame, DateTimeOffset now, List<ConversionNote> notes)
    {
        if (result.GetType() != snapshot.GetType())
            throw new ArgumentException("The snapshot must be in the same format as the result.", nameof(snapshot));

        RestoreMetData(result, snapshot);
        RestoreRibbons(result, snapshot);
        RestoreExtras(result, snapshot);
        RestoreAbility(result, snapshot);

        // What rhome would have sent to the other game from this snapshot. Comparing it with what came back
        // tells which fields the player changed there.
        var sent = Formats.Convert(snapshot, newestGame, now);
        if (!sent.Succeeded)
            return;
        var outbound = sent.Pokemon;

        if (outbound.PID == newest.PID && result.PID != snapshot.PID)
        {
            result.PID = snapshot.PID; // the original PID of a Gen 5 Pokémon that got a new one for its nature
            if (result is PK5 pk5 && snapshot is PK5 old5)
                pk5.Nature = old5.Nature;
        }

        if (snapshot.IsNicknamed && outbound.Nickname == newest.Nickname && outbound.IsNicknamed == newest.IsNicknamed)
        {
            result.IsNicknamed = true;
            result.Nickname = snapshot.Nickname; // e.g. characters the older game had to show as '?'
        }
        if (outbound.OriginalTrainerName == newest.OriginalTrainerName)
            result.OriginalTrainerName = snapshot.OriginalTrainerName;

        RestoreHeldItem(result, newest, snapshot, outbound, notes);

        result.RefreshChecksum();
    }

    static void RestoreHeldItem(PKM result, PKM newest, PKM snapshot, PKM outbound, List<ConversionNote> notes)
    {
        if (outbound.HeldItem == newest.HeldItem)
        {
            result.HeldItem = snapshot.HeldItem;
            return;
        }
        // The player changed the item in the other game, so that's what it holds now. An item that had to stay
        // behind can't come back on top of it.
        if (snapshot.HeldItem != 0 && outbound.HeldItem == 0)
            notes.Add(new(ChangeKind.Dropped, ChangedField.HeldItem,
                $"Can't give back the held item {EnglishNames.Item(snapshot.HeldItem)}: it was given another item while it was away."));
    }

    /// <summary>
    /// The ability slot (first, second or hidden). Gen 3–5 games have no way to change it (the Ability Capsule
    /// came in Gen 6), so it always comes back. A slot rather than an ability, because the Pokémon may have
    /// evolved: a hidden ability stays the hidden one.
    /// </summary>
    static void RestoreAbility(PKM result, PKM snapshot)
    {
        var slot = AbilitySlot(snapshot);
        if (slot >= 0 && AbilitySlot(result) != slot)
            result.RefreshAbility(slot);
    }

    /// <summary>0 or 1 for a regular ability, 2 for a hidden one, -1 when the ability isn't one of the species'.</summary>
    static int AbilitySlot(PKM pk) => pk switch
    {
        PK5 { HiddenAbility: true } => 2,
        PK3 pk3 => pk3.AbilityBit && ((PersonalInfo3)pk3.PersonalInfo).HasSecondAbility ? 1 : 0,
        _ => pk.PersonalInfo.GetIndexOfAbility(pk.Ability),
    };

    static void RestoreMetData(PKM result, PKM snapshot)
    {
        switch ((result, snapshot))
        {
            case (PK3 r, PK3 s):
                r.Version = s.Version;
                r.MetLocation = s.MetLocation;
                r.MetLevel = s.MetLevel;
                r.Ball = s.Ball;
                r.OriginalTrainerGender = s.OriginalTrainerGender;
                r.FatefulEncounter = s.FatefulEncounter;
                break;
            case (PK4 or PK5, PK4 or PK5):
                // Gen 4 and 5 keep their met data in the same places: the Platinum/HeartGold/SoulSilver
                // locations (0x44), origin game (0x5F), dates and locations (0x78), and ball, level, ground
                // and HeartGold/SoulSilver ball (0x83). Copied as bytes so every game's variant comes along.
                CopyBytes(snapshot, result, 0x44, 4);
                CopyBytes(snapshot, result, 0x5F, 1);
                CopyBytes(snapshot, result, 0x78, 0x0A);
                CopyBytes(snapshot, result, 0x83, 4);
                result.FatefulEncounter = snapshot.FatefulEncounter;
                break;
        }
    }

    static void RestoreRibbons(PKM result, PKM snapshot)
    {
        switch ((result, snapshot))
        {
            case (PK3 r, PK3 s):
                // Gen 3 counts contest wins per category; keep the higher count, and every other ribbon from either.
                r.RibbonCountG3Cool = Math.Max(r.RibbonCountG3Cool, s.RibbonCountG3Cool);
                r.RibbonCountG3Beauty = Math.Max(r.RibbonCountG3Beauty, s.RibbonCountG3Beauty);
                r.RibbonCountG3Cute = Math.Max(r.RibbonCountG3Cute, s.RibbonCountG3Cute);
                r.RibbonCountG3Smart = Math.Max(r.RibbonCountG3Smart, s.RibbonCountG3Smart);
                r.RibbonCountG3Tough = Math.Max(r.RibbonCountG3Tough, s.RibbonCountG3Tough);
                var flags = Read32(s, 0x4C) & 0x7FFF8000; // bit 31 is the fateful flag, restored with the met data
                Write32(r, 0x4C, Read32(r, 0x4C) | flags);
                break;
            case (PK4 or PK5, PK4 or PK5):
                foreach (var offset in new[] { 0x24, 0x3C, 0x60 })
                    Write32(result, offset, Read32(result, offset) | Read32(snapshot, offset));
                break;
        }
    }

    static void RestoreExtras(PKM result, PKM snapshot)
    {
        switch ((result, snapshot))
        {
            case (PK4 r, PK4 s):
                r.ShinyLeaf = s.ShinyLeaf;
                r.WalkingMood = s.WalkingMood;
                break;
            case (PK5 r, PK5 s):
                r.NSparkle = s.NSparkle;
                r.PokeStarFame = s.PokeStarFame;
                break;
        }
    }

    static void CopyBytes(PKM from, PKM to, int offset, int length) =>
        from.Data.Slice(offset, length).CopyTo(to.Data.Slice(offset, length));

    static uint Read32(PKM pk, int offset) => BitConverter.ToUInt32(pk.Data.Slice(offset, 4));

    static void Write32(PKM pk, int offset, uint value) => BitConverter.TryWriteBytes(pk.Data.Slice(offset, 4), value);
}
