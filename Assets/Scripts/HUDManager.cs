using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class HUDManager : MonoBehaviour
{
    public static HUDManager Instance;
    public List<Item> HUDItems = new List<Item>();
    public Transform HUDItemContent;
    public GameObject HUDItemPrefab;
    public HotbarManager hotbarManager; 
    public Image maskDurationBar;
    public GameObject maskDurationBarContainer;
    

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
        
        
        int slotIndex = 0;
        foreach (Transform child in HUDItemContent)
        {
            if (child.GetComponent<DraggableItem>() == null)
            {
                child.gameObject.AddComponent<DraggableItem>();
            }
            
            
            if (hotbarManager != null)
            {
                hotbarManager.SetupHotbarSlot(slotIndex, child.gameObject);
            }
            
            slotIndex++;
        }
    }

    public void Add(Item item)
    {
        if (!HUDItems.Contains(item))
        {
            HUDItems.Add(item);
        }
    }

    public void Remove(Item item)
    {
        HUDItems.Remove(item);
    }

    public void ClearSlot(GameObject slot)
{
    
    ItemController itemController = slot.GetComponent<ItemController>();
    
    if (itemController != null)
    {
        
        itemController.item = null; 
    }

    
    Transform itemIconTransform = slot.transform.Find("ItemIcon"); 
    if (itemIconTransform != null)
    {
        
        Image itemIcon = itemIconTransform.GetComponent<Image>();
        if (itemIcon != null)
        {
            itemIcon.sprite = null; 
        }
    }
}

public void ShowMaskDurationBar(bool show)
    {
        if (maskDurationBarContainer != null)
        {
            maskDurationBarContainer.SetActive(show);
        }
    }

    public void UpdateMaskDurationBar(float fillAmount)
    {
        if (maskDurationBar != null)
        {
            maskDurationBar.fillAmount = fillAmount;
        }
    }
}