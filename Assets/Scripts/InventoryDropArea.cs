using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventoryDropArea : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;

        ItemController itemController = eventData.pointerDrag.GetComponent<ItemController>();
        if (itemController != null && itemController.item != null)
        {
            Item droppedItem = itemController.item;
            Transform hudSlot = eventData.pointerDrag.transform;
            
            
            if (hudSlot.parent == HUDManager.Instance.HUDItemContent)
            {
                
                InventoryManager.Instance.Add(droppedItem);
                
                
                HUDManager.Instance.Remove(droppedItem);
                
               
                Image itemIcon = hudSlot.Find("ItemIcon").GetComponent<Image>();
                if (itemIcon != null)
                {
                    itemIcon.sprite = null;
                }
                
                
                Destroy(itemController);
            }
        }
    }
}