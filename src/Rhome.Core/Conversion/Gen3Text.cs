using PKHeX.Core;

namespace Rhome.Core;

/// <summary>Fitting names from a newer game into the GBA games' character set.</summary>
static class Gen3Text
{
    /// <summary>What a Gen 3 game shows in place of a character it has no glyph for.</summary>
    public const char Placeholder = '?';

    /// <summary>
    /// The text with every character Gen 3 can't show replaced by <see cref="Placeholder"/>, cut to
    /// <paramref name="maxLength"/>. <paramref name="replaced"/> lists the characters that were swapped.
    /// </summary>
    public static string Fit(string text, int language, int maxLength, out string replaced)
    {
        var fitted = new char[Math.Min(text.Length, maxLength)];
        var missing = new List<char>();
        Span<byte> buffer = stackalloc byte[2];
        for (var i = 0; i < fitted.Length; i++)
        {
            var c = text[i];
            // SetString stops at the first character it can't encode; a lone character that encodes leaves
            // it plus the terminator, so a count of 2 means the game has a glyph for it.
            if (StringConverter3.SetString(buffer, [c], 1, language) == 2)
            {
                fitted[i] = c;
            }
            else
            {
                fitted[i] = Placeholder;
                if (!missing.Contains(c))
                    missing.Add(c);
            }
        }
        replaced = new string([.. missing]);
        return new string(fitted);
    }

    /// <summary>Writes a name the way the GBA games store it: the characters, a terminator, then 0xFF to the end.</summary>
    public static void Write(Span<byte> field, string text, int language)
    {
        field.Fill(0xFF);
        StringConverter3.SetString(field, text, field.Length, language, StringConverterOption.ClearFF);
    }
}
