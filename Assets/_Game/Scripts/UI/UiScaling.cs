using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Settings UI scale (M19d): every ScaleWithScreenSize canvas gets its reference resolution divided by the scale, so
// 125% makes everything 25% bigger. The scene's own reference resolution (1920 x 1080) is remembered per canvas.
public static class UiScaling
{
    private static readonly Dictionary<CanvasScaler, Vector2> s_Base = new();

    public static void Apply()
    {
        float scale = Mathf.Max(0.5f, GameSettings.UiScale);
        foreach (CanvasScaler scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include))
        {
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            if (!s_Base.TryGetValue(scaler, out Vector2 reference))
            {
                reference = scaler.referenceResolution;
                s_Base[scaler] = reference;
            }
            scaler.referenceResolution = reference / scale;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_Base.Clear();
}
