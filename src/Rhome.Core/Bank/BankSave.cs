using System.Text.Json.Serialization;
using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// A save the bank has seen: which game and whose. Deliberately has no location: see the remarks on
/// <see cref="Bank"/>.
/// </summary>
/// <param name="Id">The bank's own ID for this save. The platform layer maps it to a file on the device.</param>
/// <param name="Game">The game, as precisely as the save tells (see <see cref="DetectedSave.Game"/>).</param>
/// <param name="Trainer">The save's trainer.</param>
/// <param name="DisplayName">What the user calls this save, e.g. "Emerald (Thor)".</param>
/// <param name="LastSeenHash">SHA-256 of the save's bytes the last time rhome read or wrote it, in hex, so a save
/// changed behind rhome's back can be noticed. Null until rhome has read it.</param>
public sealed record BankSave(
    Guid Id,
    GameVersion Game,
    TrainerInfo Trainer,
    string DisplayName,
    string? LastSeenHash);

/// <summary>A trainer as the games identify one: name plus the 16-bit trainer ID and secret ID.</summary>
public sealed record TrainerInfo(
    string Name,
    [property: JsonPropertyName("tid")] ushort TID16,
    [property: JsonPropertyName("sid")] ushort SID16);
