using System;
using UnityEngine;
using UnityEngine.InputSystem;

// The rebindable keys (M19e): which bindings the Controls tab offers, how a key is shown, rebinding with a conflict swap,
// and the saved overrides (PlayerPrefs "CityGame.Input.Bindings"; not part of the city save). Esc, F1, the 1-4 speed keys,
// the arrow keys, Backspace and the mouse are fixed. Only the first binding of an action is rebound (WASD for Pan, Del
// for Demolish); the others stay as a second way in.
public static class KeyBindings
{
    public const string PrefsKey = "CityGame.Input.Bindings";

    public sealed class Entry
    {
        public string Label;
        public string Action;
        public string Part;   // the composite part ("Up"), or null for a plain binding
    }

    public static readonly Entry[] Entries =
    {
        new Entry { Label = "Pan up", Action = "Pan", Part = "Up" },
        new Entry { Label = "Pan down", Action = "Pan", Part = "Down" },
        new Entry { Label = "Pan left", Action = "Pan", Part = "Left" },
        new Entry { Label = "Pan right", Action = "Pan", Part = "Right" },
        new Entry { Label = "Rotate building", Action = "Rotate" },
        new Entry { Label = "Rotate view left", Action = "RotateCameraLeft" },
        new Entry { Label = "Rotate view right", Action = "RotateCameraRight" },
        new Entry { Label = "Demolish tool", Action = "Demolish" },
        new Entry { Label = "Road tool", Action = "RoadTool" },
        new Entry { Label = "Pipe tool", Action = "PipeTool" },
        new Entry { Label = "Cycle info view", Action = "CycleOverlay" },
        new Entry { Label = "Quick save", Action = "QuickSave" },
        new Entry { Label = "Quick load", Action = "QuickLoad" },
    };

    // The old hard-coded hints in tooltips, toasts and hints; Fill swaps them for the current keys.
    private static readonly (string token, string action)[] s_Tokens =
    {
        ("[V]", "CycleOverlay"), ("[Del]", "Demolish"), ("[B]", "RoadTool"), ("[P]", "PipeTool"), ("[R]", "Rotate"),
        ("[Q]", "RotateCameraLeft"), ("[E]", "RotateCameraRight"),
    };

    public static bool Rebinding { get; private set; }
    public static int FinishedFrame { get; private set; } = -10;

    // True while a rebind runs and for the frame it ends (the Esc that cancels it must not also close the window).
    public static bool SwallowEscape => Rebinding || Time.frameCount - FinishedFrame <= 1;

    private static InputActionAsset Asset => InputReader.Instance != null ? InputReader.Instance.Asset : null;

