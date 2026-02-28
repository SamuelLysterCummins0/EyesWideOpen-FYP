using UnityEngine;
using UnityEngine.UI;
using SUPERCharacter;
public class HotbarManager : MonoBehaviour
{
    public static HotbarManager Instance;
    public GameObject itemUseIndicator;
    private SUPERCharacterAIO playerController;
    
    [System.Serializable]
    public class HotbarSlot
    {
        public KeyCode hotkey = KeyCode.None;
        public GameObject slotObject;
        public bool isActive;
    }
    
    public HotbarSlot[] hotbarSlots = new HotbarSlot[6];
    private float lastUseTime;
    private Camera mainCamera;
    private int activeSlotIndex = -1;
    public Transform itemMountPoint; 
    private GameObject activeItemInView;

    private void Awake()
    {
        Instance = this;
        playerController = FindObjectOfType<SUPERCharacterAIO>();
        mainCamera = Camera.main;
        
        if (hotbarSlots[0].hotkey == KeyCode.None)
        {
            hotbarSlots[0].hotkey = KeyCode.Alpha1;
            hotbarSlots[1].hotkey = KeyCode.Alpha2;
            hotbarSlots[2].hotkey = KeyCode.Alpha3;
            hotbarSlots[3].hotkey = KeyCode.Alpha4;
            hotbarSlots[4].hotkey = KeyCode.Alpha5;
            hotbarSlots[5].hotkey = KeyCode.Alpha6;
        }
    }

    public Item GetActiveItem()
{
    if (activeSlotIndex >= 0 && activeSlotIndex < hotbarSlots.Length)
    {
        GameObject slot = hotbarSlots[activeSlotIndex].slotObject;
        ItemController itemController = slot.GetComponent<ItemController>();
        return itemController != null ? itemController.item : null;
    }
    return null;
}

public void RemoveActiveItem()
    {
        if (activeSlotIndex >= 0 && activeSlotIndex < hotbarSlots.Length)
        {
            GameObject slot = hotbarSlots[activeSlotIndex].slotObject;
            ItemController itemController = slot.GetComponent<ItemController>();
            if (itemController != null && itemController.item != null)
            {
                itemController.item = null;
                HUDManager.Instance.ClearSlot(slot);
                DeactivateCurrentSlot();
            }
        }
    }

    private void Update()
    {
        for (int i = 0; i < hotbarSlots.Length; i++)
        {
            if (Input.GetKeyDown(hotbarSlots[i].hotkey))
            {
                ToggleSlot(i);
            }
        }

        if (activeSlotIndex != -1 && Input.GetMouseButtonDown(0))
        {
            UseActiveItem();
        }
    }

    private void ToggleSlot(int index)
    {
        if (activeSlotIndex == index)
        {
            DeactivateCurrentSlot();
            return;
        }

        DeactivateCurrentSlot();
        
        GameObject slot = hotbarSlots[index].slotObject;
        ItemController itemController = slot.GetComponent<ItemController>();

        if (itemController != null && itemController.item != null)
        {
            activeSlotIndex = index;
            hotbarSlots[index].isActive = true;
            DisplayItemInView(itemController.item);
        }
    }

    private void DeactivateCurrentSlot()
    {
        if (activeSlotIndex != -1)
        {
            hotbarSlots[activeSlotIndex].isActive = false;

            GameObject slot = hotbarSlots[activeSlotIndex].slotObject;
            Image slotImage = slot.GetComponent<Image>();
            

            HideItemInView();
            activeSlotIndex = -1;
        }
    }

    private void DisplayItemInView(Item item)
    {
        if (item.itemPrefab == null) return;

        if (item.itemType == Item.ItemType.Mask)
        {
            return;
        }

        
        HideItemInView();

        
        activeItemInView = Instantiate(item.itemPrefab, itemMountPoint.position, itemMountPoint.rotation);
        activeItemInView.transform.SetParent(itemMountPoint, true);
    }

    public void HideItemInView()
    {
        if (activeItemInView != null)
        {
            Destroy(activeItemInView);
            activeItemInView = null;
        }
    }

    public void SetupHotbarSlot(int index, GameObject slotObject)
{
    if (index >= 0 && index < hotbarSlots.Length)
    {
        hotbarSlots[index].slotObject = slotObject;
    }
}


    private void UseActiveItem()
{
    if (activeSlotIndex == -1) return;
    
    ItemController itemController = hotbarSlots[activeSlotIndex].slotObject.GetComponent<ItemController>();
    if (itemController == null || itemController.item == null) return;

    Item activeItem = itemController.item;

    if (Time.time - lastUseTime < activeItem.useDelay) return;

    // Special handling for different item types
    if (activeItem.itemType == Item.ItemType.Consumable)
    {
        IncreaseStamina(activeItem);
        DestroyConsumableItem();
    }
    else if (activeItem.itemType == Item.ItemType.Flare)
    {
        // For flares, just call Use with the player's position
        activeItem.Use(transform.position, Vector3.zero);
        lastUseTime = Time.time;
    }
    else 
    {
        // Original raycast behavior for other items
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 usePosition;
        Vector3 useDirection;

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            usePosition = hit.point;
            useDirection = hit.normal;
        }
        else
        {
            usePosition = mainCamera.transform.position + ray.direction * activeItem.useDistance;
            useDirection = ray.direction;
        }

        activeItem.Use(usePosition, useDirection);
        lastUseTime = Time.time;
    }
}
    private void IncreaseStamina(Item consumable)
    {
        
        float staminaIncrease = consumable.staminaBonus; 
        if (staminaIncrease > 0 && playerController != null)
        {
            
            playerController.currentStaminaLevel = Mathf.Clamp(playerController.currentStaminaLevel + staminaIncrease, 0, playerController.Stamina);
        }
    }

private void DestroyConsumableItem()
{
    
    if (activeSlotIndex >= 0 && activeSlotIndex < hotbarSlots.Length)
    {
        
        if (HUDManager.Instance != null)
        {
            GameObject slot = hotbarSlots[activeSlotIndex].slotObject; 
            HUDManager.Instance.ClearSlot(slot); 
        }

        
        if (hotbarSlots[activeSlotIndex].slotObject != null)
        {
            ItemController itemController = hotbarSlots[activeSlotIndex].slotObject.GetComponent<ItemController>();
            if (itemController != null)
            {
                itemController.item = null; 
            }
        }

        
        hotbarSlots[activeSlotIndex].isActive = false;

       

        
        activeSlotIndex = -1;

        
        HideItemInView();
    }

    
    if (activeSlotIndex >= 0 && activeSlotIndex < hotbarSlots.Length)
    {
        ItemController activeItemController = hotbarSlots[activeSlotIndex].slotObject.GetComponent<ItemController>();
        if (activeItemController != null && activeItemController.item != null && activeItemController.item.itemType == Item.ItemType.Consumable)
        {
            IncreaseStamina(activeItemController.item);
        }
    }
}

}
