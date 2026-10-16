using System;
using System.Runtime.InteropServices;
using UnityEngine;

// M28: makes the browser build's saves and settings outlive the page. On WebGL persistentDataPath is an in-memory file
// system backed by IndexedDB that is only written by FS.syncfs (ChronoFs.jslib): every SaveSlots change is flushed
// at once, and a settings change saves PlayerPrefs (which the browser only keeps after PlayerPrefs.Save) half a second
// after the last one (a dragged slider writes many) and then flushes. M28b: Download / PickFile let the Load panel
// export and import city files. Everywhere else every call here does nothing (FilesSupported is false).
public static class WebStorage
{
    private const float PrefsDelay = 0.5f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void ChronoSyncFs();
    [DllImport("__Internal")] private static extern void ChronoInstallUnloadSync();
    [DllImport("__Internal")] private static extern void ChronoDownload(string fileName, string text);
    [DllImport("__Internal")] private static extern void ChronoPickFile(string gameObject, string method);
#endif

    internal const string PumpName = "WebStorage";

    private static bool s_PrefsDirty;
    private static float s_PrefsDueAt;
    private static Action<string, string> s_Picked;

#if UNITY_WEBGL && !UNITY_EDITOR
    public static bool FilesSupported => true;
#else
    public static bool FilesSupported => false;
#endif

    public static bool PrefsPending => s_PrefsDirty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SaveSlots.Changed -= Flush;
        SaveSlots.Changed += Flush;
        ChronoInstallUnloadSync();
        var pump = new GameObject(PumpName);   // not hidden: the file picker's SendMessage finds it by name
        UnityEngine.Object.DontDestroyOnLoad(pump);
        pump.AddComponent<WebStoragePump>();
#endif
    }

    // Writes the browser's file system to IndexedDB now.
    public static void Flush()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ChronoSyncFs();
#endif
    }

    // A PlayerPrefs value changed: save them soon.
    public static void PrefsChanged()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        s_PrefsDirty = true;
        s_PrefsDueAt = Time.realtimeSinceStartup + PrefsDelay;
#endif
    }

    // Saves text as a file in the player's downloads.
    public static void Download(string fileName, string text)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ChronoDownload(fileName, text);
#endif
    }

    // Asks the player for a .json file; picked(file name, text) runs when one is chosen (never on cancel).
    public static void PickFile(Action<string, string> picked)
    {
        s_Picked = picked;
#if UNITY_WEBGL && !UNITY_EDITOR
        ChronoPickFile(PumpName, nameof(WebStoragePump.OnFilePicked));
#endif
    }

    internal static void FilePicked(string payload)
    {
        Action<string, string> picked = s_Picked;
        s_Picked = null;
        if (picked == null || string.IsNullOrEmpty(payload)) return;
        int newline = payload.IndexOf('\n');
        if (newline < 0) return;
        picked(payload.Substring(0, newline), payload.Substring(newline + 1));
    }

    internal static void Pump(bool now)
    {
        if (!s_PrefsDirty || (!now && Time.realtimeSinceStartup < s_PrefsDueAt)) return;
        s_PrefsDirty = false;
        try { PlayerPrefs.Save(); } catch (System.Exception) { }
        Flush();
    }
}

// Runs WebStorage's delayed PlayerPrefs save (WebGL only; created by WebStorage.Install).
internal sealed class WebStoragePump : MonoBehaviour
{
    private void Update() => WebStorage.Pump(false);

    // ChronoPickFile's SendMessage target: "<file name>\n<text>".
    public void OnFilePicked(string payload) => WebStorage.FilePicked(payload);

    private void OnApplicationFocus(bool focus)
    {
        if (!focus) WebStorage.Pump(true);
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause) WebStorage.Pump(true);
    }
}
