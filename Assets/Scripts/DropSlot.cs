using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DropSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    public bool isHUDSlot;
    private Image itemIcon;
    private CanvasGroup canvasGroup;

    void Awake()
    {
        itemIcon = transform.Find("ItemIcon")?.GetComponent<Image>();
        if (itemIcon == null)
        {
            Debug.LogWarning($"No ItemIcon found in {gameObject.name} - this might affect drag and drop functionality");
        }

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Debug.Log($"Pointer entered {gameObject.name}");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Debug.Log($"Pointer exited {gameObject.name}");
    }

    public void OnDrop(PointerEventData eventData)
    {
        Debug.Log($"OnDrop called on {gameObject.name}");
        
        if (eventData.pointerDrag == null)
        {
            Debug.Log("No dragged object found");
            return;
        }

        // Clean up any existing drag icon
        DraggableItem draggable = eventData.pointerDrag.GetComponent<DraggableItem>();
        if (draggable != null)
        {
            draggable.CleanupDragIcon();
        }

        ItemController sourceItemController = eventData.pointerDrag.GetComponent<ItemController>();
        if (sourceItemController == null || sourceItemController.item == null)
        {
            Debug.Log($"No valid ItemController found on {eventData.pointerDrag.name}");
            return;
        }

        Item droppedItem = sourceItemController.item;
        Debug.Log($"Dropping item: {droppedItem.itemName} onto {gameObject.name}");

        if (isHUDSlot)
        {
            HandleInventoryToHUDTransfer(eventData.pointerDrag, droppedItem);
        }
    }

    private void HandleInventoryToHUDTransfer(GameObject draggedObject, Item droppedItem)
    {
        Transform sourceParent = draggedObject.transform.parent;
        if (sourceParent != null && sourceParent == InventoryManager.Instance.ItemContent)
        {
            Debug.Log($"Processing inventory to HUD transfer for slot: {gameObject.name}");

            if (itemIcon != null)
            {
                itemIcon.sprite = droppedItem.icon;
                itemIcon.enabled = true;
                Debug.Log($"Updated HUD slot icon in {gameObject.name}");
            }
            else
            {
                Debug.LogError($"ItemIcon component not found in HUD slot: {gameObject.name}");
                return;
            }

            ItemController slotItemController = GetComponent<ItemController>();
            if (slotItemController == null)
            {
                slotItemController = gameObject.AddComponent<ItemController>();
            }
            slotItemController.item = droppedItem;

            if (GetComponent<DraggableItem>() == null)
            {
                gameObject.AddComponent<DraggableItem>();
            }

            InventoryManager.Instance.Remove(droppedItem);
            HUDManager.Instance.Add(droppedItem);

            
            DraggableItem draggable = draggedObject.GetComponent<DraggableItem>();
            if (draggable != null)
            {
                draggable.CleanupDragIcon();
            }

            Destroy(draggedObject);
            
            Debug.Log($"Successfully transferred item to HUD slot: {gameObject.name}");
        }
    }
}