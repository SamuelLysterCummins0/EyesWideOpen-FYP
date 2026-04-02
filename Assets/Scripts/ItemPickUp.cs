using UnityEngine;
using TMPro;

public class ItemPickUp : MonoBehaviour
{
    public Item Item;
    public float pickUpRadius = 3f;
    public KeyCode pickUpKey = KeyCode.E;

    private Transform player;
    private TextMeshProUGUI interactionText;
    private static bool hasShownMaskInstructions = false;
    private static bool hasShownFlareInstructions = false;
    private static bool hasShownConsumableInstructions = false;
    private static bool hasShownGogglesInstructions = false;

    void Start()
    {
        player = GameObject.FindWithTag("Player")?.transform;
        interactionText = GetComponentInChildren<TextMeshProUGUI>();
        
        if (interactionText != null)
        {
            interactionText.enabled = false;
        }
    }

    void Update()
    {
        if (player == null || interactionText == null || GameManager.Instance == null) 
            return;

        if (!GameManager.Instance.IsDeathScreenActive() && 
            Vector3.Distance(player.position, transform.position) <= pickUpRadius)
        {
            interactionText.enabled = true;

            if (Input.GetKeyDown(pickUpKey))
            {
                PickUp();
            }
        }
        else
        {
            interactionText.enabled = false;
        }
    }

    void PickUp()
    {
        if (InventoryManager.Instance != null)
        {
            if (Item.itemType == Item.ItemType.Mask && !hasShownMaskInstructions)
            {
                InstructionUI.Instance.ShowPanel(Item.itemType);
                hasShownMaskInstructions = true;
            }
            else if (Item.itemType == Item.ItemType.Flare && !hasShownFlareInstructions)
            {
                InstructionUI.Instance.ShowPanel(Item.itemType);
                hasShownFlareInstructions = true;
            }
            else if (Item.itemType == Item.ItemType.Consumable && !hasShownConsumableInstructions)
            {
                InstructionUI.Instance.ShowPanel(Item.itemType);
                hasShownConsumableInstructions = true;
            }
            else if (Item.itemType == Item.ItemType.Goggles && !hasShownGogglesInstructions)
            {
                InstructionUI.Instance.ShowPanel(Item.itemType);
                hasShownGogglesInstructions = true;
            }

            InventoryManager.Instance.Add(Item);
            Destroy(gameObject);
        }
    }
}