using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The UI's look as data (M32a): the palette UiKit draws with, the sprites for its widgets and a font. Loaded from
// Resources/UiTheme; without the asset, or with a slot left empty, the code defaults below are the flat pre-M32 look.
// The code defaults equal the asset (UiThemeTests). Sprites are made at 3x design size and imported at 300 pixels per
// unit, so a 9-slice border draws at its design size (Docs/UiArtSizing.md). A sprite is tinted by its slot's colour:
// light grey art takes the colour, full-colour art sets that colour to white.
[CreateAssetMenu(fileName = "UiTheme", menuName = "CityBuilder/UI Theme")]
public sealed class UiTheme : ScriptableObject
{
    [Header("Palette")]
    public Color Panel = new Color(0.10f, 0.12f, 0.16f, 0.97f);
    public Color Dim = new Color(0f, 0f, 0f, 0.55f);
    public Color Button = new Color(0.24f, 0.27f, 0.33f);
    public Color Accent = new Color(0.30f, 0.55f, 0.92f);
    public Color Disabled = new Color(0.17f, 0.18f, 0.21f);
    public Color Title = new Color(0.95f, 0.80f, 0.35f);
    public Color Body = new Color(0.85f, 0.88f, 0.94f);
    public Color Muted = new Color(0.60f, 0.64f, 0.70f);
    [Tooltip("Input fields and the checkbox box.")]
    public Color Field = new Color(0.06f, 0.07f, 0.10f, 1f);
    [Tooltip("Slider track (the fill is Accent).")]
    public Color Track = new Color(0.20f, 0.23f, 0.28f);
    [Tooltip("Slider handle.")]
    public Color Handle = Color.white;

    [Header("Sprites (empty = flat colour)")]
    [Tooltip("Modal window panel (9-sliced).")]
    public Sprite WindowSprite;
    [Tooltip("Text buttons (9-sliced).")]
    public Sprite ButtonSprite;
    [Tooltip("Input fields (9-sliced).")]
    public Sprite FieldSprite;
    public Sprite SliderTrackSprite;
    public Sprite SliderFillSprite;
    public Sprite SliderHandleSprite;
    public Sprite ToggleBoxSprite;
    public Sprite ToggleTickSprite;

    [Header("Text")]
    [Tooltip("Empty = the TMP default font.")]
    public TMP_FontAsset Font;

    private static UiTheme s_Current;

    public static UiTheme Current
    {
        get
        {
            if (s_Current == null)
            {
                s_Current = Resources.Load<UiTheme>("UiTheme");
                if (s_Current == null)
                {
                    s_Current = CreateInstance<UiTheme>();
                    s_Current.hideFlags = HideFlags.DontSave;
                }
            }
            return s_Current;
        }
    }

    // Colours an Image and, when a sprite is given, puts it on (sliced when it has a 9-slice border). With no sprite
    // the Image keeps whatever sprite it had (none for UiKit's widgets), so the flat look is unchanged.
    public static void Paint(Image image, Sprite sprite, Color color)
    {
        image.color = color;
        if (sprite == null) return;
        image.sprite = sprite;
        image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    // The theme font on a text (nothing when the theme has none).
    public static void ApplyFont(TMP_Text text)
    {
        TMP_FontAsset font = Current.Font;
        if (font != null) text.font = font;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_Current = null;
}
