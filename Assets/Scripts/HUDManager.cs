using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class HUDManager : MonoBehaviour
{
    public static HUDManager Instance;

    // ── Hotbar ─────────────────────────────────────────────────────────────────
    [Tooltip("Reference to the HotbarManager — used for item mounting.")]
    public HotbarManager hotbarManager;

    // ── Insanity Bar ───────────────────────────────────────────────────────────
    [Header("Insanity Bar")]
    public Image        insanityBar;
    public GameObject   insanityBarContainer;

    // ── Intro Mode ─────────────────────────────────────────────────────────────
    [Header("Intro Mode — hide until player enters dungeon")]
    [Tooltip("The CodeNumberHUD panel GameObject — hidden during the intro room.")]
    public GameObject   codeNumberHUDContainer;

    // ── Legacy compatibility stubs ─────────────────────────────────────────────
    // These fields are referenced by DropSlot, InventoryDropArea, BatteryUsageText,
    // and MaskController. They are kept here so those scripts still compile, but
    // HideInInspector keeps them out of the Inspector to avoid confusion.
    // HUDItems stays as an always-empty list so .Any() calls return false safely.
    [HideInInspector] public List<Item>  HUDItems       = new List<Item>();
    [HideInInspector] public Transform   HUDItemContent;   // null → slot-parent checks fail safely
    [HideInInspector] public GameObject  HUDItemPrefab;

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
        // No foreach loop over HUDItemContent — that field is unused and was the
        // source of the NullReferenceException at Awake() line 36 in the build.
    }

    // ── Insanity Bar ───────────────────────────────────────────────────────────

    public void ShowInsanityBar(bool show)
    {
        if (insanityBarContainer != null)
            insanityBarContainer.SetActive(show);
    }

    public void UpdateInsanityBar(float fillAmount)
    {
        if (insanityBar != null)
            insanityBar.fillAmount = Mathf.Clamp01(fillAmount);
    }

    // ── Intro Mode ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Hides/shows the insanity bar and code-number HUD, and locks the inventory.
    /// Called with true at game start (intro room) and false when the player
    /// drops into the Level 0 spawn room (SpawnRoomCheckpoint / SaveGameManager).
    /// </summary>
    public void SetIntroMode(bool introActive)
    {
        if (insanityBarContainer != null)
            insanityBarContainer.SetActive(!introActive);

        if (codeNumberHUDContainer != null)
            codeNumberHUDContainer.SetActive(!introActive);

        InventoryManager.Instance?.SetInventoryLocked(introActive);
    }

    // ── Legacy no-op methods ───────────────────────────────────────────────────
    // Called by DropSlot, InventoryDropArea, and HotbarManager. Kept so those
    // scripts compile without changes; the HUD item slot system is no longer used.

    public void Add(Item item)             { /* HUD item slots removed */ }
    public void Remove(Item item)          { /* HUD item slots removed */ }
    public void ClearSlot(GameObject slot) { /* HUD item slots removed */ }
}
