using UnityEngine;
using UnityEngine.UI;
using System.Linq;

public class MaskController : MonoBehaviour
{
    public static MaskController Instance { get; private set; }

    [Header("Mask Settings")]
    public float maskDuration = 5f;
    public GameObject maskOverlayPrefab;

    [Header("Mask Position")]
    public Vector3 maskPosition = new Vector3(0, 0, 0.2f);
    public Vector3 maskRotation = Vector3.zero;

    [Header("UI Elements")]
    public Image durationBar;
    public GameObject durationBarUI;

    private GameObject currentMaskOverlay;
    private float currentDuration;
    private bool isMaskActive;
    private bool canUseMask = true;
    private NPCMovement[] monsters;

    public float CurrentDuration => currentDuration;
    public float MaxDuration => maskDuration;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        currentDuration = maskDuration;
        if (durationBarUI != null)
            durationBarUI.SetActive(false);

        CreateMaskOverlay();
    }

    void Update()
    {
        // Only update duration if mask is active
        if (isMaskActive)
        {
            UpdateMaskDuration();
        }
    }

    private void CreateMaskOverlay()
    {
        if (maskOverlayPrefab != null)
        {
            currentMaskOverlay = Instantiate(maskOverlayPrefab, Camera.main.transform);
            UpdateMaskTransform();
            currentMaskOverlay.SetActive(false);
        }
    }

    public void UseMask()
    {
        if (!canUseMask) return;

        if (!isMaskActive)
        {
            ActivateMask();
        }
        else
        {
            DeactivateMask();
        }
    }

    private void ActivateMask()
    {
        if (currentDuration <= 0) return;

        isMaskActive = true;
        if (currentMaskOverlay != null)
            currentMaskOverlay.SetActive(true);
        if (durationBarUI != null)
            durationBarUI.SetActive(true);

        // Disable monster detection
        monsters = FindObjectsOfType<NPCMovement>();
        foreach (var monster in monsters)
        {
            if (monster != null)
                monster.enabled = false;
        }
    }

    private void UpdateMaskDuration()
    {
        currentDuration -= Time.deltaTime;
        
        if (durationBar != null)
        {
            durationBar.fillAmount = Mathf.Max(0, currentDuration / maskDuration);
        }

        if (currentDuration <= 0)
        {
            currentDuration = 0;
            canUseMask = false;
            DeactivateMask();
        }
    }

    private void DeactivateMask()
    {
        isMaskActive = false;
        if (currentMaskOverlay != null)
            currentMaskOverlay.SetActive(false);
        if (durationBarUI != null)
            durationBarUI.SetActive(false);

        if (monsters != null)
        {
            foreach (var monster in monsters)
            {
                if (monster != null)
                    monster.enabled = true;
            }
        }
    }

    private void UpdateMaskTransform()
{
    if (currentMaskOverlay != null)
    {
        currentMaskOverlay.transform.localPosition = maskPosition;
        currentMaskOverlay.transform.localRotation = Quaternion.Euler(maskRotation);
    }
}

    public void RechargeMask()
    {
        currentDuration = maskDuration;
        canUseMask = true;
    }

    public string TryChargeMask()
{
    // Check if player has mask in inventory
    bool hasMask = false;
    if (InventoryManager.Instance != null)
    {
        hasMask = InventoryManager.Instance.Items.Any(item => item.itemType == Item.ItemType.Mask);
    }
    if (!hasMask && HUDManager.Instance != null && !HUDManager.Instance.HUDItems.Any(item => item.itemType == Item.ItemType.Mask))
    {
        return "Nothing to charge";
    }

    // Check if mask is already fully charged
    if (currentDuration >= maskDuration)
    {
        return "Mask charge is full";
    }

    // Charge the mask
    currentDuration = maskDuration;
    canUseMask = true;
    
    // Update UI if visible
    if (durationBar != null)
    {
        durationBar.fillAmount = 1f;
    }

    return "Mask charged!";
}
}