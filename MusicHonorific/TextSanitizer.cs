using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AnyAscii;

namespace MusicHonorific;

/// <summary>
/// Converts arbitrary track metadata into text the FFXIV client font can actually render.
/// The game font (Axis) only has glyphs for ASCII, most Latin-1 accented letters and a few
/// symbols (including the musical note). Anything else - Cyrillic ("KoЯn"), Japanese, emoji,
/// smart quotes, em dashes - shows up as boxes or question marks. We transliterate those to
/// the supported set instead of changing any encoding (the encoding was never the problem).
/// </summary>
public static class TextSanitizer
{
    // Characters the game can render that AnyAscii would otherwise strip or change.
    private const char MusicNote = '\u266a'; // ♪

    /// <summary>
    /// Known stylized spellings where a Latin lookalike glyph is used for visual effect rather
    /// than its phonetic value (e.g. "KoЯn" uses a mirrored R, not the Russian "Ya" sound).
    /// Phonetic transliteration would turn these into nonsense ("KoYan"), so we substitute the
    /// intended Latin form for whole-word matches only - this never touches genuine Cyrillic text.
    /// Keys are compared case-insensitively.
    /// </summary>
    private static readonly Dictionary<string, string> StylizedOverrides = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Ko\u042Fn"] = "Korn",   // KoЯn -> Korn (Я is a mirrored R here, not "Ya")
    };

    /// <summary>
    /// Returns a render-safe version of <paramref name="input"/>: known stylized names are
    /// substituted, common punctuation is normalised, glyphs the game already supports are kept,
    /// and everything else is transliterated to ASCII via AnyAscii. Surrogate pairs are handled safely.
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        input = ApplyStylizedOverrides(input);

        var sb = new StringBuilder(input.Length);

        var enumerator = StringInfo.GetTextElementEnumerator(input);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;

            // Fast path: a single char the game can already render.
            if (element.Length == 1)
            {
                var c = element[0];
                if (c == MusicNote || IsGameRenderable(c))
                {
                    sb.Append(MapPunctuation(c));
                    continue;
                }
            }
            else
            {
                // Multi-char element (surrogate pair, combining sequence). If, after dropping
                // combining marks, it collapses to renderable Latin-1, keep that.
                var stripped = StripCombiningMarks(element);
                if (stripped.Length == 1 && IsGameRenderable(stripped[0]))
                {
                    sb.Append(stripped[0]);
                    continue;
                }
            }

            // Fall back to transliteration for anything the font can't show.
            var ascii = element.Transliterate();
            if (!string.IsNullOrEmpty(ascii))
                sb.Append(ascii);
        }

        return CollapseWhitespace(sb.ToString());
    }

    /// <summary>
    /// True for characters the game nameplate font can render. The FFXIV font includes the full
    /// Latin-1 range plus Japanese (kana + JIS kanji), since it is a Japanese game. Simplified-only
    /// Chinese hanzi, Korean, Cyrillic, emoji, etc. are NOT included and fall through to romanization.
    /// </summary>
    private static bool IsGameRenderable(char c)
    {
        if (c is '\t' or ' ') return true;
        if (char.IsControl(c)) return false;
        // Basic Latin and Latin-1 Supplement printable ranges.
        if (c is >= '\u0020' and <= '\u007E'    // ASCII printable
              or >= '\u00A1' and <= '\u00FF')   // Latin-1 accented letters & symbols
            return true;
        // Japanese scripts the game font ships natively.
        return c is >= '\u3000' and <= '\u303F'    // CJK symbols & punctuation (、。「」《》)
                 or >= '\u3040' and <= '\u309F'    // Hiragana
                 or >= '\u30A0' and <= '\u30FF'    // Katakana
                 or >= '\u4E00' and <= '\u9FFF'    // CJK Unified Ideographs (kanji)
                 or >= '\uFF00' and <= '\uFFEF';   // Full-width / half-width forms
    }

    /// <summary>
    /// Replaces known stylized spellings (whole-word matches) with their intended Latin form
    /// before transliteration runs, so phonetic mapping can't mangle them.
    /// </summary>
    private static string ApplyStylizedOverrides(string input)
    {
        foreach (var (stylized, replacement) in StylizedOverrides)
            input = Regex.Replace(input, Regex.Escape(stylized), replacement, RegexOptions.IgnoreCase);
        return input;
    }

    /// <summary>Replaces typographic punctuation that has no game glyph with plain ASCII.</summary>
    private static string MapPunctuation(char c) => c switch
    {
        '\u2013' or '\u2014' or '\u2015' => "-",          // – — ―
        '\u2018' or '\u2019' or '\u201B' => "'",          // ' ' ‛
        '\u201C' or '\u201D' or '\u201F' => "\"",         // " " ‟
        '\u2026' => "...",                                  // …
        '\u00A0' => " ",                                    // non-breaking space
        _ => c.ToString(),
    };

    private static string StripCombiningMarks(string s)
    {
        var n = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var c in n)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var c in s)
        {
            var isSpace = c == ' ';
            if (isSpace && lastWasSpace) continue;
            sb.Append(c);
            lastWasSpace = isSpace;
        }
        return sb.ToString().Trim();
    }
}
