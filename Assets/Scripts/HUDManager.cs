using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class HUDManager : MonoBehaviour
{
    public static HUDManager Instance;
    public List<Item> HUDItems = new List<Item>();
    public Transform HUDItemContent;
    public GameObject HUDItemPrefab;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
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
}
