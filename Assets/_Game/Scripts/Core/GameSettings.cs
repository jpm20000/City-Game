using System;
using System.Collections.Generic;
using UnityEngine;

public enum SoundChannel { Music, Ambience, Sfx }

// Every player setting (M19d), in PlayerPrefs under CityGame.* (not in the city save). It took over SoundSettings and
// DayNightCycle.LockToDay from M18 and kept their keys (CityGame.Audio.*, CityGame.Visual.LockDay, with the stored
// types), so settings made before M19 carry over. Values are read lazily and cached; every write raises Changed.
// A missing or blocked PlayerPrefs means the defaults, never an error. Applying a setting to the game (the screen,
// the UI scale) is done by the code that owns it, from the Settings panel and once at startup (GameFlow).
public static class GameSettings
{
    private const string Prefix = "CityGame.";

    // Display modes, in the order of the Settings stepper.
    public const int ModeFullScreen = 0, ModeBorderless = 1, ModeWindowed = 2;

    public static readonly float[] UiScales = { 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };
    public static readonly int[] FrameCaps = { 0, 30, 60, 120, 144, 240 };
    public static readonly float[] TooltipDelays = { 0f, 0.3f, 0.6f, 1f };
    public static readonly int[] AutosaveChoices = { 0, 1, 3, 6 };     // months between autosaves; 0 = off
    public static readonly float[] PanSpeeds = { 0.5f, 0.75f, 1f, 1.5f, 2f };

    private static readonly Dictionary<string, float> s_Floats = new();
    private static readonly Dictionary<string, int> s_Ints = new();

    public static event Action Changed;

    // ---- storage ----------------------------------------------------------------------------------------

    private static float GetFloat(string key, float fallback)
    {
        if (s_Floats.TryGetValue(key, out float cached)) return cached;
        float value = fallback;
        try { value = PlayerPrefs.GetFloat(Prefix + key, fallback); } catch (Exception) { }
        s_Floats[key] = value;
        return value;
    }

    private static void SetFloat(string key, float value)
    {
        s_Floats[key] = value;
        try { PlayerPrefs.SetFloat(Prefix + key, value); } catch (Exception) { }
        Changed?.Invoke();
    }

    private static int GetInt(string key, int fallback)
    {
        if (s_Ints.TryGetValue(key, out int cached)) return cached;
        int value = fallback;
        try { value = PlayerPrefs.GetInt(Prefix + key, fallback); } catch (Exception) { }
        s_Ints[key] = value;
        return value;
    }

    private static void SetInt(string key, int value)
    {
        s_Ints[key] = value;
        try { PlayerPrefs.SetInt(Prefix + key, value); } catch (Exception) { }
        Changed?.Invoke();
    }

    private static bool Has(string key)
    {
        try { return PlayerPrefs.HasKey(Prefix + key); } catch (Exception) { return false; }
    }

    private static void Forget(params string[] keys)
    {
        foreach (string key in keys)
        {
            s_Floats.Remove(key);
            s_Ints.Remove(key);
            try { PlayerPrefs.DeleteKey(Prefix + key); } catch (Exception) { }
        }
        Changed?.Invoke();
    }

    // ---- audio (M18e keys: CityGame.Audio.*, floats) -----------------------------------------------------

    private static readonly string[] s_ChannelKeys = { "Audio.Music", "Audio.Ambience", "Audio.Sfx" };
    private static readonly float[] s_ChannelDefaults = { 0.5f, 0.6f, 0.8f };

    public static float Master
    {
        get => GetFloat("Audio.Master", 0.8f);
        set => SetFloat("Audio.Master", Mathf.Clamp01(value));
    }

    public static bool Mute
    {
        get => GetFloat("Audio.Mute", 0f) > 0.5f;
        set => SetFloat("Audio.Mute", value ? 1f : 0f);
    }

    public static float Get(SoundChannel channel) => GetFloat(s_ChannelKeys[(int)channel], s_ChannelDefaults[(int)channel]);

    public static void Set(SoundChannel channel, float value) => SetFloat(s_ChannelKeys[(int)channel], Mathf.Clamp01(value));

    // The volume a channel actually plays at (0 while muted).
    public static float Effective(SoundChannel channel) => Mute ? 0f : Master * Get(channel);

    public static void ResetAudio() => Forget("Audio.Master", "Audio.Mute", s_ChannelKeys[0], s_ChannelKeys[1], s_ChannelKeys[2]);

    // ---- display ----------------------------------------------------------------------------------------

    public static int DisplayMode
    {
        get => Mathf.Clamp(GetInt("Display.Mode", ModeBorderless), ModeFullScreen, ModeWindowed);
        set => SetInt("Display.Mode", Mathf.Clamp(value, ModeFullScreen, ModeWindowed));
    }

