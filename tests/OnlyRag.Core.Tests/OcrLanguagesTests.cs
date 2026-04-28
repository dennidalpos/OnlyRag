using OnlyRag.Core;

namespace OnlyRag.Core.Tests;

public sealed class OcrLanguagesTests
{
    [Theory]
    [InlineData(null, "it")]
    [InlineData("", "it")]
    [InlineData("  ", "it")]
    [InlineData("fr", "fr")]
    [InlineData("japan", "japan")]
    [InlineData("not-supported", "it")]
    public void NormalizeCode_ReturnsSupportedLanguageOrDefault(string? input, string expected)
    {
        Assert.Equal(expected, OcrLanguages.NormalizeCode(input));
    }

    [Fact]
    public void All_ContainsDefaultItalianAndAdvancedLanguages()
    {
        Assert.Contains(OcrLanguages.All, language => language.Code == "it" && language.IsDefault);
        Assert.Contains(OcrLanguages.All, language => language.Code == "tab" && language.ScriptGroup == "Avanzate");
    }
}
