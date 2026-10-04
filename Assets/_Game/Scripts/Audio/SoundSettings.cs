using System;
using UnityEngine;

public enum SoundChannel { Music, Ambience, Sfx }

// Player volume settings (M18e), stored in PlayerPrefs (not in the city save): a master volume, one per
// channel and a mute switch. M19's settings menu will take them over.
public static class SoundSettings
{
    private const string k_Prefix = "CityGame.Audio.";
    private static float s_Master = -1f;
    private static readonly float[] s_Channels = { -1f, -1f, -1f };
    private static readonly float[] s_Defaults = { 0.5f, 0.6f, 0.8f };
    private static int s_Mute = -1;

    public static event Action Changed;

    public static float Master
    {
        get { if (s_Master < 0f) s_Master = Read("Master", 0.8f); return s_Master; }
        set { s_Master = Mathf.Clamp01(value); Write("Master", s_Master); Changed?.Invoke(); }
    }

    public static bool Mute
    {
        get { if (s_Mute < 0) s_Mute = Read("Mute", 0f) > 0.5f ? 1 : 0; return s_Mute == 1; }
        set { s_Mute = value ? 1 : 0; Write("Mute", s_Mute); Changed?.Invoke(); }
    }

    public static float Get(SoundChannel channel)
    {
        int i = (int)channel;
        if (s_Channels[i] < 0f) s_Channels[i] = Read(channel.ToString(), s_Defaults[i]);
        return s_Channels[i];
    }

    public static void Set(SoundChannel channel, float value)
    {
        int i = (int)channel;
        s_Channels[i] = Mathf.Clamp01(value);
        Write(channel.ToString(), s_Channels[i]);
        Changed?.Invoke();
    }

    // The volume a channel actually plays at (0 while muted).
    public static float Effective(SoundChannel channel) => Mute ? 0f : Master * Get(channel);

    private static float Read(string key, float fallback)
    {
        try { return PlayerPrefs.GetFloat(k_Prefix + key, fallback); } catch (Exception) { return fallback; }
    }

    private static void Write(string key, float value)
    {
        try { PlayerPrefs.SetFloat(k_Prefix + key, value); } catch (Exception) { }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_Master = -1f;
        s_Mute = -1;
        for (int i = 0; i < s_Channels.Length; i++) s_Channels[i] = -1f;
        Changed = null;
    }
}
