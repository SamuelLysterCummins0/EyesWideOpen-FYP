using TMPro;
using UnityEngine;

/// <summary>
/// Displays the code collection progress as [_][_][_][_] slots in the HUD.
/// Assign 4 TMP_Text references for the digit slots and one for the counter text.
///
/// Scene setup: Add a Canvas panel to your HUD with 4 TMP_Text children for the
/// digit slots and one TMP_Text for the progress counter. Assign them in the inspector.
/// </summary>
public class CodeNumberHUD : MonoBehaviour
{
    [Header("Digit Slots (assign 4 TMP_Text elements)")]
    [SerializeField] private TMP_Text[] digitSlots = new TMP_Text[4];

    [Header("Counter & Status Text")]
    [SerializeField] private TMP_Text counterText;

    [Header("Colors")]
    [SerializeField] private Color emptySlotColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [SerializeField] private Color filledSlotColor = new Color(1f, 0.9f, 0.4f, 1f);  // Warm yellow

    [Header("Detonation Message")]
    [SerializeField] private Color detonationMessageColor = Color.red;
    [Tooltip("Font size for the detonation escape message. Should be a bit larger than the normal counter text.")]
    [SerializeField] private float detonationMessageFontSize = 28f;

    private float originalCounterFontSize;

    private void Awake()
    {
        if (counterText != null)
            originalCounterFontSize = counterText.fontSize;
        ResetDisplay();
    }

    /// <summary>
    /// Resets all slots to empty at the start of a new level.
    /// </summary>
    public void ResetDisplay()
    {
        for (int i = 0; i < digitSlots.Length; i++)
        {
            if (digitSlots[i] != null)
            {
                digitSlots[i].text = "[_]";
                digitSlots[i].color = emptySlotColor;
            }
        }

        if (counterText != null)
            counterText.text = "0 / 4 codes found";
    }

    /// <summary>
    /// Fills a slot with the collected digit. Called by CodeNumberManager.
    /// </summary>
    public void UpdateSlot(int orderIndex, int digit, int totalCollected)
    {
        if (orderIndex < 0 || orderIndex >= digitSlots.Length) return;

        if (digitSlots[orderIndex] != null)
        {
            digitSlots[orderIndex].text = $"[{digit}]";
            digitSlots[orderIndex].color = filledSlotColor;
        }

        if (counterText != null)
            counterText.text = $"{totalCollected} / 4 codes found";
    }

    /// <summary>
    /// Called when all 4 digits are collected - updates the status message.
    /// </summary>
    public void ShowAllCollectedMessage()
    {
        if (counterText != null)
            counterText.text = "All codes found - use keypad!";
    }

    /// <summary>
    /// Called by PowerManager when the player enters a level with a power outage.
    /// Hides the digit slots and replaces the counter text with a power prompt.
    /// </summary>
    public void ShowPowerMessage()
    {
        foreach (TMP_Text slot in digitSlots)
            if (slot != null) slot.gameObject.SetActive(false);

        if (counterText != null)
            counterText.text = "Turn on the power";
    }

    /// <summary>
    /// Called by DetonationManager when the detonation sequence starts.
    /// Hides the digit slots and shows the escape objective in larger red text.
    /// </summary>
    public void ShowDetonationMessage()
    {
        foreach (TMP_Text slot in digitSlots)
            if (slot != null) slot.gameObject.SetActive(false);

        if (counterText != null)
        {
            counterText.fontSize = detonationMessageFontSize;
            counterText.color    = detonationMessageColor;
            counterText.text     = "Get to the safe room on level 1 and escape!";
        }
    }

    /// <summary>
    /// Called by PowerManager when the powerbox is activated, or by DetonationManager on reset.
    /// Restores the digit slots — CodeNumberManager.ActivateLevel will then
    /// re-fill any already-collected slots.
    /// </summary>
    public void RestoreCodeDisplay()
    {
        foreach (TMP_Text slot in digitSlots)
            if (slot != null) slot.gameObject.SetActive(true);

        if (counterText != null)
        {
            if (originalCounterFontSize > 0) counterText.fontSize = originalCounterFontSize;
            counterText.color = filledSlotColor;
            counterText.text  = "0 / 4 codes found";
        }
    }
}
