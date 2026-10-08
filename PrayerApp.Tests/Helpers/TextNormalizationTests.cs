using PrayerApp.Helpers;

namespace PrayerApp.Tests.Helpers;

public class TextNormalizationTests
{
    [Theory]
    [InlineData("a  b", "a b")]
    [InlineData("a   b    c", "a b c")]
    [InlineData("a\tb", "a b")]
    [InlineData("a\nb", "a b")]
    [InlineData("a \t\r\n b", "a b")]
    [InlineData("  a b", "a b")]
    [InlineData("a b  ", "a b")]
    [InlineData(" \t\na b\r\n ", "a b")]
    [InlineData("a b", "a b")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void CollapseWhitespace_CollapsesRunsAndTrimsEnds(string text, string expected)
    {
        Assert.Equal(expected, TextNormalization.CollapseWhitespace(text));
    }
}
