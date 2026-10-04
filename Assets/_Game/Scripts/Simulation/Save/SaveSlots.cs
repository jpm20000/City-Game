using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Named saves on disk (M19a): <Root>/<name>.json (the city, SaveData), <name>.info.json (SaveSummary) and <name>.png
// (the thumbnail, bytes handed in by the runtime). Pure System.IO, so it is testable in a temp folder. Root is set by
// the runtime (persistentDataPath/Saves, or the -savesDir override) and by tests.
public static class SaveSlots
{
    public const int MaxNameLength = 48;
    public const int AutosaveCount = 3;
    public const string LegacyImportName = "Imported city";
    public const string UnnamedCity = "Unnamed city";

    private const string JsonExt = ".json";
    private const string InfoExt = ".info.json";
    private const string ThumbExt = ".png";
    private const string LegacyMarker = ".legacy_imported";
    private const string QuickSuffix = "_quick";
    private const string AutoPrefix = "autosave_";

    private static readonly string[] s_Reserved =
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static string s_Root;

    public static bool HasRoot => !string.IsNullOrEmpty(s_Root);

    public static string Root
    {
        get
        {
            if (string.IsNullOrEmpty(s_Root)) throw new InvalidOperationException("SaveSlots.Root has not been set.");
            return s_Root;
        }
        set => s_Root = value;
    }

    public static string JsonPath(string name) => Path.Combine(Root, name + JsonExt);
    public static string InfoPath(string name) => Path.Combine(Root, name + InfoExt);
    public static string ThumbnailPath(string name) => Path.Combine(Root, name + ThumbExt);

    public static bool Exists(string name) => !string.IsNullOrEmpty(name) && File.Exists(JsonPath(name));

    // --- Names ---

