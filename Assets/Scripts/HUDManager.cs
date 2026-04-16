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

    [Header("Insanity Bar")]
    public Image insanityBar;
    public GameObject insanityBarContainer;

    [Header("Intro Mode — hide until player enters dungeon")]
    [Tooltip("The CodeNumberHUD panel GameObject — hidden during the intro room.")]
    public GameObject codeNumberHUDContainer;


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

    private void Start()
    {
        // If an IntroRoomSetup is in the scene the player starts in the intro room,
        // so hide dungeon HUD elements until they drop into the dungeon.
        if (FindObjectOfType<IntroRoomSetup>() != null)
            SetIntroMode(true);
    }

    /// <summary>
    /// Hides/shows the insanity bar, code number HUD, and locks the inventory.
    /// Called with true at game start (intro room) and false when the player
    /// drops into the Level 0 spawn room (SpawnRoomCheckpoint level 0).
    /// </summary>
    public void SetIntroMode(bool introActive)
    {
        if (insanityBarContainer != null)
            insanityBarContainer.SetActive(!introActive);

        if (codeNumberHUDContainer != null)
            codeNumberHUDContainer.SetActive(!introActive);

        InventoryManager.Instance?.SetInventoryLocked(introActive);
    }

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
}