using System.Linq;
using System.Text.RegularExpressions;
using PrayerApp.Helpers;
using PrayerApp.Models;
using static PrayerApp.Helpers.TextNormalization;

namespace PrayerApp.Services;

public class TextSelectionParser : ITextSelectionParser
{
    private static readonly Regex LineSplitter =
        new(@"\r\n|\r|\n", RegexOptions.Compiled);

    // Splits on a run of one-or-more blank lines (any line-ending). A "blank
    // line" here is a line-ending followed by optional whitespace and at
    // least one more line-ending — i.e. an empty line between two content
    // lines. Used to detect blank-line-separated prayer blocks (option d,
    // 2026-05-10).
    private static readonly Regex BlockSplitter =
        new(@"(?:\r\n|\r|\n)[ \t]*(?:\r\n|\r|\n)+", RegexOptions.Compiled);

    private static readonly Regex MarkerStripper =
        new(@"^\s*(?:\d+[\.\)]|[-*•])\s*", RegexOptions.Compiled);

    // Threshold for the option-d line-2 sentence-shape heuristic. A line
    // with > this many space-separated words (after marker stripping) is
    // treated as "sentence-shaped" even without a clause delimiter. 6
    // matches the BuildPrayer rule-6 threshold for long-line title
    // synthesis.
    private const int SentenceShapeWordThreshold = 6;

    private readonly Func<DateTime> _now;

    public TextSelectionParser() : this(() => DateTime.Now) { }

    public TextSelectionParser(Func<DateTime> now)
    {
        _now = now;
    }

    public ParseResult Parse(string rawText)
    {
        var defaultTitle = $"Imported {_now():MMM d}";
        var normalized = TextNormalization.NormalizeQuotes(rawText) ?? string.Empty;

        // Split on blank-line runs into prayer blocks. Within a block, lines
        // are still split by single line-endings. Header extraction runs
        // on block 0 only; subsequent blocks each become one or more
        // prayers per the sentence-shape heuristic (option d, 2026-05-10).
        var blocks = BlockSplitter.Split(normalized);

        string? headerTitle = null;
        var entries = new List<ParsedPrayer>();

        for (int b = 0; b < blocks.Length; b++)
        {
            var blockLines = LineSplitter.Split(blocks[b]);
            int firstContentLine = 0;

            if (b == 0)
            {
                // When subsequent blocks exist, content is guaranteed to follow
                // even if block 0 is header-only ("MISSIONS OUTREACH\n\nPray for staff.").
                var hasFollowingBlocks = blocks.Length > 1;
                var (extracted, linesToSkip) = ExtractHeaderTitle(blockLines, hasFollowingBlocks);
                headerTitle = extracted;
                firstContentLine = linesToSkip;
            }

            // Strip markers, drop empties. Track per-line marker presence so the
            // fold gate below can distinguish list lines from prose.
            var contentLines = new List<(string Content, bool HadMarker)>(blockLines.Length);
            for (int i = firstContentLine; i < blockLines.Length; i++)
            {
                var raw = blockLines[i];
                var match = MarkerStripper.Match(raw);
                var content = CollapseWhitespace(match.Success ? raw[match.Length..] : raw);
                if (content.Length == 0) continue;
                contentLines.Add((content, match.Success));
            }

            if (contentLines.Count == 0) continue;

            // Option-d fold (2026-05-10): line 2 sentence-shaped AND no marker
            // on line 2 → fold to title + joined details. Marker on line 2
            // forces per-line; markers on line 3+ are ambiguous (sub-asks
            // under a title) and do NOT veto the fold.
            if (contentLines.Count >= 2 && IsSentenceShaped(contentLines[1].Content) && !contentLines[1].HadMarker)
            {
                var details = string.Join('\n', contentLines.GetRange(1, contentLines.Count - 1).Select(l => l.Content));
                entries.Add(new ParsedPrayer(CapTitle(contentLines[0].Content), details));
            }
            else
            {
                foreach (var line in contentLines)
                    entries.Add(BuildPrayer(line.Content));
            }
        }

        if (entries.Count == 0 && !string.IsNullOrWhiteSpace(normalized))
        {
            entries.Add(BuildPrayer(CollapseWhitespace(normalized)));
        }

        return new ParseResult(entries.AsReadOnly(), headerTitle ?? defaultTitle);
    }

    /// <summary>
    /// A line is "sentence-shaped" when it contains a clause delimiter
    /// (`,` `;` `:` `—` `-`) OR has more than <see cref="SentenceShapeWordThreshold"/>
    /// space-separated words. Used by the option-d block fold (2026-05-10)
    /// to decide whether a 2-line block reads as name + details (fold) or
    /// as a flat list (per-line).
    /// </summary>
    private static bool IsSentenceShaped(string line)
    {
        if (line.IndexOfAny(ClauseDelimiters) >= 0) return true;
        var wordCount = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return wordCount > SentenceShapeWordThreshold;
    }

