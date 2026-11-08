using PKHeX.Core;

namespace Rhome.Core;

/// <summary>Fitting names from a newer game into an older game's character set.</summary>
static class GameText
{
    /// <summary>What the older games show in place of a character they have no glyph for.</summary>
    public const char Placeholder = '?';

    /// <summary>
    /// The text cut to <paramref name="maxLength"/>, with every character <paramref name="canShow"/> rejects
    /// replaced by <see cref="Placeholder"/>. <paramref name="replaced"/> lists the characters that were swapped.
    /// </summary>
    public static string Fit(string text, int maxLength, Func<char, bool> canShow, out string replaced)
    {
        var fitted = new char[Math.Min(text.Length, maxLength)];
        var missing = new List<char>();
        for (var i = 0; i < fitted.Length; i++)
        {
            var c = text[i];
            if (canShow(c))
            {
                fitted[i] = c;
                continue;
            }
            fitted[i] = Placeholder;
            if (!missing.Contains(c))
                missing.Add(c);
        }
        replaced = new string([.. missing]);
        return new string(fitted);
    }

    /// <summary>The characters, quoted for a note: 'a', 'b'.</summary>
    public static string Quote(string characters) => string.Join(", ", characters.Select(c => $"'{c}'"));

    /// <summary>True when a GBA game has a glyph for <paramref name="c"/> in this language.</summary>
    public static bool Gen3CanShow(char c, int language)
    {
        // SetString stops at the first character it can't encode. A lone character that encodes leaves it
        // plus the terminator, so a count of 2 means the game has a glyph for it.
        Span<byte> buffer = stackalloc byte[2];
        return StringConverter3.SetString(buffer, [c], 1, language) == 2;
    }

    /// <summary>True when a DS Gen 4 game has a glyph for <paramref name="c"/> in this language.</summary>
    public static bool Gen4CanShow(char c, int language)
    {
        // Gen 4 writes a stand-in for a character it lacks, so encode it and see whether it reads back the same.
        Span<byte> buffer = stackalloc byte[4];
        StringConverter4.SetString(buffer, [c], 1, language);
        return StringConverter4.GetString(buffer) == c.ToString();
    }

    /// <summary>Writes a name the way the GBA games store it: the characters, a terminator, then 0xFF to the end.</summary>
    public static void WriteGen3(Span<byte> field, string text, int language)
    {
        field.Fill(0xFF);
        StringConverter3.SetString(field, text, field.Length, language, StringConverterOption.ClearFF);
    }
}
