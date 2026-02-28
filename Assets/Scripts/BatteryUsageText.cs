using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class BatteryUsageText : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI interactionText;
    public float displayRange = 3f;

    private void Update()
    {
        Item currentItem = HotbarManager.Instance?.GetActiveItem();
        if (currentItem == null || currentItem.itemType != Item.ItemType.Battery)
        {
            HideText();
            return;
        }

        string message;
        if (MaskController.Instance != null)
        {
            bool hasMask = false;
            if (InventoryManager.Instance != null)
            {
                hasMask = InventoryManager.Instance.Items.Any(item => item.itemType == Item.ItemType.Mask);
            }
            if (HUDManager.Instance != null)
            {
                hasMask |= HUDManager.Instance.HUDItems.Any(item => item.itemType == Item.ItemType.Mask);
            }

            if (!hasMask)
            {
                message = "Nothing to charge";
            }
            else if (MaskController.Instance.CurrentDuration >= MaskController.Instance.MaxDuration)
            {
                message = "Mask charge is full";
            }
            else
            {
                message = "[LEFT CLICK] to Charge";
            }

            ShowText(message);
        }
    }

    private void ShowText(string message)
    {
        if (interactionText != null)
        {
            interactionText.text = message;
            interactionText.enabled = true;
        }
    }

    private void HideText()
    {
        if (interactionText != null)
        {
            interactionText.enabled = false;
        }
    }
}