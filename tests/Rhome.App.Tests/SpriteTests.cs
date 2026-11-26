using System.Net;
using PKHeX.Core;
using Rhome.App.Sprites;

namespace Rhome.App.Tests;

/// <summary>Which sprite file shows a Pokémon, and keeping sprites on the device (issue #48).</summary>
public sealed class SpriteTests : IDisposable
{
    static readonly SpriteTable Table = SpriteTable.Default;
    static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("rhome-sprites-");

    public void Dispose() => folder.Delete(recursive: true);

    static string? PathFor(ushort species, byte form = 0, bool shiny = false, bool female = false, bool egg = false, int format = 5) =>
        Table.PathFor(new SpriteRequest(species, form, shiny, female, egg, format));

    [Fact]
    public void A_plain_species_is_its_number_in_black_and_whites_folder_at_the_pinned_commit()
    {
        Assert.Equal("1.png", PathFor(1));
        Assert.Matches("^[0-9a-f]{40}$", Table.Commit);
        Assert.Equal(
            $"https://cdn.jsdelivr.net/gh/PokeAPI/sprites@{Table.Commit}/sprites/pokemon/versions/generation-v/black-white/1.png",
            Table.UrlFor("1.png").ToString());
    }

    [Theory]
    [InlineData(201, 1, "201-b.png")]        // Unown B: a cosmetic form, named by its letter
    [InlineData(201, 27, "201-question.png")]
    [InlineData(172, 1, "172-spiky-eared.png")] // HeartGold/SoulSilver's event Pichu, a Gen 4-only form
    [InlineData(386, 1, "10001.png")]        // Attack Forme Deoxys: PokeAPI counts it as a Pokémon of its own
    [InlineData(479, 1, "10008.png")]        // Heat Rotom
    [InlineData(487, 1, "10007.png")]        // Origin Forme Giratina
    [InlineData(585, 3, "585-winter.png")]   // Winter Deerling
    [InlineData(649, 1, "649-douse.png")]    // Genesect with the Douse Drive
    [InlineData(414, 1, "414.png")]          // Mothim's forms look the same: the plain sprite
    public void A_form_uses_its_own_file_when_it_has_one(int species, int form, string path) =>
        Assert.Equal(path, PathFor((ushort)species, (byte)form));

    [Fact]
    public void Shiny_and_female_sprites_are_used_where_they_exist()
    {
        Assert.Equal("shiny/25.png", PathFor(25, shiny: true));
        Assert.Equal("female/25.png", PathFor(25, female: true));
        Assert.Equal("shiny/female/25.png", PathFor(25, shiny: true, female: true));
        Assert.Equal("1.png", PathFor(1, female: true)); // Bulbasaur looks the same either way
    }

    [Fact]
    public void Gen_4_arceus_forms_are_numbered_past_its_unknown_type_form()
    {
        Assert.Equal("493.png", PathFor(493, 9, format: 4));        // ???: shown as plain Arceus
        Assert.Equal("493-fire.png", PathFor(493, 10, format: 4));
        Assert.Equal("493-fire.png", PathFor(493, 9, format: 5));
        Assert.Equal("493-fighting.png", PathFor(493, 1, format: 4));
    }

    [Fact]
    public void Eggs_show_an_egg_and_unknown_species_show_nothing()
    {
        Assert.Equal("egg.png", PathFor(25, egg: true));
        Assert.Null(PathFor(0));
        Assert.Null(PathFor((ushort)(Table.MaxSpecies + 1)));
    }

    [Fact]
    public void A_request_from_a_pokemon_takes_its_shininess_gender_and_format()
    {
        var pk = new PK4 { Species = 25, Gender = 1 };
        pk.SetIsShiny(true);
        Assert.Equal(new SpriteRequest(25, 0, true, true, false, 4), SpriteRequest.Of(pk));
    }

