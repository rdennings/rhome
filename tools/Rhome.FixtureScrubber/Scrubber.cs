using System.Buffers.Binary;
using PKHeX.Core;

namespace Rhome.FixtureScrubber;

/// <summary>The scrubbed save, what was done to it, and anything that stops it from being used.</summary>
public sealed record ScrubResult(byte[] Data, IReadOnlyList<string> Report, IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>
/// Replaces the player's name, rival name and trainer IDs in a real save, so it can be committed as a
/// test fixture. PKHeX does the structured edits; the tool then handles what PKHeX leaves behind
/// (backup copies, and records such as Gen 3 TV shows) and checks the raw bytes afterwards.
/// </summary>
/// <remarks>
/// Trainer IDs are replaced with random ones, kept the same across every save in one run (a Pokémon
/// moved between two of the player's games keeps matching), and chosen so TID xor SID doesn't change.
/// That keeps every Pokémon's shininess, and the Gen 5 PID bit that depends on the IDs, as they were.
/// </remarks>
public sealed class Scrubber
{
    public const string TrainerName = "TEST";
    public const string RivalName = "RIVAL";

    readonly Dictionary<uint, uint> _ids = [];

    /// <summary>The new ID32 for an old one, made on first use.</summary>
    uint MapId(uint old)
    {
        if (_ids.TryGetValue(old, out var mapped))
            return mapped;

        var oldTid = (ushort)old;
        var oldSid = (ushort)(old >> 16);
        var tid = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var sid = (ushort)(tid ^ oldTid ^ oldSid);
        mapped = (uint)(sid << 16) | tid;
        _ids[old] = mapped;
        return mapped;
    }

    public ScrubResult Scrub(byte[] original)
    {
        var report = new List<string>();
        var problems = new List<string>();

        var sav = Load(original) ?? throw new InvalidDataException("PKHeX doesn't recognize this save.");
        if (sav is not (SAV3 or SAV4 or SAV5BW))
            throw new NotSupportedException(
                $"{sav.GetType().Name} isn't handled yet: where its backup copy lives hasn't been checked on a real save.");

        var player = sav.OT;
        var rival = GetRival(sav);
        var playerOldId = sav.ID32;
        var oldIds = new HashSet<uint> { playerOldId };
        report.Add($"{sav.Version}: trainer and the player's Pokémon renamed to {TrainerName}");

        CheckForUnhandledPokemon(sav, problems);

        // 1. Structured edits through PKHeX.
        sav.OT = TrainerName;
        var newId = MapId(sav.ID32);
        sav.TID16 = (ushort)newId;
        sav.SID16 = (ushort)(newId >> 16);
        if (rival is { Length: > 0 })
        {
            SetRival(sav, RivalName);
            report.Add($"rival renamed to {RivalName}");
        }

        for (int i = 0; i < sav.PartyCount; i++)
        {
            var pk = sav.GetPartySlotAtIndex(i);
            if (ScrubPokemon(pk, player, oldIds, report))
                sav.SetPartySlotAtIndex(pk, i, EntityImportSettings.None);
        }
        for (int i = 0; i < sav.SlotCount; i++)
        {
            var pk = sav.GetBoxSlotAtIndex(i);
            if (ScrubPokemon(pk, player, oldIds, report))
                sav.SetBoxSlotAtIndex(pk, i, EntityImportSettings.None);
        }

        var data = sav.Write(BinaryExportSetting.None).ToArray();
        MirrorBackups(sav, data);

        // 2. Whatever PKHeX doesn't model: find the old name and IDs in the raw bytes and replace them.
        var names = new List<(string Old, string New)> { (player, TrainerName) };
        if (rival is { Length: > 0 } && rival != player)
            names.Add((rival, RivalName));
        ReplaceLeftovers(sav, data, names, oldIds, playerOldId, report, problems);

        // 3. Load and write once more so PKHeX fixes the checksums of anything step 2 touched.
        var final = Load(data);
        if (final is null)
        {
            problems.Add("the scrubbed save no longer loads");
            return new ScrubResult(data, report, problems);
        }
        data = final.Write(BinaryExportSetting.None).ToArray();
        MirrorBackups(final, data);

        Verify(original, data, names.Select(n => n.Old), oldIds, problems);
        return new ScrubResult(data, report, problems);
    }

    static SaveFile? Load(byte[] data) =>
        SaveUtil.TryGetSaveFile(data.ToArray(), out var sav) ? sav : null;

    static string? GetRival(SaveFile sav) => sav switch
    {
        SAV4 s4 => s4.RivalName,
        SAV3FRLG frlg => frlg.RivalName,
        _ => null, // Ruby/Sapphire/Emerald and Gen 5 rivals have fixed names.
    };

    static void SetRival(SaveFile sav, string name)
    {
        switch (sav)
        {
            case SAV4 s4: s4.RivalName = name; break;
            case SAV3FRLG frlg: frlg.RivalName = name; break;
        }
    }

    /// <summary>
    /// Pokémon outside the party and boxes (day care, Battle Box) aren't scrubbed, and Gen 4/5 Pokémon are
    /// encrypted, so the raw-byte check couldn't see them either. Refuse rather than miss them.
    /// </summary>
    static void CheckForUnhandledPokemon(SaveFile sav, List<string> problems)
    {
        if (sav is IDaycareStorage daycare)
        {
            for (int i = 0; i < daycare.DaycareSlotCount; i++)
            {
                if (daycare.IsDaycareOccupied(i))
                    problems.Add($"day care slot {i + 1} is occupied; take the Pokémon out in-game first");
            }
        }

        if (sav is SAV5BW bw)
        {
            for (int i = 0; i < BattleBox5.Count; i++)
            {
                // Empty slots are encrypted too, so they aren't all zeros: decrypt to check.
                if (bw.GetDecryptedPKM(bw.BattleBox.GetSlot(i).ToArray()).Species != 0)
                    problems.Add($"Battle Box slot {i + 1} is used; empty the Battle Box in-game first");
            }
        }
    }

    /// <summary>Renames a Pokémon the player caught (in this game or another of theirs). Others are kept.</summary>
    bool ScrubPokemon(PKM pk, string player, HashSet<uint> oldIds, List<string> report)
    {
        if (pk.Species == 0)
            return false;

        if (pk.OriginalTrainerName != player)
        {
            report.Add($"kept {(Species)pk.Species}: its OT is {pk.OriginalTrainerName}, not the player");
            return false;
        }

        oldIds.Add(pk.ID32);
        var id = MapId(pk.ID32);
        pk.OriginalTrainerName = TrainerName;
        pk.TID16 = (ushort)id;
        pk.SID16 = (ushort)(id >> 16);
        pk.RefreshChecksum();
        if (pk.IsNicknamed)
            report.Add($"kept nickname {(Species)pk.Species} '{pk.Nickname}'");
        return true;
    }

    /// <summary>
    /// Gen 3 records PKHeX doesn't model (TV shows: 36 bytes each) store the player's name followed by
    /// the 16-bit trainer ID only, sometimes several times. Replace the old TID in the rest of the record.
    /// </summary>
    void ReplaceGen3RecordTid(byte[] data, int nameAt, uint playerOldId, List<string> report)
    {
        const int RecordSize = 36;
        var oldTid = (ushort)playerOldId;
        var newTid = (ushort)MapId(playerOldId);
        var end = Math.Min(data.Length - 1, nameAt + RecordSize);
        for (int at = nameAt; at < end; at++)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) != oldTid)
                continue;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), newTid);
            report.Add($"replaced TID {oldTid} at 0x{at:X}, after the name");
            at++;
        }
    }

    void ReplaceLeftovers(SaveFile sav, byte[] data, List<(string Old, string New)> names, HashSet<uint> oldIds,
        uint playerOldId, List<string> report, List<string> problems)
    {
        foreach (var (oldName, newName) in names)
        {
            var needle = Encode(sav, oldName);
            var replacement = Encode(sav, newName);
            foreach (var at in FindAll(data, needle))
            {
                // A longer name only fits if the bytes after the old one are unused padding.
                var extra = data.AsSpan(at + needle.Length, Math.Max(0, replacement.Length - needle.Length));
                if (extra.ContainsAnyExcept((byte)0x00, (byte)0xFF))
                {
                    problems.Add($"'{oldName}' at 0x{at:X} has no room for '{newName}'");
                    continue;
                }
                replacement.CopyTo(data.AsSpan(at));
                report.Add($"replaced '{oldName}' at 0x{at:X} (not covered by PKHeX)");

                if (sav is SAV3)
                    ReplaceGen3RecordTid(data, at, playerOldId, report);
            }
        }

        foreach (var old in oldIds)
        {
            var needle = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(needle, old);
            foreach (var at in FindAll(data, needle))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), MapId(old));
                report.Add($"replaced trainer ID {(ushort)old}/{old >> 16} at 0x{at:X} (not covered by PKHeX)");
            }
        }
    }

    /// <summary>
    /// The games keep a second copy of the save, which PKHeX reads but doesn't rewrite. Overwrite it with
    /// the scrubbed copy, so both hold the same data (the old one would still hold the real name).
    /// </summary>
    static void MirrorBackups(SaveFile sav, byte[] data)
    {
        switch (sav)
        {
            case SAV3 gen3:
                gen3.WriteBothSaveSlots(data);
                break;
            case SAV4 gen4:
                foreach (var (start, size) in Gen4Blocks(gen4))
                    MirrorGen4Block(data, start, size);
                break;
            case SAV5BW:
                // Black/White mirror the first 0x24000 bytes right after them.
                data.AsSpan(0, Gen5BackupOffset).CopyTo(data.AsSpan(Gen5BackupOffset));
                break;
        }
    }

    const int Gen4HalfSize = 0x40000;
    const int Gen5BackupOffset = 0x24000;

    /// <summary>Gen 4 saves have two blocks (general and storage); each has its own copy in each half.</summary>
    static (int Start, int Size)[] Gen4Blocks(SAV4 sav) => sav switch
    {
        SAV4DP => [(0, SAV4DP.GeneralSize), (SAV4DP.GeneralSize, 0x121E0)],
        SAV4Pt => [(0, SAV4Pt.GeneralSize), (SAV4Pt.GeneralSize, 0x121E4)],
        SAV4HGSS => [(0, SAV4HGSS.GeneralSize), (0xF700, 0x12310)],
        _ => throw new NotSupportedException(sav.GetType().Name),
    };

    /// <summary>
    /// Copies the newer copy of a Gen 4 block over the older one. Each copy ends in a footer whose last
    /// 16 bytes are the save counter, the block size, a magic number, the block ID and the checksum.
    /// </summary>
    static void MirrorGen4Block(byte[] data, int start, int size)
    {
        var a = data.AsSpan(start, size);
        var b = data.AsSpan(start + Gen4HalfSize, size);

        // A game saved only once has written one copy; the other is still blank. Leave it blank.
        if (IsBlank(a) || IsBlank(b))
            return;

        foreach (var offset in new[] { start, start + Gen4HalfSize })
        {
            var sizeInFooter = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + size - 12));
            if (sizeInFooter != size)
                throw new InvalidDataException($"Gen 4 block at 0x{offset:X}: footer says size 0x{sizeInFooter:X}, expected 0x{size:X}.");
        }

        var counterA = BinaryPrimitives.ReadUInt32LittleEndian(a[^16..]);
        var counterB = BinaryPrimitives.ReadUInt32LittleEndian(b[^16..]);
        if (counterA == counterB && !a.SequenceEqual(b))
            throw new InvalidDataException($"Gen 4 block at 0x{start:X}: both copies have counter {counterA} but differ.");

        if (counterA >= counterB)
            a.CopyTo(b);
        else
            b.CopyTo(a);
    }

    static bool IsBlank(ReadOnlySpan<byte> copy) => !copy.ContainsAnyExcept((byte)0xFF) || !copy.ContainsAnyExcept((byte)0x00);

    /// <summary>The checks that decide whether the result can be committed.</summary>
    static void Verify(byte[] original, byte[] data, IEnumerable<string> oldNames, HashSet<uint> oldIds, List<string> problems)
    {
        var before = Load(original)!;
        var after = Load(data);
        if (after is null || after.GetType() != before.GetType())
        {
            problems.Add("the scrubbed save doesn't load as the same game");
            return;
        }
        if (!after.ChecksumsValid)
            problems.Add($"checksums are wrong: {after.ChecksumInfo}");

        // Raw bytes: the old names and IDs must be gone everywhere, backups included.
        foreach (var name in oldNames)
        {
            foreach (var at in FindAll(data, Encode(after, name)))
                problems.Add($"'{name}' is still at 0x{at:X}");
        }
        foreach (var old in oldIds)
        {
            var needle = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(needle, old);
            foreach (var at in FindAll(data, needle))
                problems.Add($"trainer ID {(ushort)old}/{old >> 16} is still at 0x{at:X}");
        }

        // Decrypted Pokémon (Gen 4/5 encrypt the OT, so the raw check can't see it): same Pokémon, new OT.
        var oldNameSet = oldNames.ToHashSet();
        var pairs = before.PartyData.Zip(after.PartyData).Concat(before.BoxData.Zip(after.BoxData));
        foreach (var (b, a) in pairs)
        {
            if (b.Species != a.Species || b.PID != a.PID || b.CurrentLevel != a.CurrentLevel
                || b.Nickname != a.Nickname || b.IsShiny != a.IsShiny)
                problems.Add($"{(Species)b.Species} changed beyond its OT");
            if (oldNameSet.Contains(a.OriginalTrainerName) || oldIds.Contains(a.ID32))
                problems.Add($"{(Species)a.Species} still has the old OT");
        }
        if (before.PartyCount != after.PartyCount)
            problems.Add("party size changed");

        // Backups must now be exact copies of the scrubbed data.
        switch (after)
        {
            case SAV4 gen4:
                foreach (var (start, size) in Gen4Blocks(gen4))
                {
                    var a = data.AsSpan(start, size);
                    var b = data.AsSpan(start + Gen4HalfSize, size);
                    if (!a.SequenceEqual(b) && !IsBlank(a) && !IsBlank(b))
                        problems.Add($"Gen 4 block at 0x{start:X} differs from its backup");
                }
                break;
            case SAV5BW:
                if (!data.AsSpan(0, Gen5BackupOffset).SequenceEqual(data.AsSpan(Gen5BackupOffset, Gen5BackupOffset)))
                    problems.Add("Gen 5 data differs from its backup");
                break;
        }
    }

    /// <summary>The name as the game stores it, terminator included.</summary>
    static byte[] Encode(SaveFile sav, string name)
    {
        var buffer = new byte[64];
        var length = sav.SetString(buffer, name, name.Length + 1, StringConverterOption.None);
        return buffer[..length];
    }

    static List<int> FindAll(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
    {
        var hits = new List<int>();
        for (int from = 0; ;)
        {
            var i = data[from..].IndexOf(needle);
            if (i < 0)
                return hits;
            hits.Add(from + i);
            from += i + 1;
        }
    }
}
