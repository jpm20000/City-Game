using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M19e play-through of the Controls tab, UI-only: a virtual keyboard and mouse open Settings, rebind keys by click and
// key press, and check the result through the game's own tools. Run in Play mode from a short RunCommand:
//   UnityEngine.Object.FindAnyObjectByType<GameManager>().StartCoroutine(ControlsDriver.Run(logPath));
// then read the log for FAIL / DONE lines. Resets its PlayerPrefs at the end.
public static class ControlsDriver
{
    private static Mouse s_Mouse;
    private static Keyboard s_Keyboard;
    private static string s_Log;
    private static int s_Fails;

    private static void Log(string line) => File.AppendAllText(s_Log, $"[{Time.frameCount}] {line}\n");
    private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    private static void Check(bool ok, string what)
    {
        if (!ok) s_Fails++;
        Log((ok ? "PASS " : "FAIL ") + what);
    }

    private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState(keys));
    private static IEnumerator Press(Key key) { Keys(key); yield return Frames(2); Keys(); yield return Frames(3); }

    private static IEnumerator ClickAt(Vector2 pos)
    {
        InputSystem.QueueStateEvent(s_Mouse, new MouseState { position = pos }); yield return Frames(2);
        InputSystem.QueueStateEvent(s_Mouse, new MouseState { position = pos }.WithButton(MouseButton.Left)); yield return Frames(2);
        InputSystem.QueueStateEvent(s_Mouse, new MouseState { position = pos }); yield return Frames(2);
    }

    private static IEnumerator ClickUi(GameObject go)
    {
        if (go == null || !go.activeInHierarchy) { Check(false, "missing ui object"); yield break; }
        var rt = (RectTransform)go.transform;
        yield return ClickAt(RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center)));
    }

    private static GameObject Find(string path) => GameObject.Find(path);

    // The row of the Controls tab with this label: its n-th button (0 = the key, 1 = Reset).
    private static GameObject RowButton(string label, int n)
    {
        Transform window = Find("UI/SettingsPanel/Panel").transform;
        foreach (TMP_Text text in window.GetComponentsInChildren<TMP_Text>())
        {
            if (text.text != label || text.GetComponent<LayoutElement>() == null) continue;
            Button[] buttons = text.transform.parent.GetComponentsInChildren<Button>();
            return n < buttons.Length ? buttons[n].gameObject : null;
        }
        return null;
    }

    private static GameObject Named(string name)
    {
        foreach (Button button in Find("UI/SettingsPanel/Panel").GetComponentsInChildren<Button>())
        {
            if (button.name == name) return button.gameObject;
        }
        return null;
    }

    private static string Status()
    {
        GameObject go = null;
        foreach (TMP_Text text in Find("UI/SettingsPanel/Panel").GetComponentsInChildren<TMP_Text>())
        {
            if (text.name == "ControlsStatus") go = text.gameObject;
        }
        return go != null ? go.GetComponent<TMP_Text>().text : "(no status)";
    }

    private static string Key(string action) => KeyBindings.Label(action);

    private static IEnumerator Rebind(string row, Key key)
    {
        yield return ClickUi(RowButton(row, 0));
        yield return Frames(2);
        Check(KeyBindings.Rebinding, $"{row}: waiting for a key");
        // Held for a while: the rebind waits a moment for a better match before it takes a key.
        Keys(key);
        yield return new WaitForSecondsRealtime(0.3f);
        Keys();
        yield return Frames(4);
    }

    public static IEnumerator Run(string logPath)
    {
        s_Log = logPath;
        s_Fails = 0;
        File.WriteAllText(s_Log, string.Empty);
        foreach (InputDevice device in InputSystem.devices.ToArray())
        {
            if (device.name.StartsWith("Virtual")) InputSystem.RemoveDevice(device);
        }
        Mouse realMouse = Mouse.current;
        Keyboard realKeyboard = Keyboard.current;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        if (realMouse != null) InputSystem.DisableDevice(realMouse);
        if (realKeyboard != null) InputSystem.DisableDevice(realKeyboard);
        s_Mouse = InputSystem.AddDevice<Mouse>("VirtualMouse");
        s_Keyboard = InputSystem.AddDevice<Keyboard>("VirtualKeyboard");
        s_Mouse.MakeCurrent();
        s_Keyboard.MakeCurrent();
        try
        {
            KeyBindings.ResetAll();
            GameFlow flow = GameFlow.Instance;
            if (flow.State == GameFlowState.MainMenu) flow.StartPlaying();
            yield return Frames(5);
            var placement = Object.FindAnyObjectByType<PlacementController>();

            // Open Settings from the HUD and go to Controls.
            yield return ClickUi(Find("UI/HUD/Game/SettingsButton") ?? GameObject.Find("SettingsButton"));
            yield return Frames(3);
            Check(flow.Settings.IsOpen, "Settings opens");
            yield return ClickUi(Named("ControlsButton"));
            yield return Frames(3);
            Check(flow.Settings.CurrentTab == SettingsPanel.Tab.Controls, "Controls tab");
            Check(Key("RoadTool") == "B" && Key("Rotate") == "R" && Key("PipeTool") == "P", "defaults B / R / P");

            // Road tool -> N.
            yield return Rebind("Road tool", UnityEngine.InputSystem.Key.N);
            Check(Key("RoadTool") == "N", "Road tool is N: " + Key("RoadTool"));
            Check(Status().Contains("Road tool"), "status: " + Status());
            Check(KeyBindings.Fill("Road [B]") == "Road [N]", "tooltip text follows: " + KeyBindings.Fill("Road [B]"));

            // Esc cancels a rebind without closing the window.
            yield return ClickUi(RowButton("Rotate building", 0));
            yield return Frames(2);
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            Check(!KeyBindings.Rebinding && Key("Rotate") == "R", "Esc cancels the rebind");
            Check(flow.Settings.IsOpen, "the window stays open after the cancelling Esc");

            // A fixed key (F1) is refused.
            yield return Rebind("Rotate building", UnityEngine.InputSystem.Key.F1);
            Check(Key("Rotate") == "R" && Status().Contains("reserved"), "F1 is refused: " + Status());

            // Pipe tool -> N: a conflict swaps with the Road tool.
            yield return Rebind("Pipe tool", UnityEngine.InputSystem.Key.N);
            Check(Key("PipeTool") == "N" && Key("RoadTool") == "P", $"swap: pipe {Key("PipeTool")}, road {Key("RoadTool")}");
            Check(Status().Contains("took"), "status names the swap: " + Status());

            // Reset the pipe row: back to P, so the road tool gets N back.
            yield return ClickUi(RowButton("Pipe tool", 1));
            yield return Frames(2);
            Check(Key("PipeTool") == "P" && Key("RoadTool") == "N", $"row reset: pipe {Key("PipeTool")}, road {Key("RoadTool")}");

            // The saved overrides survive a restart: a fresh copy of the actions loads them.
            var copy = Object.Instantiate(InputReader.Instance.Asset);
            KeyBindings.Load(copy);
            Check(copy.FindAction("Gameplay/RoadTool").bindings[0].effectivePath == "<Keyboard>/n", "a fresh copy of the actions loads the saved N");

            // Close Settings, then N draws roads and B does nothing.
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            Check(!flow.Settings.IsOpen, "Esc closes Settings");
            yield return Press(UnityEngine.InputSystem.Key.B);
            Check(placement.CurrentMode != PlacementController.Mode.Road, "B no longer selects the road tool");
            yield return Press(UnityEngine.InputSystem.Key.N);
            Check(placement.CurrentMode == PlacementController.Mode.Road, "N selects the road tool: " + placement.CurrentMode);
            yield return Press(UnityEngine.InputSystem.Key.Escape);

            // Reset this tab restores everything.
            yield return ClickUi(Find("UI/HUD/Game/SettingsButton") ?? GameObject.Find("SettingsButton"));
            yield return Frames(3);
            yield return ClickUi(Named("ControlsButton"));
            yield return Frames(3);
            yield return ClickUi(Named("Reset this tabButton"));
            yield return Frames(3);
            Check(Key("RoadTool") == "B" && PlayerPrefs.GetString(KeyBindings.PrefsKey, "") == "", "Reset this tab: keys back, prefs cleared");
            yield return Press(UnityEngine.InputSystem.Key.Escape);
        }
        finally
        {
            KeyBindings.ResetAll();
            foreach (InputDevice device in InputSystem.devices.ToArray())
            {
                if (device.name.StartsWith("Virtual")) InputSystem.RemoveDevice(device);
            }
            if (realMouse != null) InputSystem.EnableDevice(realMouse);
            if (realKeyboard != null) InputSystem.EnableDevice(realKeyboard);
            Log($"DONE, {s_Fails} failed checks");
            Log("devices restored");
        }
    }
}
