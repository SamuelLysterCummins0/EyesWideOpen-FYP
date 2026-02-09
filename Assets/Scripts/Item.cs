using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName= "Item/Create New Item")]

public class Item : ScriptableObject
{
    public int id;
    public string itemName;
     public GameObject itemPrefab;
     public float useDistance = 3f;
    public float useDelay = 0.5f;
    public float staminaBonus;
    public string desc;
    public Sprite icon;
    public string instructions;

public enum ItemType
    {
        Consumable,
        CarPart,
        Mask,
        Battery,
        Flare
    }

    public ItemType itemType;


    public virtual void Use(Vector3 usePosition, Vector3 useDirection)
    {
        // Item usage will be implemented per type
        Debug.Log($"Used item: {itemName}");
    }

}