    // Header-detection thresholds. Tuned for the corporate-prayer-document
    // shape ("CORPORATE PRAYER" / "MUSSONS OUTREACH" → "Pray for...");
    // adjust if real imports surface false positives or negatives.
    private const int MinHeaderLetters = 3;
    private const int MaxHeaderLength = 60;
    private const double UppercaseRatioThreshold = 0.8;

    /// <summary>
    /// Scans the leading lines for an ALL-CAPS topic header. Multiple
    /// consecutive headers collapse to the LAST one — when a corporate
    /// prayer document has both a section header and a topic header
    /// ("CORPORATE PRAYER" / "MUSSONS OUTREACH"), the topic line wins.
    /// A content line (non-blank, non-header) must follow the header
    /// candidate(s); without that signal an ALL-CAPS list
    /// ("MOM\nDAD\nSISTER") would lose its first item. When
    /// <paramref name="contentFollowsExternally"/> is true, the caller has
    /// already verified that content exists outside this block (in a
    /// later block), so the within-block content check is bypassed.
    /// Returns the title-cased header (or null) plus the count of leading
    /// lines to skip during prayer extraction.
    /// </summary>
    private static (string? Title, int LinesToSkip) ExtractHeaderTitle(
        string[] lines, bool contentFollowsExternally = false)
    {
        int headerEnd = -1;
        string? headerTrimmed = null;
        bool sawContent = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0) continue;

            if (IsHeaderLine(trimmed))
            {
                headerEnd = i;
                headerTrimmed = trimmed;
            }
            else
            {
                sawContent = true;
                break;
            }
        }

        return headerEnd < 0 || (!sawContent && !contentFollowsExternally)
            ? (null, 0)
            : (TitleCase(headerTrimmed!), headerEnd + 1);
    }

    private static bool IsHeaderLine(string trimmed)
    {
        if (trimmed.Length < MinHeaderLetters || trimmed.Length > MaxHeaderLength) return false;
        if (MarkerStripper.IsMatch(trimmed)) return false;

        int letters = 0, upper = 0;
        foreach (var c in trimmed)
        {
            if (char.IsLetter(c))
            {
                letters++;
                if (char.IsUpper(c)) upper++;
            }
        }
        return letters >= MinHeaderLetters
            && (double)upper / letters >= UppercaseRatioThreshold;
    }

    // Tokens of <= this length in an ALL-CAPS header are treated as
    // acronyms and preserved as-is (NPM, FBC, USA). 4+ overlaps with
    // common short words (BABY, GIFT) and would mis-preserve them.
    private const int MaxAcronymLength = 3;

    private static string TitleCase(string s)
    {
        var titled = System.Globalization.CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(s.ToLowerInvariant());

        var sourceTokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var titledTokens = titled.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (sourceTokens.Length != titledTokens.Length) return titled;

        for (int i = 0; i < sourceTokens.Length; i++)
        {
            if (sourceTokens[i].Length <= MaxAcronymLength && IsAllUpperLetters(sourceTokens[i]))
                titledTokens[i] = sourceTokens[i];
        }
        return string.Join(' ', titledTokens);
    }

    private static bool IsAllUpperLetters(string token)
    {
        if (token.Length == 0) return false;
        foreach (var c in token)
            if (!char.IsLetter(c) || !char.IsUpper(c)) return false;
        return true;
    }

    // Includes `-` per architecture doc rule 6, but plain hyphens appear in
    // compound words ("father-in-law") and dates ("2026-05-01") — those will
    // currently false-split. Tighten to ` - ` (space-flanked) if real imports
    // surface the issue.
    private static readonly char[] ClauseDelimiters = { ',', ';', ':', '—', '-' };

    // Rule 6: split on first clause delimiter, or take first 5 words if >6.
    // Rule 7 (100-char title cap) still applies on top.
    private static ParsedPrayer BuildPrayer(string text)
    {
        var delimiterIdx = text.IndexOfAny(ClauseDelimiters);
        if (delimiterIdx > 0)
            return new ParsedPrayer(CapTitle(text[..delimiterIdx].TrimEnd()), text);

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 6)
            return new ParsedPrayer(CapTitle(string.Join(' ', words.Take(5))), text);

        return text.Length <= Prayer.TitleMaxLength
            ? new ParsedPrayer(text, null)
            : new ParsedPrayer(CapTitle(text), text[Prayer.TitleMaxLength..]);
    }

    private static string CapTitle(string title) =>
        title.Length <= Prayer.TitleMaxLength ? title : title[..Prayer.TitleMaxLength];
}