    // A file-name-safe name: invalid characters and dots are dropped (a dot would let "x.info" collide with the
    // sidecar of "x"), reserved Windows names get a leading underscore, at most MaxNameLength characters; never empty.
    public static string SanitizeName(string raw)
    {
        var sb = new StringBuilder();
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in raw ?? "")
        {
            if (c == '.' || char.IsControl(c) || Array.IndexOf(invalid, c) >= 0) continue;
            sb.Append(c);
        }
        string name = sb.ToString().Trim();
        if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength).TrimEnd();
        if (name.Length == 0) return "Unnamed";
        foreach (string reserved in s_Reserved)
        {
            if (string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase)) return "_" + name;
        }
        return name;
    }

    // The sanitized name, or "name 2", "name 3", ... when a save of that name already exists.
    public static string UniqueName(string raw)
    {
        string name = SanitizeName(raw);
        if (!Exists(name)) return name;
        for (int n = 2; ; n++)
        {
            string suffix = " " + n;
            string stem = name.Length + suffix.Length > MaxNameLength ? name.Substring(0, MaxNameLength - suffix.Length).TrimEnd() : name;
            string candidate = stem + suffix;
            if (!Exists(candidate)) return candidate;
        }
    }

    // One quicksave per city: "<city>_quick".
    public static string QuickName(string cityName)
    {
        string city = string.IsNullOrWhiteSpace(cityName) ? UnnamedCity : cityName;
        string stem = SanitizeName(city);
        if (stem.Length + QuickSuffix.Length > MaxNameLength) stem = stem.Substring(0, MaxNameLength - QuickSuffix.Length).TrimEnd();
        return stem + QuickSuffix;
    }

    public static string AutosaveName(int slot) => AutoPrefix + slot;

    public static SaveKind KindOf(string name)
    {
        if (name == null) return SaveKind.Manual;
        if (name.StartsWith(AutoPrefix, StringComparison.Ordinal)) return SaveKind.Auto;
        if (name.EndsWith(QuickSuffix, StringComparison.Ordinal)) return SaveKind.Quick;
        return SaveKind.Manual;
    }

    // The autosave slot to write next: the first that doesn't exist, else the one saved longest ago.
    public static string NextAutosave()
    {
        string oldest = null;
        long oldestTicks = long.MaxValue;
        for (int i = 1; i <= AutosaveCount; i++)
        {
            string name = AutosaveName(i);
            if (!Exists(name)) return name;
            long ticks = SavedTicks(name);
            if (ticks < oldestTicks)
            {
                oldestTicks = ticks;
                oldest = name;
            }
        }
        return oldest;
    }

    // --- Writing ---

    // Writes the city, its sidecar and its thumbnail (png may be null: any older thumbnail of that name is removed).
    // Only the city file decides success; a failed sidecar or thumbnail is rebuilt / missing, never an error.
    public static bool Write(string name, SaveData data, SaveSummary summary, byte[] png, out string error)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (summary == null) throw new ArgumentNullException(nameof(summary));
        error = null;
        if (string.IsNullOrEmpty(name) || SanitizeName(name) != name)
        {
            error = $"'{name}' is not a valid save name.";
            return false;
        }
        if (!SaveSystem.TryWrite(JsonPath(name), data, out error)) return false;

        try
        {
            File.WriteAllText(InfoPath(name), JsonUtility.ToJson(summary));
            if (png != null && png.Length > 0) File.WriteAllBytes(ThumbnailPath(name), png);
            else if (File.Exists(ThumbnailPath(name))) File.Delete(ThumbnailPath(name));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
        }
        return true;
    }

    public static bool Delete(string name)
    {
        if (!Exists(name)) return false;
        try
        {
            File.Delete(JsonPath(name));
            if (File.Exists(InfoPath(name))) File.Delete(InfoPath(name));
            if (File.Exists(ThumbnailPath(name))) File.Delete(ThumbnailPath(name));
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Renames a save (and its sidecar and thumbnail). newName is sanitized; it must not belong to another save.
    public static bool Rename(string oldName, string newName, out string finalName, out string error)
    {
        finalName = SanitizeName(newName);
        error = null;
        if (!Exists(oldName))
        {
            error = "That save no longer exists.";
            return false;
        }
        if (finalName == oldName) return true;
        if (Exists(finalName))
        {
            error = $"A save called '{finalName}' already exists.";
            return false;
        }
        try
        {
            File.Move(JsonPath(oldName), JsonPath(finalName));
            if (File.Exists(InfoPath(oldName))) File.Move(InfoPath(oldName), InfoPath(finalName));
            if (File.Exists(ThumbnailPath(oldName))) File.Move(ThumbnailPath(oldName), ThumbnailPath(finalName));
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            error = e.Message;
            return false;
        }
    }

    // --- Listing ---

    // True when at least one save exists (a directory scan, no parsing).
    public static bool HasAny()
    {
        if (!HasRoot || !Directory.Exists(s_Root)) return false;
        foreach (string path in Directory.GetFiles(s_Root, "*" + JsonExt))
        {
            if (!path.EndsWith(InfoExt, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // Every save, newest first. A missing or unreadable sidecar is rebuilt from the city file (ages gives the age name);
    // a city file that can't be read is listed as Damaged.
    public static List<SaveSummary> List(AgeDatabase ages = null, TechDatabase techs = null)
    {
        var result = new List<SaveSummary>();
        if (!HasRoot || !Directory.Exists(s_Root)) return result;

        foreach (string path in Directory.GetFiles(s_Root, "*" + JsonExt))
        {
            string file = Path.GetFileName(path);
            if (file.EndsWith(InfoExt, StringComparison.OrdinalIgnoreCase)) continue;
            string name = file.Substring(0, file.Length - JsonExt.Length);

            SaveSummary summary = ReadSummary(name) ?? Rebuild(name, ages, techs);
            summary.Name = name;
            summary.HasThumbnail = File.Exists(ThumbnailPath(name));
            result.Add(summary);
        }
        result.Sort((a, b) =>
        {
            int byTime = b.SavedUtcTicks.CompareTo(a.SavedUtcTicks);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Name, b.Name);
        });
        return result;
    }

    // Reads and migrates a save by name.
    public static bool TryRead(string name, out SaveData data, out string error, AgeDatabase ages = null, TechDatabase techs = null)
    {
        return SaveSystem.TryRead(JsonPath(name), out data, out error, ages, techs);
    }

    private static SaveSummary ReadSummary(string name)
    {
        string path = InfoPath(name);
        if (!File.Exists(path)) return null;
        try
        {
            SaveSummary summary = JsonUtility.FromJson<SaveSummary>(File.ReadAllText(path));
            if (summary == null || summary.SavedUtcTicks <= 0 || summary.SaveVersion <= 0 || summary.Width <= 0) return null;
            return summary;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
        {
            return null;
        }
    }

    private static long SavedTicks(string name)
    {
        SaveSummary summary = ReadSummary(name);
        return summary != null ? summary.SavedUtcTicks : FileTicks(JsonPath(name));
    }

    private static long FileTicks(string path)
    {
        try { return File.GetLastWriteTimeUtc(path).Ticks; } catch (Exception) { return 0; }
    }

    private static SaveSummary Rebuild(string name, AgeDatabase ages, TechDatabase techs)
    {
        long ticks = FileTicks(JsonPath(name));
        if (!SaveSystem.TryRead(JsonPath(name), out SaveData data, out _, ages, techs))
        {
            return new SaveSummary { Damaged = true, SavedUtcTicks = ticks, Kind = (int)KindOf(name) };
        }

        string ageName = ages != null && ages.IsValidIndex(data.Age) ? ages[data.Age].DisplayName : "";
        SaveSummary summary = SaveSummary.From(data, KindOf(name), ticks, "", ageName);
        try { File.WriteAllText(InfoPath(name), JsonUtility.ToJson(summary)); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        return summary;
    }

    // --- The old single-slot save ---

    // Copies (never moves) the pre-M19 city.json into the folder as "Imported city", once: a marker file records that
    // it was tried, so deleting the import doesn't bring it back. Returns true when a save was imported.
    public static bool ImportLegacy(string legacyPath, AgeDatabase ages = null, TechDatabase techs = null)
    {
        string marker = Path.Combine(Root, LegacyMarker);
        if (File.Exists(marker) || string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath)) return false;

        bool imported = false;
        try
        {
            Directory.CreateDirectory(Root);
            if (!Exists(LegacyImportName) && SaveSystem.TryRead(legacyPath, out SaveData data, out _, ages, techs))
            {
                File.Copy(legacyPath, JsonPath(LegacyImportName));
                string ageName = ages != null && ages.IsValidIndex(data.Age) ? ages[data.Age].DisplayName : "";
                SaveSummary summary = SaveSummary.From(data, SaveKind.Manual, FileTicks(legacyPath), "", ageName);
                File.WriteAllText(InfoPath(LegacyImportName), JsonUtility.ToJson(summary));
                imported = true;
            }
            File.WriteAllText(marker, "");
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
        }
        return imported;
    }
}
