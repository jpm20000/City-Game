using System.Runtime.InteropServices;
using UnityEngine;

// M28: makes the browser build's saves and settings outlive the page. On WebGL persistentDataPath is an in-memory file
// system backed by IndexedDB that is only written by FS.syncfs (ChronoFs.jslib): every SaveSlots change is flushed
// at once, and a settings change saves PlayerPrefs (which the browser only keeps after PlayerPrefs.Save) half a second
// after the last one (a dragged slider writes many) and then flushes. Everywhere else every call here does nothing.
public static class WebStorage
{
    private const float PrefsDelay = 0.5f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void ChronoSyncFs();
    [DllImport("__Internal")] private static extern void ChronoInstallUnloadSync();
#endif

    private static bool s_PrefsDirty;
    private static float s_PrefsDueAt;

    public static bool PrefsPending => s_PrefsDirty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SaveSlots.Changed -= Flush;
        SaveSlots.Changed += Flush;
        ChronoInstallUnloadSync();
        var pump = new GameObject("WebStorage") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(pump);
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

    private void OnApplicationFocus(bool focus)
    {
        if (!focus) WebStorage.Pump(true);
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause) WebStorage.Pump(true);
    }
}