    [Fact]
    public void Download_for_my_games_covers_every_sprite_up_to_genesect_once()
    {
        var paths = Table.AllPaths(649);
        Assert.Equal(paths.Count, paths.Distinct().Count());
        Assert.Contains("201-b.png", paths);
        Assert.Contains("shiny/female/25.png", paths);
        Assert.Contains("egg.png", paths);
        Assert.DoesNotContain("650.png", paths);
        for (ushort species = 1; species <= 649; species++)
        {
            Assert.Contains(PathFor(species)!, paths);
            Assert.Contains(PathFor(species, shiny: true)!, paths);
        }
    }

    [Fact]
    public async Task A_sprite_is_downloaded_once_then_read_from_the_device()
    {
        var web = new FakeWeb(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });
        var cache = new SpriteCache(Table, folder.FullName, new HttpClient(web));

        Assert.Equal(Png, await cache.GetAsync("shiny/25.png", Ct));
        Assert.True(cache.Has("shiny/25.png"));
        Assert.Equal(Png, await cache.GetAsync("shiny/25.png", Ct));
        Assert.Equal(Png, await new SpriteCache(Table, folder.FullName, new HttpClient(web)).GetAsync("shiny/25.png", Ct));

        Assert.Equal([Table.UrlFor("shiny/25.png")], web.Asked);
        Assert.True(File.Exists(Path.Combine(folder.FullName, Table.Commit, "shiny", "25.png")));
    }

    [Fact]
    public async Task Slots_showing_the_same_sprite_at_once_share_one_download()
    {
        var release = new TaskCompletionSource();
        var web = FakeWeb.Async(async _ =>
        {
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        });
        var cache = new SpriteCache(Table, folder.FullName, new HttpClient(web));

        var loads = Enumerable.Range(0, 5).Select(_ => cache.GetAsync("1.png", Ct)).ToList();
        release.SetResult();
        foreach (var bytes in await Task.WhenAll(loads))
            Assert.Equal(Png, bytes);
        Assert.Single(web.Asked);
    }

    [Fact]
    public async Task What_isnt_a_png_is_not_kept_and_a_failure_isnt_retried_straight_away()
    {
        var web = new FakeWeb(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>rate limited</html>") });
        var cache = new SpriteCache(Table, folder.FullName, new HttpClient(web));

        Assert.Null(await cache.GetAsync("1.png", Ct));
        Assert.False(cache.Has("1.png"));
        Assert.Null(await cache.GetAsync("1.png", Ct));
        Assert.Single(web.Asked);
    }

    [Fact]
    public async Task Offline_there_are_no_sprites_and_nothing_breaks()
    {
        var web = new FakeWeb(_ => throw new HttpRequestException("no network"));
        var cache = new SpriteCache(Table, folder.FullName, new HttpClient(web));

        Assert.Null(await cache.GetAsync("1.png", Ct));
        Assert.Null(await new SpriteCache(Table, folder.FullName,
            new HttpClient(new FakeWeb(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))).GetAsync("2.png", Ct));
    }

    [Fact]
    public async Task Download_all_fetches_only_what_isnt_on_the_device()
    {
        var web = new FakeWeb(r => r.RequestUri!.AbsolutePath.EndsWith("/3.png", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });
        var cache = new SpriteCache(Table, folder.FullName, new HttpClient(web));
        await cache.GetAsync("1.png", Ct);

        var reports = new List<(int Done, int Total)>();
        var result = await cache.DownloadAllAsync(["1.png", "2.png", "3.png", "4.png"], new SyncProgress(reports.Add), Ct);

        Assert.Equal(new SpriteDownload(Total: 4, AlreadyHad: 1, Downloaded: 2, Failed: 1), result);
        Assert.Equal((3, 3), reports.MaxBy(r => r.Done));
        Assert.Equal(4, web.Asked.Count);
    }

    sealed class SyncProgress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value)
        {
            lock (this)
                report(value);
        }
    }

    /// <summary>Answers requests without a network, and remembers what was asked for.</summary>
    sealed class FakeWeb(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        Func<HttpRequestMessage, Task<HttpResponseMessage>> answerAsync = r => Task.FromResult(answer(r));

        public static FakeWeb Async(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) =>
            new(_ => throw new InvalidOperationException()) { answerAsync = answer };

        public List<Uri> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Asked)
                Asked.Add(request.RequestUri!);
            return await answerAsync(request);
        }
    }
}
