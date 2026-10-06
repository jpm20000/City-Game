using System;
using System.Collections.Generic;

// One row of the build menu (M25c), flattened from a BuildingDefinition by the UI so the search itself stays pure.
public struct BuildMenuEntry
{
    public string Id;
    public string Name;
    public string Group;      // toolbar group name, e.g. "Services"
    public string AgeName;    // age of the unlocking tech, "" when it needs none
    public int Age;           // that age's index, -1 when it needs no tech
    public string Effects;    // searchable words and numbers: "housing 4 jobs 8 police"
    public bool Custom;
    public bool Locked;
}

// The build menu's search and filters. Every word of the query must match somewhere; a hit in the name beats one in
// the id, the group / age or the effect words. Unlocked entries come before locked ones, then by relevance, then in the
// database's own order (so an empty query lists the database as it is).
public static class BuildMenuFilter
{
    // group null / "" = any group; age < 0 = any age; the result is indices into `entries`, best first.
    public static List<int> Apply(IReadOnlyList<BuildMenuEntry> entries, string query, string group, int age, bool customOnly, bool showLocked)
    {
        string[] words = (query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<(int index, int score)>();
        for (int i = 0; i < entries.Count; i++)
        {
            BuildMenuEntry entry = entries[i];
            if (entry.Locked && !showLocked) continue;
            if (customOnly && !entry.Custom) continue;
            if (!string.IsNullOrEmpty(group) && !string.Equals(entry.Group, group, StringComparison.OrdinalIgnoreCase)) continue;
            if (age >= 0 && entry.Age != age) continue;

            int total = 0;
            bool all = true;
            foreach (string word in words)
            {
                int score = Score(entry, word);
                if (score < 0) { all = false; break; }
                total += score;
            }
            if (all) hits.Add((i, total));
        }
        hits.Sort((a, b) =>
        {
            int byLock = entries[a.index].Locked.CompareTo(entries[b.index].Locked);
            if (byLock != 0) return byLock;
            int byScore = a.score.CompareTo(b.score);
            return byScore != 0 ? byScore : a.index.CompareTo(b.index);
        });
        var result = new List<int>(hits.Count);
        foreach ((int index, int _) in hits) result.Add(index);
        return result;
    }

    // -1 = no match; lower is better.
    private static int Score(BuildMenuEntry entry, string word)
    {
        string name = entry.Name ?? "";
        if (name.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 0;
        if (WordStartsWith(name, word)) return 1;
        if (Contains(name, word)) return 2;
        if (Contains(entry.Id, word)) return 3;
        if (Contains(entry.Group, word) || Contains(entry.AgeName, word)) return 4;
        if (entry.Custom && "custom".StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 4;
        if (Contains(entry.Effects, word)) return 5;
        return -1;
    }

    private static bool Contains(string text, string word) => !string.IsNullOrEmpty(text) && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool WordStartsWith(string text, string word)
    {
        for (int i = 1; i < text.Length; i++)
        {
            if (text[i - 1] == ' ' && string.Compare(text, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0) return true;
        }
        return false;
    }
}
