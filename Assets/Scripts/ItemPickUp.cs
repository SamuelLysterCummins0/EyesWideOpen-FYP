using UnityEngine;
using TMPro;

public class ItemPickUp : MonoBehaviour
{
    public Item Item;
    public float pickUpRadius = 3f;
    public KeyCode pickUpKey = KeyCode.E;

    private Transform player;
    private TextMeshProUGUI interactionText;

    void Start()
    {
        player = GameObject.FindWithTag("Player")?.transform;
        interactionText = GetComponentInChildren<TextMeshProUGUI>();

        if (interactionText != null)
        {
            interactionText.enabled = false;
        }
    }

    void Update()
    {
        if (player == null || interactionText == null)
            return;

        if (Vector3.Distance(player.position, transform.position) <= pickUpRadius)
        {
            interactionText.enabled = true;

            if (Input.GetKeyDown(pickUpKey))
            {
                PickUp();
            }
        }
        else
        {
            interactionText.enabled = false;
        }
    }

    void PickUp()
    {
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.Add(Item);
            Destroy(gameObject);
        }
    }
}
