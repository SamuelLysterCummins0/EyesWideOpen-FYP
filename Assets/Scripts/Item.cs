using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class Item : ScriptableObject
{
    public enum ItemType { Consumable, Mask, Flare, Goggles }

    public string     itemName;
    public ItemType   itemType;
    public Sprite     icon;
    public GameObject itemPrefab;
    public float      useDelay    = 0.5f;
    public float      useDistance = 5f;
    public float      staminaBonus = 0f;

    public void Use(Vector3 position, Vector3 direction)
    {
        if (itemPrefab != null)
            Object.Instantiate(itemPrefab, position, Quaternion.LookRotation(direction));
    }
}
