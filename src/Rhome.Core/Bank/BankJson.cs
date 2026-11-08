using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rhome.Core;

/// <summary>
/// The bank's JSON format, used both for the bank file on the device and for the export file
/// (PLAN.md §4). Snapshot bytes are base64. Games are PKHeX's <c>GameVersion</c> numbers, which are the
/// games' own version IDs and so never change.
/// </summary>
/// <remarks>
/// Every file carries a format name and version. A file from a newer rhome, or one that isn't a bank
/// at all, is refused rather than half-read: dropping fields we don't understand and writing the bank
/// back would lose data.
/// </remarks>
public static class BankJson
{
    /// <summary>The <c>format</c> value every bank file starts with.</summary>
    public const string FormatName = "rhome-bank";

    /// <summary>The version this build writes, and the newest it reads.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Writes the bank as JSON. Refuses a bank that breaks its own rules (<see cref="Bank.Problems"/>).</summary>
    public static void Write(Bank bank, Stream to)
    {
        var problems = bank.Problems();
        if (problems.Count > 0)
            throw new InvalidOperationException("Refusing to write a broken bank: " + string.Join("; ", problems));

        var document = new BankDocument(FormatName, CurrentVersion, bank.Boxes, bank.Saves, bank.Pokemon);
        JsonSerializer.Serialize(to, document, BankJsonContext.Default.BankDocument);
    }

    /// <summary>The JSON as a byte array, as <see cref="Write(Bank, Stream)"/> would write it.</summary>
    public static byte[] ToBytes(Bank bank)
    {
        using var buffer = new MemoryStream();
        Write(bank, buffer);
        return buffer.ToArray();
    }

    /// <summary>Reads a bank written by this or an older rhome.</summary>
    /// <exception cref="BankFormatException">The data isn't a bank, comes from a newer rhome, or is damaged.</exception>
    public static Bank Read(Stream from)
    {
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(from);
        }
        catch (JsonException e)
        {
            throw new BankFormatException("This isn't a bank file: it isn't valid JSON.", e);
        }

        using (json)
        {
            CheckHeader(json.RootElement);

            BankDocument? document;
            try
            {
                document = json.RootElement.Deserialize(BankJsonContext.Default.BankDocument);
            }
            catch (JsonException e)
            {
                throw new BankFormatException($"The bank file is damaged: {e.Message}", e);
            }
            if (document is null)
                throw new BankFormatException("The bank file is empty.");

            var bank = new Bank { Boxes = document.Boxes, Saves = document.Saves, Pokemon = document.Pokemon };
            var problems = bank.Problems();
            if (problems.Count > 0)
                throw new BankFormatException("The bank file is damaged: " + string.Join("; ", problems));
            return bank;
        }
    }

    static void CheckHeader(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format)
            || format.ValueKind != JsonValueKind.String
            || format.GetString() != FormatName)
            throw new BankFormatException("This isn't a bank file.");

        if (!root.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || !versionElement.TryGetInt32(out var version)
            || version < 1)
            throw new BankFormatException("The bank file has no valid format version.");

        if (version > CurrentVersion)
            throw new BankFormatException(
                $"This bank was written by a newer rhome (format version {version}; this one reads up to {CurrentVersion}). Update rhome to open it.");
    }
}

/// <summary>Data that can't be read as a bank. Nothing is changed when this is thrown.</summary>
public sealed class BankFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The bank file's top level: a header, then the bank.</summary>
sealed record BankDocument(
    string Format,
    int Version,
    BoxGroup Boxes,
    List<BankSave> Saves,
    List<BankPokemon> Pokemon);

// Source-generated, so reading and writing need no reflection (safe for trimmed and NativeAOT builds).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(BankDocument))]
partial class BankJsonContext : JsonSerializerContext;
