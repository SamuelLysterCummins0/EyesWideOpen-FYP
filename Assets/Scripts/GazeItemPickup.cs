using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gaze-and-blink item pickup — look at the item, it pulses green, blink to collect.
///
/// Replace (or use instead of) ItemPickUp on any prefab that should be picked up via
/// head-tracking.  Emission + base-colour dual-channel glow ensures the pulse is
/// visible regardless of whether the material has emission enabled.
///
/// Setup:
///   1. Add this component to the item root (needs a Collider).
///   2. Assign the Item ScriptableObject.
///   3. Use Rotation Correction (e.g. 180 0 0) if the model spawns upside-down.
///   4. Use Height Offset to nudge it up/down after spawn.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GazeItemPickup : MonoBehaviour
{
    [Header("Item")]
    [SerializeField] public Item item;

    [Header("Spawn Correction")]
    [Tooltip("Euler angles added to the object's local rotation on Start.\nUse (180, 0, 0) to flip an upside-down model right-side up.")]
    [SerializeField] private Vector3 rotationCorrection = Vector3.zero;
    [Tooltip("Extra Y offset applied on Start, in world units.")]
    [SerializeField] private float   heightOffset       = 0f;

    [Header("Gaze / Blink Settings")]
    [SerializeField] private float  rayDistance   = 6f;
    [SerializeField] private float  gazeHitRadius = 0.5f;
    [SerializeField] private string promptText    = "Blink to pick up";

    [Header("Glow")]
    [SerializeField] private Color glowColor     = Color.green;
    [SerializeField] private float glowIntensity = 2.5f;
    [SerializeField] private float pulseSpeed    = 3f;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private GazeDetector  gazeDetector;
    private BlinkDetector blinkDetector;
    private Camera        playerCamera;

    // Per-renderer material state (instance copies so other objects are unaffected)
    private Renderer[] itemRenderers;
    private Color[]    originalEmission;
    private Color[]    originalBaseColor;
    private bool[]     supportsEmission;
    private bool[]     usesBaseColor;      // true = URP _BaseColor, false = Standard _Color

    private bool isGazedAt   = false;
    private bool wasBlinking = false;

    private Text uiPrompt;


    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        gazeDetector  = FindObjectOfType<GazeDetector>();
        blinkDetector = FindObjectOfType<BlinkDetector>();
        playerCamera  = Camera.main;

        // Apply spawn corrections before anything else
        if (rotationCorrection != Vector3.zero)
            transform.Rotate(rotationCorrection, Space.Self);
        if (heightOffset != 0f)
            transform.position += Vector3.up * heightOffset;

        // ── Cache materials ───────────────────────────────────────────────────
        // .material creates per-instance copies, so we can tint freely
        itemRenderers    = GetComponentsInChildren<Renderer>();
        originalEmission = new Color[itemRenderers.Length];
        originalBaseColor= new Color[itemRenderers.Length];
        supportsEmission = new bool [itemRenderers.Length];
        usesBaseColor    = new bool [itemRenderers.Length];

        for (int i = 0; i < itemRenderers.Length; i++)
        {
            // Access .material (not .sharedMaterial) to create an instance
            Material mat = itemRenderers[i].material;

            // Emission channel (_EmissionColor — URP Lit & Standard)
            supportsEmission[i] = mat.HasProperty("_EmissionColor");
            if (supportsEmission[i])
            {
                mat.EnableKeyword("_EMISSION");
                originalEmission[i] = mat.GetColor("_EmissionColor");
            }

            // Base colour channel: URP uses _BaseColor, Standard uses _Color
            if (mat.HasProperty("_BaseColor"))
            {
                usesBaseColor[i]    = true;
                originalBaseColor[i]= mat.GetColor("_BaseColor");
            }
            else if (mat.HasProperty("_Color"))
            {
                usesBaseColor[i]    = false;
                originalBaseColor[i]= mat.GetColor("_Color");
            }
            else
            {
                originalBaseColor[i]= Color.white;
            }
        }

        BuildPromptUI();
    }

    private void Update()
    {
        isGazedAt = CheckGaze();

        if (isGazedAt)
        {
            PulseGlow();
            ShowPrompt(true);

            bool blinkingNow = blinkDetector != null && blinkDetector.IsBlinking;
            if (blinkingNow && !wasBlinking)
                PickUp();
            wasBlinking = blinkingNow;
        }
        else
        {
            ResetGlow();
            ShowPrompt(false);
            wasBlinking = blinkDetector != null && blinkDetector.IsBlinking;
        }
    }

    // ── Gaze ─────────────────────────────────────────────────────────────────

    private bool CheckGaze()
    {
        if (gazeDetector == null || !gazeDetector.IsTracking || playerCamera == null)
            return false;

        Ray ray = gazeDetector.GetGazeRay(playerCamera);

        // Primary: simple raycast (same as keypad — reliable direct-look detection)
        if (Physics.Raycast(ray, out RaycastHit directHit, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (directHit.collider.transform == transform ||
                directHit.collider.transform.IsChildOf(transform))
                return true;
        }

        // Secondary: SphereCastAll for looser hit detection
        RaycastHit[] hits = Physics.SphereCastAll(ray, gazeHitRadius, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
                return true;
        }

        // Fallback: direct ray-to-centre distance check — fires even when the collider
        // doesn't cover the visual centre of the model.
        Vector3 toItem = transform.position - ray.origin;
        float   along  = Vector3.Dot(ray.direction, toItem);
        if (along > 0f && along <= rayDistance)
        {
            Vector3 closest = ray.origin + ray.direction * along;
            if (Vector3.Distance(closest, transform.position) <= gazeHitRadius * 2f)
                return true;
        }

        return false;
    }

    // ── Glow — dual channel: emission + base tint ─────────────────────────────
    // Using both channels guarantees a visible pulse even if the material doesn't
    // have emission enabled in the asset itself.

    private void PulseGlow()
    {
        float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1f);

        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;

            // Emission glow
            if (supportsEmission[i])
                m.SetColor("_EmissionColor", glowColor * glowIntensity * pulse);

            // Base-colour tint (50 % blend toward green at peak — always visible)
            Color tinted = Color.Lerp(originalBaseColor[i], glowColor, pulse * 0.5f);
            if (usesBaseColor[i])
                m.SetColor("_BaseColor", tinted);
            else if (m.HasProperty("_Color"))
                m.SetColor("_Color", tinted);
        }
    }

    private void ResetGlow()
    {
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;

            if (supportsEmission[i])
                m.SetColor("_EmissionColor", originalEmission[i]);

            if (usesBaseColor[i])
                m.SetColor("_BaseColor", originalBaseColor[i]);
            else if (m.HasProperty("_Color"))
                m.SetColor("_Color", originalBaseColor[i]);
        }
    }

    // ── Pickup ────────────────────────────────────────────────────────────────

    private void PickUp()
    {
        if (item == null) return;

        ResetGlow();
        ShowPrompt(false);

        if (item.itemType == Item.ItemType.Goggles)
        {
            if (GoggleController.Instance != null)
                GoggleController.Instance.UnlockGoggles();
            if (InstructionUI.Instance != null)
                InstructionUI.Instance.ShowPanel(item.itemType);
        }

        if (InventoryManager.Instance != null)
            InventoryManager.Instance.Add(item);

        Destroy(gameObject);
    }

    // ── Prompt UI ─────────────────────────────────────────────────────────────

    private void BuildPromptUI()
    {
        GameObject canvasObj = new GameObject($"GazePickupCanvas_{GetInstanceID()}");
        Canvas canvas        = canvasObj.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder  = 100;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject textObj = new GameObject("PromptText");
        textObj.transform.SetParent(canvas.transform, false);

        uiPrompt           = textObj.AddComponent<Text>();
        uiPrompt.text      = promptText;
        uiPrompt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiPrompt.fontSize  = 26;
        uiPrompt.color     = new Color(0.4f, 1f, 0.4f);
        uiPrompt.alignment = TextAnchor.MiddleCenter;

        RectTransform rect    = uiPrompt.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.5f, 0.22f);
        rect.anchorMax        = new Vector2(0.5f, 0.22f);
        rect.sizeDelta        = new Vector2(400, 50);
        rect.anchoredPosition = Vector2.zero;

        uiPrompt.gameObject.SetActive(false);
    }

    private void ShowPrompt(bool show)
    {
        if (uiPrompt != null)
            uiPrompt.gameObject.SetActive(show);
    }

    private void OnDestroy()
    {
        if (uiPrompt != null && uiPrompt.transform.parent != null)
            Destroy(uiPrompt.transform.parent.gameObject);
    }
}
