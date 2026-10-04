using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// M19a: named saves on disk (SaveSlots), all in a temp folder.
public sealed class SaveSlotsTests
{
    private string m_Dir;
    private BalanceConfig m_Config;

    [SetUp]
    public void SetUp()
    {
        m_Dir = Path.Combine(Path.GetTempPath(), "CityGameSaveSlots_" + Guid.NewGuid().ToString("N"));
        SaveSlots.Root = m_Dir;
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(m_Config);
        if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
    }

    private SaveData City(string name = "Ashford", int population = 100)
    {
        SaveData data = SaveSystem.CreateNew(8, 8, m_Config);
        data.CityName = name;
        data.Population = population;
        return data;
    }

    private static SaveSummary Summary(SaveData data, SaveKind kind, long ticks)
    {
        return SaveSummary.From(data, kind, ticks, "1.0.0", "Industrial Age");
    }

    private void Save(string name, SaveData data, SaveKind kind, long ticks, byte[] png = null)
    {
        Assert.IsTrue(SaveSlots.Write(name, data, Summary(data, kind, ticks), png, out string error), error);
    }

    // --- names ---

    [Test]
    public void SanitizeName_DropsInvalidCharsAndDots_AndLimitsLength()
    {
        Assert.AreEqual("ab", SaveSlots.SanitizeName("a/b"));
        Assert.AreEqual("Mill", SaveSlots.SanitizeName("  Mi:l*l?  "));
        Assert.AreEqual("xinfo", SaveSlots.SanitizeName("x.info"), "a dot would make 'x.info' the sidecar of 'x'");
        Assert.AreEqual("Trailing", SaveSlots.SanitizeName("Trailing..."));
        Assert.AreEqual(SaveSlots.MaxNameLength, SaveSlots.SanitizeName(new string('a', 200)).Length);
    }

    [Test]
    public void SanitizeName_NeverEmpty_AndAvoidsReservedWindowsNames()
    {
        Assert.AreEqual("Unnamed", SaveSlots.SanitizeName(""));
        Assert.AreEqual("Unnamed", SaveSlots.SanitizeName(null));
        Assert.AreEqual("Unnamed", SaveSlots.SanitizeName("...:::"));
        Assert.AreEqual("_CON", SaveSlots.SanitizeName("CON"));
        Assert.AreEqual("_lpt1", SaveSlots.SanitizeName("lpt1"));
        Assert.AreEqual("Console", SaveSlots.SanitizeName("Console"));
    }

    [Test]
    public void UniqueName_AddsASuffixWhenTheNameIsTaken()
    {
        Assert.AreEqual("Ashford", SaveSlots.UniqueName("Ashford"));
        Save("Ashford", City(), SaveKind.Manual, 100);
        Assert.AreEqual("Ashford 2", SaveSlots.UniqueName("Ashford"));
        Save("Ashford 2", City(), SaveKind.Manual, 100);
        Assert.AreEqual("Ashford 3", SaveSlots.UniqueName("Ashford"));

        string longName = new string('b', 60);
        string stem = SaveSlots.SanitizeName(longName);
        Save(stem, City(), SaveKind.Manual, 100);
        string next = SaveSlots.UniqueName(longName);
        Assert.AreNotEqual(stem, next);
        Assert.LessOrEqual(next.Length, SaveSlots.MaxNameLength);
    }

    [Test]
    public void QuickName_IsOnePerCity_AndKindsAreRecognised()
    {
        Assert.AreEqual("Ashford_quick", SaveSlots.QuickName("Ashford"));
        Assert.AreEqual("Unnamed city_quick", SaveSlots.QuickName(""));
        Assert.LessOrEqual(SaveSlots.QuickName(new string('c', 80)).Length, SaveSlots.MaxNameLength);
        Assert.AreEqual(SaveKind.Quick, SaveSlots.KindOf("Ashford_quick"));
        Assert.AreEqual(SaveKind.Auto, SaveSlots.KindOf(SaveSlots.AutosaveName(2)));
        Assert.AreEqual(SaveKind.Manual, SaveSlots.KindOf("Ashford"));
    }

    // --- write, list, delete, rename ---

    [Test]
    public void Write_ThenList_GivesTheSummary_NewestFirst()
    {
        Save("Old town", City("Old town", 10), SaveKind.Manual, 1000);
        Save("New town", City("New town", 99), SaveKind.Manual, 3000, new byte[] { 1, 2, 3 });
        Save("Mid town", City("Mid town", 50), SaveKind.Manual, 2000);

        var list = SaveSlots.List();

        Assert.AreEqual(3, list.Count);
        CollectionAssert.AreEqual(new[] { "New town", "Mid town", "Old town" }, list.ConvertAll(s => s.Name));
        Assert.AreEqual("New town", list[0].CityName);
        Assert.AreEqual(99, list[0].Population);
        Assert.AreEqual("Industrial Age", list[0].AgeName);
        Assert.AreEqual(8, list[0].Width);
        Assert.AreEqual(SaveData.CurrentVersion, list[0].SaveVersion);
        Assert.IsTrue(list[0].HasThumbnail);
        Assert.IsFalse(list[1].HasThumbnail);
        Assert.IsFalse(list[0].Damaged);
    }

