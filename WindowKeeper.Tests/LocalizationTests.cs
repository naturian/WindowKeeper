using WindowKeeper;
using Xunit;

namespace WindowKeeper.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void GermanStringsAreReturnedWhenInitializedWithGerman()
    {
        try
        {
            Loc.Initialize("de");
            Assert.Equal("Speichern", Loc.T("Common.Save"));
            Assert.Equal("Einstellungen…", Loc.T("Tray.Settings"));
        }
        finally
        {
            Loc.Initialize("en");
        }
    }

    [Fact]
    public void EnglishIsTheDefaultForUnknownLanguages()
    {
        try
        {
            Loc.Initialize("fr");
            Assert.Equal("Save", Loc.T("Common.Save"));
        }
        finally
        {
            Loc.Initialize("en");
        }
    }

    [Fact]
    public void UnknownKeysFallBackToTheKeyItself()
    {
        Loc.Initialize("en");
        Assert.Equal("Does.Not.Exist", Loc.T("Does.Not.Exist"));
    }

    [Fact]
    public void FormatInsertsArguments()
    {
        try
        {
            Loc.Initialize("de");
            Assert.Equal("Version 9.9.9 ist verfügbar.", Loc.F("About.UpdateAvailable", "9.9.9"));
        }
        finally
        {
            Loc.Initialize("en");
        }
    }

    [Fact]
    public void SettingsNormalizeClampsLanguageToKnownValues()
    {
        var settings = new Settings { Language = "zz" };
        settings.Normalize();
        Assert.Equal("auto", settings.Language);

        settings.Language = "de";
        settings.Normalize();
        Assert.Equal("de", settings.Language);
    }
}
