using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;
    public List<Item> Items = new List<Item>();

    public Transform ItemContent;
    public GameObject InventoryItem;
    public GameObject inventoryUI; 
    public KeyCode toggleKey = KeyCode.I; 

    private bool isInventoryOpen = false;
    private bool inventoryLocked = false;
    public GameObject dropArea;

    public void Awake()
    {
        Instance = this;
         if (dropArea != null)
        {
            dropArea.AddComponent<InventoryDropArea>();
        }
    }

    public void SetInventoryLocked(bool locked)
    {
        inventoryLocked = locked;
        if (locked)
        {
            // Always hide the panel regardless of whether it was open —
            // it may start active in the scene by default.
            isInventoryOpen = false;
            if (inventoryUI != null) inventoryUI.SetActive(false);
        }
    }

    void Update()
    {
        if (inventoryLocked) return;
        if (Input.GetKeyDown(toggleKey))
            ToggleInventory();
    }

    public void ToggleInventory()
    {
        isInventoryOpen = !isInventoryOpen;
        inventoryUI.SetActive(isInventoryOpen);

        if (isInventoryOpen)
        {
            ListItems();
            
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;                  
        }
        else
        {
            ClearInventoryUI();
            
            Cursor.lockState = CursorLockMode.Locked; 
            Cursor.visible = false;                   // Hide cursor
        }
    }

    public void Add(Item item)
    {
        Items.Add(item);
        ListItems();
    }


    public void Remove(Item item)
    {
        Items.Remove(item);
        ListItems();
    }

    public void ListItems()
    {
        foreach (Transform item in ItemContent)
        {
            Destroy(item.gameObject);
        }
        foreach (var item in Items)
        {
            GameObject obj = Instantiate(InventoryItem, ItemContent);
            obj.AddComponent<DraggableItem>(); 

            ItemController itemController = obj.AddComponent<ItemController>();
        itemController.item = item;

            var itemName = obj.transform.Find("ItemName").GetComponent<TMP_Text>();
            var itemIcon = obj.transform.Find("ItemIcon").GetComponent<Image>();

            itemName.text = item.itemName;
            itemIcon.sprite = item.icon;
        }
    }

    private void ClearInventoryUI()
    {
        foreach (Transform child in ItemContent)
        {
            Destroy(child.gameObject);
        }
    }
}
