using System;

public enum SaveKind { Manual = 0, Quick = 1, Auto = 2 }

// The small sidecar (<name>.info.json) next to every named save (M19): what the save browser lists, so it never has
// to parse a whole city. Name is the file name, filled in by SaveSlots.List (never stored); Damaged marks a save whose
// city file can't be read (it can only be deleted).
[Serializable]
public sealed class SaveSummary
{
    public string CityName = "";
    public int Age = SaveData.NoAge;
    public string AgeName = "";
    public int Population;
    public float Money;
    public int Day = 1;
    public int Month = 1;
    public int Year = 1;
    public int Width;
    public int Height;
    public long SavedUtcTicks;
    public int SaveVersion;
    public string GameVersion = "";
    public int Kind;
    public bool Tutorial;

    [NonSerialized] public string Name = "";
    [NonSerialized] public bool Damaged;
    [NonSerialized] public bool HasThumbnail;

    public SaveKind SaveKind => Enum.IsDefined(typeof(SaveKind), Kind) ? (SaveKind)Kind : SaveKind.Manual;
    public DateTime SavedUtc => new DateTime(Math.Max(0, SavedUtcTicks), DateTimeKind.Utc);

    public static SaveSummary From(SaveData data, SaveKind kind, long savedUtcTicks, string gameVersion, string ageName)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        return new SaveSummary
        {
            CityName = data.CityName ?? "",
            Age = data.Age,
            AgeName = ageName ?? "",
            Population = data.Population,
            Money = data.Money,
            Day = data.Day,
            Month = data.Month,
            Year = data.Year,
            Width = data.Width,
            Height = data.Height,
            SavedUtcTicks = savedUtcTicks,
            SaveVersion = data.Version,
            GameVersion = gameVersion ?? "",
            Kind = (int)kind,
            Tutorial = data.Tutorial != SaveData.NoTutorial,
        };
    }
}
