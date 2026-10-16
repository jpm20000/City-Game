using UnityEngine;
using UnityEngine.UI;

// (M32d) Paints a prefab's Image (or, slot Title, sets a text's font, M32e) from the UI theme on Awake, so the HUD, toolbar and side-panel prefabs take the frame
// and button art without their sprites set by hand. With no art in the theme the Image is left as it is (the flat
// look). Panels built in code call the UiTheme painters themselves.
public sealed class UiThemed : MonoBehaviour
{
    public enum Slot
    {
        Panel,          // side panels, the toolbar, tooltips, toasts
        Window,         // modal dialogs
        HudBar,         // the top bar
        Button,         // a plain button
        ChoiceButton,   // a button whose selected choice is the non-interactable one (speed, new-city size / age)
        Slider,         // on the Slider: track, fill (keeps its colour) and handle
        Title,          // (M32e) on a text: the theme's title font
    }

    [SerializeField] private Slot m_Slot;

    public Slot Kind => m_Slot;

    private void Awake()
    {
        var image = GetComponent<Image>();
        if (m_Slot == Slot.Title)
        {
            UiTheme.ApplyTitleFont(GetComponent<TMPro.TMP_Text>());
            return;
        }
        if (image == null && m_Slot != Slot.Slider) return;
        switch (m_Slot)
        {
            case Slot.Panel: UiTheme.PaintPanel(image, image.color); break;
            case Slot.Window: UiTheme.PaintPanel(image, image.color, window: true); break;
            case Slot.HudBar: UiTheme.PaintHudBar(image, image.color); break;
            case Slot.Button: UiTheme.PaintButton(GetComponent<Button>()); break;
            case Slot.ChoiceButton:
                UiTheme.PaintButton(GetComponent<Button>());
                UiTheme.SelectedWhenDisabled(GetComponent<Button>());
                break;
            case Slot.Slider:
                var slider = GetComponent<Slider>();
                if (slider != null && slider.fillRect != null && UiTheme.Current.SliderTrackSprite != null)
                    UiTheme.PaintSlider(slider, slider.fillRect.GetComponent<Image>().color);
                break;
        }
    }
}
