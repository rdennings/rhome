using System.Diagnostics.CodeAnalysis;
using PKHeX.Core;

namespace Rhome.Core;

/// <summary>What a conversion did to a field that the target format can't hold as it was.</summary>
public enum ChangeKind
{
    /// <summary>Left out: the target format has no place for it (a Gen 4 move in Gen 3, a ribbon, a held item).</summary>
    Dropped,

    /// <summary>Swapped for the nearest thing the target format has (a Dusk Ball becomes a Poké Ball).</summary>
    Replaced,
}

/// <summary>The part of a Pokémon a <see cref="ConversionNote"/> is about.</summary>
public enum ChangedField
{
    Moves,
    HeldItem,
    Ball,
    Ability,
    Form,
    Nickname,
    TrainerName,
    MetData,
    Ribbons,

    /// <summary>The PID. Only a Gen 5 Pokémon going to Gen 4 or 3 gets a new one, to keep its nature.</summary>
    PersonalityValue,
    Other,
}

/// <summary>
/// One thing a conversion couldn't carry over as it was. Every note is shown to the user before anything
/// is written (PLAN.md §2: "a summary of what will happen, including anything dropped or replaced").
/// </summary>
/// <param name="Message">Plain English for the pending-changes summary, e.g. "Dropped the move Aura Sphere (not in Gen 3)."</param>
public sealed record ConversionNote(ChangeKind Kind, ChangedField Field, string Message)
{
    public override string ToString() => Message;
}

/// <summary>
/// The outcome of converting a Pokémon to another format: the converted Pokémon and notes on everything that
/// changed on the way, or the reason it can't be converted at all. A refusal is never partial: either the
/// Pokémon can exist in the target format, or nothing is produced.
/// </summary>
public sealed class ConversionResult<T> where T : PKM
{
    ConversionResult(T? pokemon, string? refusal, IReadOnlyList<ConversionNote> notes)
    {
        Pokemon = pokemon;
        Refusal = refusal;
        Notes = notes;
    }

    /// <summary>The converted Pokémon, or null when the conversion was refused.</summary>
    public T? Pokemon { get; }

    /// <summary>Why the Pokémon can't be converted, in plain English, or null when it was.</summary>
    public string? Refusal { get; }

    /// <summary>Everything that was dropped or replaced. Empty for a refusal.</summary>
    public IReadOnlyList<ConversionNote> Notes { get; }

    [MemberNotNullWhen(true, nameof(Pokemon))]
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool Succeeded => Pokemon is not null;

    public static ConversionResult<T> Converted(T pokemon, IReadOnlyList<ConversionNote> notes) => new(pokemon, null, notes);

    public static ConversionResult<T> Refused(string reason) => new(null, reason, []);
}
