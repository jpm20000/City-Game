using System.Collections.Generic;
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
    [Tooltip("Side panels, the toolbar, flyouts, tooltips and toasts (9-sliced). M32d.")]
    public Sprite PanelSprite;
    [Tooltip("The HUD bar along the top (9-sliced, ornamented ends). M32d.")]
    public Sprite HudBarSprite;
    [Tooltip("Buttons (9-sliced): the normal state. With it set, every button is drawn from the four button sprites (M32d).")]
    public Sprite ButtonSprite;
    [Tooltip("Hover, and buttons that are on / selected / primary.")]
    public Sprite ButtonActiveSprite;
    public Sprite ButtonPressedSprite;
    public Sprite ButtonDisabledSprite;
    [Tooltip("Input fields (9-sliced).")]
    public Sprite FieldSprite;
    [Tooltip("Meter and slider track (9-sliced, horizontal). M32d.")]
    public Sprite MeterTrackSprite;
    [Tooltip("Meter and slider fill (light grey, tinted with the meter's colour).")]
    public Sprite MeterFillSprite;
    public Sprite SliderTrackSprite;
    public Sprite SliderFillSprite;
    public Sprite SliderHandleSprite;
    public Sprite ToggleBoxSprite;
    public Sprite ToggleTickSprite;

    [Header("Icons (M32c)")]
    [Tooltip("HUD, tool, group and view icons, found by sprite name (UiIcons). Filled from Art/UI/hud, tools and controls by CityBuilder > Sync UI Art; a missing name is text only.")]
    public Sprite[] Icons = new Sprite[0];

    [Header("Text")]
    [Tooltip("Empty = the TMP default font.")]
    public TMP_FontAsset Font;

    private static UiTheme s_Current;
    private Dictionary<string, Sprite> m_IconsByName;

    // The icon with that sprite name, or null (the caller then shows text only, as before M32).
    public static Sprite Icon(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        UiTheme theme = Current;
        if (theme.m_IconsByName == null)
        {
            theme.m_IconsByName = new Dictionary<string, Sprite>();
            if (theme.Icons != null)
                foreach (Sprite sprite in theme.Icons)
                    if (sprite != null) theme.m_IconsByName[sprite.name] = sprite;
        }
        return theme.m_IconsByName.TryGetValue(name, out Sprite icon) ? icon : null;
    }

    private void OnValidate() => m_IconsByName = null;

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

    // (M32d) The frame and button art is full colour, so a sprite is drawn white (untinted); without the sprite the
    // Image gets its flat pre-M32 colour.
    public enum ButtonLook { Normal, Active, Pressed, Disabled }

    // Smallest layout padding inside a framed panel, so content clears the frame's border ornaments.
    public const int FramePadding = 10;

    // A button: the sprite for its look (Active = on, selected or primary), and, on its Button, hover / press /
    // disabled by sprite swap. tint (white by default) colours the art for buttons with more states (tech panel).
    public static void PaintButton(Image image, ButtonLook look, Color flat, Color? tint = null)
    {
        if (image == null) return;
        UiTheme theme = Current;
        if (theme.ButtonSprite == null)
        {
            image.color = flat;
            return;
        }
        Sprite sprite = look switch
        {
            ButtonLook.Active => theme.ButtonActiveSprite,
            ButtonLook.Pressed => theme.ButtonPressedSprite,
            ButtonLook.Disabled => theme.ButtonDisabledSprite,
            _ => theme.ButtonSprite,
        };
        Paint(image, sprite != null ? sprite : theme.ButtonSprite, tint ?? Color.white);
        var button = image.GetComponent<Button>();
        if (button == null || button.targetGraphic != image) return;
        bool swap = button.transition != Selectable.Transition.SpriteSwap;
        button.transition = Selectable.Transition.SpriteSwap;
        button.spriteState = new SpriteState
        {
            highlightedSprite = look == ButtonLook.Normal ? theme.ButtonActiveSprite : null,
            pressedSprite = theme.ButtonPressedSprite,
            disabledSprite = button.spriteState.disabledSprite == theme.ButtonActiveSprite && theme.ButtonActiveSprite != null
                ? theme.ButtonActiveSprite      // SelectedWhenDisabled was called: keep it
                : theme.ButtonDisabledSprite,
        };
        if (swap) image.CrossFadeColor(Color.white, 0f, true, true);    // drop a colour-tint transition's leftover tint
    }

    // A prefab button (white Image, colour-tint transition): drawn from the button art when the theme has it, else left
    // exactly as it is.
    public static void PaintButton(Button button)
    {
        if (button == null || Current.ButtonSprite == null) return;
        PaintButton(button.targetGraphic as Image, ButtonLook.Normal, Color.white);
    }

    // For buttons whose selected choice is the non-interactable one (game speed, new-city size and age): disabled
    // shows the active art instead of the grey one.
    public static void SelectedWhenDisabled(Button button)
    {
        if (button == null || Current.ButtonSprite == null || button.transition != Selectable.Transition.SpriteSwap) return;
        SpriteState state = button.spriteState;
        state.disabledSprite = Current.ButtonActiveSprite != null ? Current.ButtonActiveSprite : Current.ButtonSprite;
        button.spriteState = state;
    }

    // A panel (or a modal window): the frame art, drawn white, with the layout padding raised to clear the frame; else
    // the flat colour.
    public static void PaintPanel(Image image, Color flat, bool window = false)
    {
        if (image == null) return;
        PaintFramed(image, window && Current.WindowSprite != null ? Current.WindowSprite : Current.PanelSprite, flat);
    }

    // The HUD bar: its ornamented art (padding raised to clear the ends), else the panel frame, else flat.
    public static void PaintHudBar(Image image, Color flat)
    {
        if (image == null) return;
        PaintFramed(image, Current.HudBarSprite != null ? Current.HudBarSprite : Current.PanelSprite, flat);
        // The ends take room from a bar that is already full: tighter spacing between its groups.
        if (Current.HudBarSprite != null && image.GetComponent<HorizontalLayoutGroup>() is HorizontalLayoutGroup row)
            row.spacing = Mathf.Min(row.spacing, 10f);
    }

    private static void PaintFramed(Image image, Sprite sprite, Color flat)
    {
        if (sprite == null)
        {
            image.color = flat;
            return;
        }
        Paint(image, sprite, Color.white);
        if (image.GetComponent<LayoutGroup>() is LayoutGroup layout)
        {
            // The border in canvas units (the art is at 300 PPU, the canvas at 100): a frame's ornaments sit in its
            // outer 80%; the HUD bar's ends (no top / bottom border) fill theirs.
            Vector4 border = sprite.border * (image.canvas != null ? image.canvas.referencePixelsPerUnit : 100f) / sprite.pixelsPerUnit;
            RectOffset p = layout.padding;
            int side = Mathf.Max(FramePadding, Mathf.RoundToInt(Mathf.Min(border.x, border.z) * (border.y > 0f ? 0.8f : 1f)));
            int top = border.y > 0f ? FramePadding : 0;
            layout.padding = new RectOffset(Mathf.Max(p.left, side), Mathf.Max(p.right, side), Mathf.Max(p.top, top), Mathf.Max(p.bottom, top));
        }
    }

    // A meter (track + fill whose anchors the caller stretches): the meter art, the fill keeping its colour as a tint
    // and inset into the track's groove. Horizontal meters only; a vertical one stays flat.
    public static void PaintMeter(Image track, Image fill, bool vertical = false)
    {
        UiTheme theme = Current;
        if (vertical || track == null || theme.MeterTrackSprite == null) return;
        Paint(track, theme.MeterTrackSprite, Color.white);
        if (fill == null || theme.MeterFillSprite == null) return;
        Paint(fill, theme.MeterFillSprite, fill.color);
        // The fill art is narrower than the track's (217 x 14 in 235 x 30): inset by the difference, in proportion.
        Rect t = theme.MeterTrackSprite.rect, f = theme.MeterFillSprite.rect;
        float height = ((RectTransform)track.transform).rect.height;
        float scale = height > 0f ? height / t.height : 1f / 3f;
        Vector2 inset = new Vector2((t.width - f.width) * 0.5f, (t.height - f.height) * 0.5f) * scale;
        fill.rectTransform.offsetMin = inset;
        fill.rectTransform.offsetMax = -inset;
    }

    // A uGUI slider (DefaultControls layout: Background, Fill Area/Fill, Handle Slide Area/Handle).
    public static void PaintSlider(Slider slider, Color fill)
    {
        if (slider == null) return;
        UiTheme theme = Current;
        foreach (Image image in slider.GetComponentsInChildren<Image>(true))
        {
            if (image.name == "Background") Paint(image, theme.SliderTrackSprite, theme.SliderTrackSprite != null ? Color.white : theme.Track);
            else if (image.name == "Fill") Paint(image, theme.SliderFillSprite, fill);
            else if (image.name == "Handle") Paint(image, theme.SliderHandleSprite, theme.Handle);
        }
        if (theme.SliderHandleSprite != null && slider.handleRect != null)
        {
            // The round handle art: square, a little taller than the track.
            slider.handleRect.sizeDelta = new Vector2(22f, slider.handleRect.sizeDelta.y);
            if (slider.handleRect.GetComponent<Image>() is Image handle) handle.preserveAspect = true;
        }
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
