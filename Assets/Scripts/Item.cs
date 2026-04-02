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
        Flare,
        Goggles
    }
    
    public ItemType itemType;
    
    
    public virtual void Use(Vector3 usePosition, Vector3 useDirection)
    {
        switch (itemType)
        {
                
            case ItemType.Consumable:
                
                
                break;

            case ItemType.CarPart:
                
                break;

             case ItemType.Mask:
                if (MaskController.Instance != null)
                {
                    MaskController.Instance.UseMask();
                }

                 if (HotbarManager.Instance != null)
                {

                    HotbarManager.Instance.HideItemInView();
                }
                break;


            case ItemType.Battery:
                
            if (MaskController.Instance != null)
            {
               string message = MaskController.Instance.TryChargeMask();
               Debug.Log(message);

        if (message == "Mask charged!")
            {
            if (HotbarManager.Instance != null)
            {
                HotbarManager.Instance.RemoveActiveItem();
            }
             }
          }
               break;


     case ItemType.Flare:
    Debug.Log("Attempting to use flare"); // Add this line
    if (FlareController.Instance != null)
    {
        FlareController.Instance.UseFlare();
    }
    break;

            case ItemType.Goggles:
                if (GoggleController.Instance != null)
                    GoggleController.Instance.UseGoggles();
                break;

        }
    }

}