    private static int IndexOf(InputAction action, Entry entry)
    {
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            InputBinding b = bindings[i];
            bool match = entry.Part == null ? !b.isComposite && !b.isPartOfComposite : b.isPartOfComposite && b.name == entry.Part;
            if (match) return i;
        }
        return -1;
    }

    private static bool Resolve(Entry entry, out InputAction action, out int index)
    {
        action = null;
        index = -1;
        InputActionAsset asset = Asset;
        if (asset == null) return false;
        action = asset.FindAction("Gameplay/" + entry.Action, false);
        if (action == null) return false;
        index = IndexOf(action, entry);
        return index >= 0;
    }

    public static string PathOf(Entry entry)
    {
        return Resolve(entry, out InputAction action, out int index) ? action.bindings[index].effectivePath : string.Empty;
    }

    public static string KeyName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "-";
        string text = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        return text == "Delete" ? "Del" : text;
    }

    public static string Label(Entry entry) => KeyName(PathOf(entry));

    // The key of an action's first entry, for hints.
    public static string Label(string action)
    {
        foreach (Entry entry in Entries)
        {
            if (entry.Action == action && entry.Part == null) return Label(entry);
        }
        return "?";
    }

    public static bool IsDefault(Entry entry)
    {
        return Resolve(entry, out InputAction action, out int index) && string.IsNullOrEmpty(action.bindings[index].overridePath);
    }

    // "[V]" becomes "[Tab]" and so on, wherever a tooltip, toast or hint names a rebindable key.
    public static string Fill(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0 || Asset == null) return text;
        foreach ((string token, string action) in s_Tokens)
        {
            if (text.Contains(token)) text = text.Replace(token, "[" + Label(action) + "]");
        }
        return text;
    }

    // ---- changing keys ----------------------------------------------------------------------------------

    // Listens for the next key: Esc cancels. `done` gets a line for the player (what changed, a swap, or why not).
    public static void Rebind(Entry entry, Action<string> done)
    {
        if (Rebinding || !Resolve(entry, out InputAction action, out int index)) return;
        string old = action.bindings[index].effectivePath;
        Rebinding = true;
        action.Disable();
        action.PerformInteractiveRebinding(index)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnCancel(op =>
            {
                op.Dispose();
                Finish(action);
                done("Cancelled.");
            })
            .OnComplete(op =>
            {
                op.Dispose();
                string message = Settle(entry, action, index, old);
                Finish(action);
                Save();
                done(message);
            })
            .Start();
    }

    private static void Finish(InputAction action)
    {
        Rebinding = false;
        FinishedFrame = Time.frameCount;
        action.Enable();
    }

    // The new key is in place: refuse a fixed key, swap with another rebindable action that had it.
    private static string Settle(Entry entry, InputAction action, int index, string old)
    {
        string chosen = action.bindings[index].effectivePath;
        if (string.Equals(chosen, old, StringComparison.OrdinalIgnoreCase)) return KeyName(chosen) + " is unchanged.";

        if (IsFixed(chosen))
        {
            SetPath(action, index, old);
            return KeyName(chosen) + " is reserved for something else.";
        }

        foreach (Entry other in Entries)
        {
            if (other == entry || !Resolve(other, out InputAction otherAction, out int otherIndex)) continue;
            if (!string.Equals(otherAction.bindings[otherIndex].effectivePath, chosen, StringComparison.OrdinalIgnoreCase)) continue;
            SetPath(otherAction, otherIndex, old);
            return $"{KeyName(chosen)} now {entry.Label.ToLowerInvariant()}; {other.Label.ToLowerInvariant()} took {KeyName(old)}.";
        }
        return $"{entry.Label}: {KeyName(chosen)}.";
    }

    // A key some other, non-rebindable binding uses (Esc, F1, 1-4, arrows, Backspace).
    private static bool IsFixed(string path)
    {
        InputActionAsset asset = Asset;
        if (asset == null) return false;
        foreach (InputAction action in asset.FindActionMap("Gameplay").actions)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isComposite || !string.Equals(bindings[i].effectivePath, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsRebindable(action, i)) return true;
            }
        }
        return false;
    }

    private static bool IsRebindable(InputAction action, int index)
    {
        foreach (Entry entry in Entries)
        {
            if (entry.Action == action.name && IndexOf(action, entry) == index) return true;
        }
        return false;
    }

    private static void SetPath(InputAction action, int index, string path)
    {
        if (string.Equals(action.bindings[index].path, path, StringComparison.OrdinalIgnoreCase)) action.RemoveBindingOverride(index);
        else action.ApplyBindingOverride(index, path);
    }

    public static void Reset(Entry entry)
    {
        if (!Resolve(entry, out InputAction action, out int index)) return;
        string target = action.bindings[index].path;
        // A default key another entry now holds goes back to this one by swapping.
        foreach (Entry other in Entries)
        {
            if (other == entry || !Resolve(other, out InputAction otherAction, out int otherIndex)) continue;
            if (!string.Equals(otherAction.bindings[otherIndex].effectivePath, target, StringComparison.OrdinalIgnoreCase)) continue;
            SetPath(otherAction, otherIndex, action.bindings[index].effectivePath);
        }
        action.RemoveBindingOverride(index);
        Save();
    }

    public static void ResetAll()
    {
        Asset?.RemoveAllBindingOverrides();
        try { PlayerPrefs.DeleteKey(PrefsKey); } catch (Exception) { }
    }

    public static void Save()
    {
        if (Asset == null) return;
        try { PlayerPrefs.SetString(PrefsKey, Asset.SaveBindingOverridesAsJson()); } catch (Exception) { }
    }

    public static void Load(InputActionAsset asset)
    {
        try
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json)) asset.LoadBindingOverridesFromJson(json);
        }
        catch (Exception) { }
    }
}
