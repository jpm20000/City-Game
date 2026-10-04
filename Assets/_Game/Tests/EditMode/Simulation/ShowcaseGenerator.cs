using System.IO;
using NUnit.Framework;
using UnityEngine;

// M19c: writes the main menu's showcase city, Assets/StreamingAssets/showcase.json. A Medieval start played by the
// EngagedCity harness on the shipped content until the Modern age has had some days, so the built ages mix. The
// buildings are the harness's real placements (ids, origins), which the game re-places on load. Re-run it only when the
// content changes (the load check counts skipped buildings: it must stay 0), then commit the file and its .meta.
public sealed class ShowcaseGenerator
{
    private const int Size = 64;
    private const int DaysIntoModern = 220;

    [Test, Explicit("Writes Assets/StreamingAssets/showcase.json")]
    public void Generate()
    {
        var config = ScriptableObject.CreateInstance<BalanceConfig>();
        try
        {
            var city = new EngagedCity(config, 0, Size);
            int guard = 0;
            while (city.Sim.Tech.CurrentAge < 3 && guard++ < 900) city.RunDay();
            Assert.GreaterOrEqual(city.Sim.Tech.CurrentAge, 3, "the harness player reaches the Modern age");
            city.RunDays(DaysIntoModern);

            SaveData data = SaveSystem.Capture(city.Grid, city.Sim);
            data.Buildings.AddRange(city.PlacedBuildings());
            data.CityName = "Showcase";
            data.Day = 12;
            data.Month = 3;
            data.Year = city.Ages[data.Age].StartYear + 4;
            data.Speed = (int)GameSpeed.x1;
            data.Disasters = false;

            string path = Path.Combine(Application.dataPath, "StreamingAssets", "showcase.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, SaveSystem.ToJson(data));

            int grown = 0;
            foreach (byte level in data.Levels) if (level > 0) grown++;
            TestContext.WriteLine($"showcase: {Size}x{Size}, age {data.Age}, pop {data.Population}, {grown} grown cells, " +
                                  $"{data.Buildings.Count} buildings, {new FileInfo(path).Length / 1024} KB -> {path}");
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }
}
