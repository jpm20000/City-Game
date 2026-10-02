using System;
using UnityEngine;
using UnityEngine.UI;

// HUD Save / Load / New buttons. Load is disabled until a save exists; New raises NewRequested, which
// the New City dialog listens to (it discards the current city only when the player confirms there).
public sealed class GameMenu : MonoBehaviour
{
    [SerializeField] private SaveGameController m_SaveGame;
    [SerializeField] private Button m_SaveButton;
    [SerializeField] private Button m_LoadButton;
    [SerializeField] private Button m_NewButton;

    public event Action NewRequested;

    private void Start()
    {
        if (m_SaveGame == null) return;

        if (m_SaveButton != null) m_SaveButton.onClick.AddListener(() =>
        {
            m_SaveGame.Save();
            RefreshLoadButton();
        });
        if (m_LoadButton != null) m_LoadButton.onClick.AddListener(() => m_SaveGame.Load());
        if (m_NewButton != null) m_NewButton.onClick.AddListener(OnNewClicked);

        RefreshLoadButton();
    }

    private void OnEnable()
    {
        GameEvents.Notification += OnNotification;
    }

    private void OnDisable()
    {
        GameEvents.Notification -= OnNotification;
    }

    // F5 saves bypass the button; any notification is a cheap moment to recheck the file.
    private void OnNotification(string message)
    {
        RefreshLoadButton();
    }

    private void OnNewClicked()
    {
        if (NewRequested != null) NewRequested.Invoke();
        else Debug.LogWarning("GameMenu: nothing handles New (no NewCityDialog in the scene?).", this);
    }

    private void RefreshLoadButton()
    {
        if (m_LoadButton != null && m_SaveGame != null) m_LoadButton.interactable = m_SaveGame.HasSave;
    }
}
