using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

public class SaveDetectorTests
{
    // PKHeX's blank saves stand in for real ones until the fixtures in issue #4 exist. Blank Gen 3/4
    // saves only exist in memory (they can't be written out as a save file), so the tests that start
    // from bytes use Gen 5; the fixtures will cover Gen 3 and 4 from bytes.
    static SaveFile Blank(GameVersion game) => BlankSaveFile.Get(game, "TEST", LanguageID.English);

    static byte[] BlankBytes(GameVersion game) => Blank(game).Write(BinaryExportSetting.None).ToArray();

    [Theory]
    [InlineData(GameVersion.R)]
    [InlineData(GameVersion.E)]
    [InlineData(GameVersion.FR)]
    [InlineData(GameVersion.LG)]
    [InlineData(GameVersion.D)]
    [InlineData(GameVersion.Pt)]
    [InlineData(GameVersion.HG)]
    [InlineData(GameVersion.B)]
    [InlineData(GameVersion.W2)]
    public void Tier_one_games_are_supported(GameVersion game)
    {
        Assert.True(SaveDetector.IsSupported(Blank(game)));
    }

    [Theory]
    [InlineData(GameVersion.X)]
    [InlineData(GameVersion.SW)]
    public void Other_games_are_not_supported_yet(GameVersion game)
    {
        Assert.False(SaveDetector.IsSupported(Blank(game)));
    }

    [Theory]
    [InlineData(typeof(SAV3Colosseum))]
    [InlineData(typeof(SAV3XD))]
    [InlineData(typeof(SAV3RSBox))]
    [InlineData(typeof(SAV4BR))]
    [InlineData(typeof(SAV4Ranch))]
    public void Spin_off_formats_are_not_tier_one_save_classes(Type saveType)
    {
        // IsSupported relies on PKHeX keeping spin-offs (their own save formats) outside the mainline
        // SAV3/SAV4/SAV5 classes. Their constructors need real data, so check the class tree instead.
        Assert.False(typeof(SAV3).IsAssignableFrom(saveType));
        Assert.False(typeof(SAV4).IsAssignableFrom(saveType));
        Assert.False(typeof(SAV5).IsAssignableFrom(saveType));
    }

    [Theory]
    [InlineData(GameVersion.B)]
    [InlineData(GameVersion.W2)]
    public void A_save_is_recognized_from_its_bytes(GameVersion game)
    {
        var detected = SaveDetector.Detect(BlankBytes(game));

        Assert.NotNull(detected);
        Assert.Equal(game, detected.Game);
        Assert.Equal(5, detected.Generation);
        Assert.Equal("TEST", detected.Trainer);
        Assert.True(detected.IsSupported);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(0x80000)] // a DS save's size, but all zeros
    public void Bytes_that_are_not_a_save_are_refused(int size)
    {
        Assert.Null(SaveDetector.Detect(new byte[size]));
    }

    [Fact]
    public void The_callers_bytes_are_never_changed()
    {
        var data = BlankBytes(GameVersion.B);
        var original = data.ToArray();

        var detected = SaveDetector.Detect(data);
        detected!.Save.OT = "CHANGED";
        detected.Save.Write(BinaryExportSetting.None);

        Assert.Equal(original, data);
    }
}