    [Test]
    public void Write_RoundTripsTheCity()
    {
        SaveData data = City("Roundtrip", 42);
        data.Tutorial = 4;
        Save("Roundtrip", data, SaveKind.Manual, 5);

        Assert.IsTrue(SaveSlots.TryRead("Roundtrip", out SaveData loaded, out string error), error);
        Assert.AreEqual("Roundtrip", loaded.CityName);
        Assert.AreEqual(4, loaded.Tutorial);
        Assert.AreEqual(42, loaded.Population);
    }

    [Test]
    public void Write_OverTheSameName_ReplacesTheCityAndDropsTheOldThumbnail()
    {
        Save("Ashford", City("Ashford", 10), SaveKind.Manual, 100, new byte[] { 9, 9 });
        Save("Ashford", City("Ashford", 77), SaveKind.Manual, 200);

        var list = SaveSlots.List();
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(77, list[0].Population);
        Assert.IsFalse(list[0].HasThumbnail, "an overwrite without a thumbnail must not keep the stale one");
        Assert.AreEqual(0, Directory.GetFiles(m_Dir, "*.tmp").Length, "no temp file is left behind");
    }

    [Test]
    public void Write_RejectsANameThatIsNotSanitized()
    {
        SaveData data = City();
        Assert.IsFalse(SaveSlots.Write("a/b", data, Summary(data, SaveKind.Manual, 1), null, out string error));
        StringAssert.Contains("not a valid", error);
        Assert.IsFalse(SaveSlots.Write("", data, Summary(data, SaveKind.Manual, 1), null, out _));
    }

    [Test]
    public void List_IgnoresStaleTempFilesAndSidecars_AndEmptyFolders()
    {
        Assert.AreEqual(0, SaveSlots.List().Count, "no folder yet");
        Save("Ashford", City(), SaveKind.Manual, 100);
        File.WriteAllText(Path.Combine(m_Dir, "Half.json.tmp"), "{");

        var list = SaveSlots.List();

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("Ashford", list[0].Name);
    }

    [Test]
    public void List_RebuildsAMissingSidecar_AndMarksKind()
    {
        Save("Ashford_quick", City("Ashford", 31), SaveKind.Quick, 100);
        File.Delete(Path.Combine(m_Dir, "Ashford_quick.info.json"));

        var list = SaveSlots.List();

        Assert.AreEqual(1, list.Count);
        Assert.IsFalse(list[0].Damaged);
        Assert.AreEqual(31, list[0].Population);
        Assert.AreEqual(SaveKind.Quick, list[0].SaveKind);
        Assert.IsTrue(File.Exists(Path.Combine(m_Dir, "Ashford_quick.info.json")), "the rebuilt sidecar is written back");
    }

    [Test]
    public void List_RebuildsACorruptSidecar()
    {
        Save("Ashford", City("Ashford", 31), SaveKind.Manual, 100);
        File.WriteAllText(Path.Combine(m_Dir, "Ashford.info.json"), "not json at all");

        var list = SaveSlots.List();

        Assert.IsFalse(list[0].Damaged);
        Assert.AreEqual(31, list[0].Population);
    }

    [Test]
    public void List_MarksAnUnreadableCityAsDamaged_AndItCanBeDeleted()
    {
        Directory.CreateDirectory(m_Dir);
        File.WriteAllText(Path.Combine(m_Dir, "Broken.json"), "{ this is not a save");
        Save("Fine", City(), SaveKind.Manual, DateTime.UtcNow.Ticks + 10);

        var list = SaveSlots.List();

        Assert.AreEqual(2, list.Count);
        SaveSummary broken = list.Find(s => s.Name == "Broken");
        Assert.IsTrue(broken.Damaged);
        Assert.IsFalse(list.Find(s => s.Name == "Fine").Damaged);

        Assert.IsTrue(SaveSlots.Delete("Broken"));
        Assert.AreEqual(1, SaveSlots.List().Count);
    }

    [Test]
    public void Delete_RemovesTheCitySidecarAndThumbnail()
    {
        Save("Ashford", City(), SaveKind.Manual, 100, new byte[] { 1 });

        Assert.IsTrue(SaveSlots.Delete("Ashford"));

        Assert.AreEqual(0, Directory.GetFiles(m_Dir).Length);
        Assert.IsFalse(SaveSlots.Delete("Ashford"), "already gone");
    }

