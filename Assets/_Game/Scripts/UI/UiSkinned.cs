using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// (M32e) On an Image painted with the frame, HUD-bar or button art: keeps the art's own colour (white, or a state
// tint such as the tech panel's) and draws it times the current age tint (UiTheme.SkinTint). Every enabled one is
// re-tinted when the age changes; one enabled later picks the tint up then.
[DisallowMultipleComponent]
public sealed class UiSkinned : MonoBehaviour
{
    private static readonly List<UiSkinned> s_Live = new List<UiSkinned>();

    private Image m_Image;
    private Color m_Own = Color.white;

    public Color Own => m_Own;

    public void Set(Image image, Color own)
    {
        m_Image = image;
        m_Own = own;
        Apply();
    }

    public void Apply()
    {
        if (m_Image == null) m_Image = GetComponent<Image>();
        if (m_Image != null) m_Image.color = m_Own * UiTheme.SkinTint;
    }

    public static void RefreshAll()
    {
        for (int i = s_Live.Count - 1; i >= 0; i--)
        {
            if (s_Live[i] == null) s_Live.RemoveAt(i);
            else s_Live[i].Apply();
        }
    }

    private void OnEnable()
    {
        s_Live.Add(this);
        Apply();
    }

    private void OnDisable() => s_Live.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_Live.Clear();
}
