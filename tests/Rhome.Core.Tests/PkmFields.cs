using System.Reflection;
using PKHeX.Core;

namespace Rhome.Core.Tests;

/// <summary>
/// Every simple field PKHeX exposes on a Pokémon (numbers, flags, enums, text), read through its public
/// properties. Used to compare two Pokémon field by field, and for the native diff (PLAN.md §6.4).
/// </summary>
static class PkmFields
{
    // Derived or bookkeeping properties: they follow from other fields, or describe the file rather than
    // the Pokémon, so a difference in them says nothing new.
    static readonly HashSet<string> Skipped =
    [
        "Checksum", "Sanity", "ChecksumValid", "Valid", "EncryptionConstant", "Identifier", "Extension",
        "PartyStatsPresent", "Context", "Format", "SIZE_PARTY", "SIZE_STORED", "TrashCharCountTrainer",
        "TrashCharCountNickname", "FlagHasSpecies", "SpeciesInternal", "PersonalInfo", "FileName",
        "FileNameWithoutExtension",
    ];

    public static IReadOnlyDictionary<string, object?> Of(PKM pk)
    {
        var fields = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in pk.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || Skipped.Contains(property.Name) || !IsSimple(property.PropertyType))
                continue;
            object? value;
            try
            {
                value = property.GetValue(pk);
            }
            catch (TargetInvocationException)
            {
                continue; // a few properties throw for formats they don't apply to
            }
            fields[property.Name] = value;
        }
        return fields;
    }

    /// <summary>"Name: ours ≠ theirs" for every field the two Pokémon both have and disagree on.</summary>
    public static IReadOnlyList<string> Differences(PKM ours, PKM theirs)
    {
        var a = Of(ours);
        var b = Of(theirs);
        return a.Keys.Intersect(b.Keys)
            .Where(k => !Equals(a[k], b[k]))
            .Select(k => $"{k}: {a[k]} ≠ {b[k]}")
            .ToList();
    }

    /// <summary>
    /// The native diff: fields that are zero, false or empty on ours but set on a Pokémon the game made
    /// itself. These are the "constructor default" bugs (PLAN.md §1, lesson 3).
    /// </summary>
    public static IReadOnlyList<string> UnsetOnOursButSetByTheGame(PKM ours, PKM native)
    {
        var a = Of(ours);
        var b = Of(native);
        return a.Keys.Intersect(b.Keys)
            .Where(k => IsUnset(a[k]) && !IsUnset(b[k]))
            .Select(k => $"{k} (game has {b[k]})")
            .ToList();
    }

    static bool IsUnset(object? value) => value switch
    {
        null => true,
        bool b => !b,
        string s => s.Length == 0,
        Enum e => System.Convert.ToInt64(e) == 0,
        IConvertible c => c.ToDecimal(null) == 0,
        _ => false,
    };

    static bool IsSimple(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal);
}
