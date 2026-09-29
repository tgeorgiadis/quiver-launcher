using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void PortugueseBrazilTranslatesKnownLabels()
    {
        var previous = LocalizationService.Language;
        try
        {
            LocalizationService.SetLanguage(LocalizationService.PortugueseBrazilLanguage);

            Assert.Equal("Configurações", LocalizationService.Translate("Settings"));
            Assert.Equal("Texto que ainda não foi traduzido", LocalizationService.Translate("Texto que ainda não foi traduzido"));
        }
        finally
        {
            LocalizationService.SetLanguage(previous);
        }
    }

    [Fact]
    public void EnglishKeepsSourceText()
    {
        var previous = LocalizationService.Language;
        try
        {
            LocalizationService.SetLanguage(LocalizationService.EnglishLanguage);

            Assert.Equal("Settings", LocalizationService.Translate("Settings"));
        }
        finally
        {
            LocalizationService.SetLanguage(previous);
        }
    }

    [Fact]
    public void UnsupportedLanguageFallsBackToSystem()
    {
        var previous = LocalizationService.Language;
        try
        {
            LocalizationService.SetLanguage("fr-FR");

            Assert.Equal(LocalizationService.SystemLanguage, LocalizationService.Language);
        }
        finally
        {
            LocalizationService.SetLanguage(previous);
        }
    }
}
