using PKHeX.Core;

namespace Rhome.Core.Tests;

/// <summary>The test saves in fixtures/, loaded by name ("emerald", "platinum-us" …).</summary>
static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".sav");

    public static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));

    public static SaveFile Load(string name) => SaveDetector.Detect(Bytes(name))!.Save;

    public static PKM Party(string name, int slot) => SaveContents.Party(Load(name))[slot - 1].Pokemon;

    public static PKM Box(string name, int box, int slot) =>
        SaveContents.Boxes(Load(name)).Single(s => s.Where == new SaveSlot(box, slot)).Pokemon;
}
