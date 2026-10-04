using System;
using UnityEngine;
using UnityEngine.UI;

// The HUD's Save / Menu buttons (M19b). The scene still has Save / Load / New: Load is relabelled Menu (it opens the
// pause menu) and New is hidden (New city lives in the pause menu), so the HUD group keeps its size. Save asks
// GameFlow, which saves the city's own file or opens the Save as window; without a GameFlow it saves directly.
public sealed class GameMenu : MonoBehaviour
{
    [SerializeField] private SaveGameController m_SaveGame;
    [SerializeField] private Button m_SaveButton;
    [SerializeField] private Button m_LoadButton;
    [SerializeField] private Button m_NewButton;

    public event Action NewRequested;
    public event Action MenuRequested;
    public event Action SaveRequested;

    private void Start()
    {
        if (m_SaveGame == null) return;

        if (m_SaveButton != null) m_SaveButton.onClick.AddListener(OnSaveClicked);
        if (m_LoadButton != null)
        {
            UiKit.SetLabel(m_LoadButton, "Menu");
            m_LoadButton.interactable = true;
            m_LoadButton.onClick.AddListener(OnMenuClicked);
        }
        if (m_NewButton != null) m_NewButton.gameObject.SetActive(false);
    }

    // The pause menu's New city entry: the New City dialog listens for this (it discards the current city only when
    // the player confirms there).
    public void RequestNew()
    {
        if (NewRequested != null) NewRequested.Invoke();
        else Debug.LogWarning("GameMenu: nothing handles New (no NewCityDialog in the scene?).", this);
    }

    private void OnSaveClicked()
    {
        if (SaveRequested != null) SaveRequested.Invoke();
        else m_SaveGame.Save();
    }

    private void OnMenuClicked()
    {
        if (MenuRequested != null) MenuRequested.Invoke();
        else Debug.LogWarning("GameMenu: nothing handles Menu (no GameFlow?).", this);
    }
}