    // 0 x 0 = the current screen resolution (nothing was chosen).
    public static Vector2Int Resolution
    {
        get => new Vector2Int(GetInt("Display.Width", 0), GetInt("Display.Height", 0));
        set
        {
            SetInt("Display.Width", value.x);
            SetInt("Display.Height", value.y);
        }
    }

    public static bool VSync
    {
        get => GetInt("Display.VSync", 1) != 0;
        set => SetInt("Display.VSync", value ? 1 : 0);
    }

    // Frames per second cap while VSync is off; 0 = unlimited.
    public static int FrameCap
    {
        get => GetInt("Display.FrameCap", 0);
        set => SetInt("Display.FrameCap", Mathf.Max(0, value));
    }

    // True once the player has chosen anything on the Display tab: until then the project's own settings stand.
    public static bool HasDisplayChoice => Has("Display.Mode") || Has("Display.Width") || Has("Display.VSync") || Has("Display.FrameCap");

    public static void ResetDisplay() => Forget("Display.Mode", "Display.Width", "Display.Height", "Display.VSync", "Display.FrameCap");

    private static FullScreenMode ScreenMode(int mode)
    {
        return mode == ModeFullScreen ? FullScreenMode.ExclusiveFullScreen : mode == ModeWindowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
    }

    // Applies the window mode, resolution, VSync and frame cap. Does nothing until the player has chosen something.
    public static void ApplyDisplay(bool force = false)
    {
#if UNITY_WEBGL
        return;   // the browser owns the window, resolution and frame rate
#else
        if (!force && !HasDisplayChoice) return;
        Vector2Int size = Resolution;
        FullScreenMode mode = ScreenMode(DisplayMode);
        if (size.x > 0 && size.y > 0) Screen.SetResolution(size.x, size.y, mode);
        else Screen.fullScreenMode = mode;
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = VSync || FrameCap <= 0 ? -1 : FrameCap;
#endif
    }

    // ---- interface and camera ---------------------------------------------------------------------------

    public static float UiScale
    {
        get => Mathf.Clamp(GetFloat("UI.Scale", 1f), UiScales[0], UiScales[UiScales.Length - 1]);
        set => SetFloat("UI.Scale", value);
    }

    // Keeps the city in daylight (M18d). Key and int format as before M19.
    public static bool LockToDay
    {
        get => GetInt("Visual.LockDay", 0) != 0;
        set => SetInt("Visual.LockDay", value ? 1 : 0);
    }

    public static float PanSpeed
    {
        get => Mathf.Clamp(GetFloat("Camera.PanSpeed", 1f), 0.25f, 3f);
        set => SetFloat("Camera.PanSpeed", value);
    }

    public static bool EdgeScroll
    {
        get => GetInt("Camera.EdgeScroll", 0) != 0;
        set => SetInt("Camera.EdgeScroll", value ? 1 : 0);
    }

    // Seconds the pointer must rest on a toolbar button before its tooltip shows.
    public static float TooltipDelay
    {
        get => Mathf.Max(0f, GetFloat("UI.TooltipDelay", 0f));
        set => SetFloat("UI.TooltipDelay", value);
    }

    public static void ResetInterface() => Forget("UI.Scale", "Visual.LockDay", "Camera.PanSpeed", "Camera.EdgeScroll", "UI.TooltipDelay");

    // ---- gameplay ---------------------------------------------------------------------------------------

    // Game months between autosaves; 0 = off.
    public static int AutosaveMonths
    {
        get => Mathf.Max(0, GetInt("Gameplay.AutosaveMonths", 3));
        set => SetInt("Gameplay.AutosaveMonths", Mathf.Max(0, value));
    }

    // The New City dialog's Disasters & events switch starts here.
    public static bool DisastersByDefault
    {
        get => GetInt("Gameplay.Disasters", 1) != 0;
        set => SetInt("Gameplay.Disasters", value ? 1 : 0);
    }

    // Whether a random event stops the clock until it is answered (off: it waits its days, then takes the last option).
    public static bool PauseOnEvents
    {
        get => GetInt("Gameplay.PauseOnEvents", 1) != 0;
        set => SetInt("Gameplay.PauseOnEvents", value ? 1 : 0);
    }

    // Ask before demolishing a placed building (a plant, a well, a school...). Roads and zones never ask.
    public static bool ConfirmDemolish
    {
        get => GetInt("Gameplay.ConfirmDemolish", 0) != 0;
        set => SetInt("Gameplay.ConfirmDemolish", value ? 1 : 0);
    }

    public static void ResetGameplay() => Forget("Gameplay.AutosaveMonths", "Gameplay.Disasters", "Gameplay.PauseOnEvents", "Gameplay.ConfirmDemolish");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_Floats.Clear();
        s_Ints.Clear();
        Changed = null;
    }
}