    [Test]
    public void Rename_MovesAllThreeFiles_AndRefusesATakenName()
    {
        Save("Ashford", City(), SaveKind.Manual, 100, new byte[] { 1 });
        Save("Kingsbridge", City(), SaveKind.Manual, 200);

        Assert.IsTrue(SaveSlots.Rename("Ashford", "Ash:ford 2", out string finalName, out string error), error);
        Assert.AreEqual("Ashford 2", finalName);
        Assert.IsFalse(SaveSlots.Exists("Ashford"));
        Assert.IsTrue(SaveSlots.List().Find(s => s.Name == "Ashford 2").HasThumbnail);

        Assert.IsFalse(SaveSlots.Rename("Ashford 2", "Kingsbridge", out _, out error));
        StringAssert.Contains("already exists", error);
        Assert.IsTrue(SaveSlots.Exists("Ashford 2"), "a refused rename changes nothing");
        Assert.IsFalse(SaveSlots.Rename("Nothing", "Else", out _, out _));
    }

    // --- autosave rotation ---

    [Test]
    public void NextAutosave_FillsTheSlotsThenOverwritesTheOldest()
    {
        Assert.AreEqual("autosave_1", SaveSlots.NextAutosave());
        Save("autosave_1", City(), SaveKind.Auto, 300);
        Assert.AreEqual("autosave_2", SaveSlots.NextAutosave());
        Save("autosave_2", City(), SaveKind.Auto, 100);
        Assert.AreEqual("autosave_3", SaveSlots.NextAutosave());
        Save("autosave_3", City(), SaveKind.Auto, 200);

        Assert.AreEqual("autosave_2", SaveSlots.NextAutosave(), "the oldest of the three");
        Save("autosave_2", City(), SaveKind.Auto, 400);
        Assert.AreEqual("autosave_3", SaveSlots.NextAutosave());
        Save("autosave_3", City(), SaveKind.Auto, 500);
        Assert.AreEqual("autosave_1", SaveSlots.NextAutosave());
    }

    [Test]
    public void NextAutosave_FallsBackToTheFileTime_WhenTheSidecarIsGone()
    {
        for (int i = 1; i <= SaveSlots.AutosaveCount; i++) Save(SaveSlots.AutosaveName(i), City(), SaveKind.Auto, new DateTime(2020, 1, i, 0, 0, 0, DateTimeKind.Utc).Ticks);
        File.Delete(Path.Combine(m_Dir, "autosave_2.info.json"));
        File.SetLastWriteTimeUtc(Path.Combine(m_Dir, "autosave_2.json"), new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.AreEqual("autosave_2", SaveSlots.NextAutosave());
    }

    // --- the legacy save ---

    [Test]
    public void ImportLegacy_CopiesTheOldCityOnce_AndLeavesTheOriginal()
    {
        string legacy = Path.Combine(m_Dir, "legacy", "city.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy));
        SaveData data = SaveSystem.CreateNew(8, 8, m_Config);
        data.Population = 123;
        Assert.IsTrue(SaveSystem.TryWrite(legacy, data, out string error), error);

        Assert.IsTrue(SaveSlots.ImportLegacy(legacy));

        Assert.IsTrue(File.Exists(legacy), "copied, not moved");
        var list = SaveSlots.List();
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(SaveSlots.LegacyImportName, list[0].Name);
        Assert.AreEqual(123, list[0].Population);
        Assert.IsFalse(list[0].HasThumbnail);

        Assert.IsFalse(SaveSlots.ImportLegacy(legacy), "only once");
        SaveSlots.Delete(SaveSlots.LegacyImportName);
        Assert.IsFalse(SaveSlots.ImportLegacy(legacy), "deleting the import doesn't bring it back");
        Assert.AreEqual(0, SaveSlots.List().Count);
    }

    [Test]
    public void ImportLegacy_WithoutAFileOrWithABadOne_ImportsNothing()
    {
        Assert.IsFalse(SaveSlots.ImportLegacy(Path.Combine(m_Dir, "nope", "city.json")));

        string bad = Path.Combine(m_Dir, "legacy", "bad.json");
        Directory.CreateDirectory(Path.GetDirectoryName(bad));
        File.WriteAllText(bad, "garbage");
        Assert.IsFalse(SaveSlots.ImportLegacy(bad));
        Assert.AreEqual(0, SaveSlots.List().Count);
    }

    [Test]
    public void ImportLegacy_MigratesTheOldFixtureOnLoad()
    {
        string fixture = Path.Combine(Application.dataPath, "_Game/Tests/EditMode/Simulation/Fixtures/city_v1_24x24.json");

        Assert.IsTrue(SaveSlots.ImportLegacy(fixture));

        Assert.IsTrue(SaveSlots.TryRead(SaveSlots.LegacyImportName, out SaveData data, out string error), error);
        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.AreEqual("", data.CityName);
        Assert.AreEqual(SaveData.NoTutorial, data.Tutorial);
        Assert.AreEqual(24, data.Width);
    }
}
