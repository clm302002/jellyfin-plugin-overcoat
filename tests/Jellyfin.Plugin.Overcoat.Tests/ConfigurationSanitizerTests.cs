using Jellyfin.Plugin.Overcoat.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Overcoat.Tests;

/// <summary>
/// The settings page enforces its limits in the browser only, and the same values are reachable from
/// the preview endpoint and a hand-edited XML file. These pin the server-side clamp that everything
/// downstream relies on.
/// </summary>
public sealed class ConfigurationSanitizerTests
{
    [Fact]
    public void AbsurdValues_AreClampedIntoRange()
    {
        var c = new PluginConfiguration
        {
            BannerFontScale = 500,
            GlassBlur = 9999,
            NeonGlow = -40,
            BadgeScale = -5,
            BadgeGapPercent = 1000,
            WatchHistoryDays = 0,
            WatchHistoryMaxScan = 1,
            ScheduleHour = 99,
            ScheduleMinute = -3,
        };

        ConfigurationSanitizer.Normalize(c);

        Assert.InRange(c.BannerFontScale, 0.2, 3.0);
        Assert.InRange(c.GlassBlur, 0, 100);
        Assert.InRange(c.NeonGlow, 0, 100);
        Assert.InRange(c.BadgeScale, 10, 300);
        Assert.InRange(c.BadgeGapPercent, 0, 50);
        Assert.InRange(c.WatchHistoryDays, 1, 3650);
        Assert.InRange(c.WatchHistoryMaxScan, 500, 1_000_000);
        Assert.InRange(c.ScheduleHour, 0, 23);
        Assert.InRange(c.ScheduleMinute, 0, 59);
    }

    [Fact]
    public void ValidValues_AreLeftAlone()
    {
        var c = new PluginConfiguration { BannerFontScale = 1.2, GlassBlur = 40, BadgeScale = 120, ScheduleHour = 4, ScheduleMinute = 15 };
        ConfigurationSanitizer.Normalize(c);

        Assert.Equal(1.2, c.BannerFontScale);
        Assert.Equal(40, c.GlassBlur);
        Assert.Equal(120, c.BadgeScale);
        Assert.Equal(4, c.ScheduleHour);
        Assert.Equal(15, c.ScheduleMinute);
    }

    [Fact]
    public void NonFiniteFontScale_DoesNotSurvive()
    {
        // double.NaN passes a naive range check, then poisons every size computed from it.
        var c = new PluginConfiguration { BannerFontScale = double.NaN };
        ConfigurationSanitizer.Normalize(c);
        Assert.InRange(c.BannerFontScale, 0.2, 3.0);
    }

    [Theory]
    [InlineData("#fff", "#fff")]
    [InlineData("#5EBD3E", "#5EBD3E")]
    [InlineData("#FF5EBD3E", "#FF5EBD3E")]
    [InlineData("  #5EBD3E  ", "#5EBD3E")]
    public void ValidColours_AreAccepted(string input, string expected)
        => Assert.Equal(expected, ConfigurationSanitizer.SafeColor(input, "#000000"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    [InlineData("5EBD3E")]      // missing #
    [InlineData("#GGGGGG")]     // not hex
    [InlineData("#12345")]      // wrong length
    [InlineData("#<script>")]
    [InlineData(null)]
    public void InvalidColours_FallBackToTheDefault(string? input)
        => Assert.Equal("#000000", ConfigurationSanitizer.SafeColor(input, "#000000"));

    [Fact]
    public void OverlongLabel_IsTruncated_NotPassedToTheRenderer()
    {
        var label = ConfigurationSanitizer.SafeLabel(new string('X', 500), "NEW");
        Assert.Equal(40, label.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyLabel_FallsBackToTheDefault(string? input)
        => Assert.Equal("NEW", ConfigurationSanitizer.SafeLabel(input, "NEW"));

    [Fact]
    public void NormalizeIsIdempotent()
    {
        var c = new PluginConfiguration { BannerFontScale = 900, ColorNew = "nonsense" };
        ConfigurationSanitizer.Normalize(c);
        var scale = c.BannerFontScale;
        var colour = c.ColorNew;

        ConfigurationSanitizer.Normalize(c);

        Assert.Equal(scale, c.BannerFontScale);
        Assert.Equal(colour, c.ColorNew);
    }

    [Theory]
    [InlineData("day", "day")]
    [InlineData("week", "week")]
    [InlineData("month", "month")]
    [InlineData("year", "week")]
    [InlineData(null, "week")]
    public void TrendingWindow_IsLimitedToImplementedChoices(string? input, string expected)
    {
        var c = new PluginConfiguration { TrendingTimeWindow = input! };
        ConfigurationSanitizer.Normalize(c);
        Assert.Equal(expected, c.TrendingTimeWindow);
    }

    [Fact]
    public void WideCardOverrides_AreClampedLikePosterFields()
    {
        // The wide-card overrides reach the same renderer via the preview endpoint and hand-edited XML,
        // so they need the same clamps.
        var c = new PluginConfiguration
        {
            WideCard = new WideCardStyle
            {
                BannerFontScale = 500,
                BannerShadowStrength = -20,
                GlassTintStrength = 9999,
                GlassBlur = -1,
                NeonGlow = 500,
                BadgeScale = -5,
                BadgeGapPercent = 1000,
                GlassTint = "not-a-colour",
            },
        };

        ConfigurationSanitizer.Normalize(c);

        Assert.InRange(c.WideCard.BannerFontScale, 0.2, 3.0);
        Assert.InRange(c.WideCard.BannerShadowStrength, 0, 100);
        Assert.InRange(c.WideCard.GlassTintStrength, 0, 100);
        Assert.InRange(c.WideCard.GlassBlur, 0, 100);
        Assert.InRange(c.WideCard.NeonGlow, 0, 100);
        Assert.InRange(c.WideCard.BadgeScale, 10, 300);
        Assert.InRange(c.WideCard.BadgeGapPercent, 0, 50);
        Assert.Equal("#0E1018", c.WideCard.GlassTint);
    }

    [Fact]
    public void WideCardNonFiniteFontScale_DoesNotSurvive()
    {
        var c = new PluginConfiguration { WideCard = new WideCardStyle { BannerFontScale = double.NaN } };
        ConfigurationSanitizer.Normalize(c);
        Assert.InRange(c.WideCard.BannerFontScale, 0.2, 3.0);
    }

    [Fact]
    public void AppearanceFor_Thumb_InheritsPoster_UnlessCustomizeIsOn()
    {
        var c = new PluginConfiguration
        {
            BannerStyle = "solid",
            BadgeScale = 100,
            WideCardCustomize = false,
            WideCard = new WideCardStyle { BannerStyle = "neon", BadgeScale = 200 },
        };

        // Posters always use the poster fields.
        var poster = c.AppearanceFor(thumb: false);
        Assert.Equal("solid", poster.BannerStyle);
        Assert.Equal(100, poster.BadgeScale);

        // Wide cards inherit the poster fields while customization is off — even though WideCard differs.
        var inherited = c.AppearanceFor(thumb: true);
        Assert.Equal("solid", inherited.BannerStyle);
        Assert.Equal(100, inherited.BadgeScale);

        // Turning it on switches wide cards to the overrides, without touching the poster resolution.
        c.WideCardCustomize = true;
        var overridden = c.AppearanceFor(thumb: true);
        Assert.Equal("neon", overridden.BannerStyle);
        Assert.Equal(200, overridden.BadgeScale);
        Assert.Equal("solid", c.AppearanceFor(thumb: false).BannerStyle);
    }
}
