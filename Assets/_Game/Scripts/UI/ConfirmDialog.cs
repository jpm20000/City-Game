using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A small modal question with a few buttons (M19b): "Unsaved changes: Save / Don't save / Cancel", "Replace this
// save?", "Delete this save?". Esc closes it without doing anything. Choosing a button runs its action first and then
// closes the dialog, so a follow-up window opened by the action keeps the game paused without a flicker.
public sealed class ConfirmDialog
{
    public readonly struct Choice
    {
        public readonly string Label;
        public readonly Action OnPick;
        public readonly bool Primary;

        public Choice(string label, Action onPick, bool primary = false)
        {
            Label = label;
            OnPick = onPick;
            Primary = primary;
        }
    }

    private readonly UiKit.Window m_Window;
    private readonly GameFlow m_Flow;
    private readonly TMP_Text m_Message;
    private readonly RectTransform m_Buttons;
    private readonly Action m_Closer;

    public bool IsOpen => m_Window.IsOpen;

    public ConfirmDialog(Transform canvas, GameFlow flow)
    {
        m_Flow = flow;
        m_Closer = Hide;
        m_Window = UiKit.CreateWindow(canvas, "ConfirmDialog", "", 460f);
        m_Message = UiKit.Text(m_Window.Body, "", 17, UiKit.BodyColor);
        m_Buttons = UiKit.Row(m_Window.Body, 38f);
        EscapeRouter.Register(this, EscapeRouter.Confirm, () =>
        {
            if (!IsOpen) return false;
            Hide();
            return true;
        });
    }

    public void Ask(string title, string message, params Choice[] choices)
    {
        m_Window.Title.text = title;
        m_Message.text = message;

        foreach (Transform child in m_Buttons)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
        foreach (Choice choice in choices)
        {
            Choice picked = choice;
            Button button = UiKit.MakeButton(m_Buttons, picked.Label, () => Pick(picked), 0f, picked.Primary ? UiKit.AccentColor : (Color?)null);
            button.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        m_Window.Show();
        m_Flow.WindowOpened(m_Closer);
    }

    private void Pick(Choice choice)
    {
        choice.OnPick?.Invoke();
        Hide();
    }

    private void Hide()
    {
        m_Window.Hide();
        m_Flow.WindowClosed(m_Closer);
    }
}
