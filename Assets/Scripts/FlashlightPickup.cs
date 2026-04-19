using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gaze + blink flashlight pickup — identical interaction to goggles / batteries.
/// Place this component on a flashlight model prefab sitting on the ground.
/// When the player gazes at it and blinks, the flashlight is "picked up":
///   - FlashlightController is unlocked (F key now works).
///   - The pickup object is destroyed.
///
/// Setup:
///   1. Create a prefab with a 3D flashlight mesh (Collider required).
///   2. Add this component.
///   3. Assign this prefab to BatterySpawnSetup.flashlightPickupPrefab.
///      The dungeon generator will place it at the level 1 stairway entrance.
/// </summary>
[RequireComponent(typeof(Collider))]
public class FlashlightPickup : MonoBehaviour
{
    [Header("Gaze / Blink Settings")]
    [SerializeField] private float  rayDistance   = 5f;
    [SerializeField] private float  gazeHitRadius = 0.5f;
    [SerializeField] private string promptMessage = "Blink to pick up flashlight";

    [Header("Glow")]
    [SerializeField] private Color glowColor     = new Color(1f, 0.9f, 0.3f); // warm yellow
    [SerializeField] private float glowIntensity = 2.5f;
    [SerializeField] private float pulseSpeed    = 3f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private GazeDetector  gazeDetector;
    private BlinkDetector blinkDetector;
    private Camera        playerCamera;

    private Renderer[] itemRenderers;
    private Color[]    originalEmission;
    private Color[]    originalBaseColor;
    private bool[]     supportsEmission;
    private bool[]     usesBaseColor;

    private bool collected   = false;
    private bool wasBlinking = false;

    private Text uiPrompt;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        gazeDetector  = FindObjectOfType<GazeDetector>();
        blinkDetector = FindObjectOfType<BlinkDetector>();
        playerCamera  = Camera.main;

        // Cache per-instance material copies
        itemRenderers    = GetComponentsInChildren<Renderer>();
        originalEmission = new Color[itemRenderers.Length];
        originalBaseColor= new Color[itemRenderers.Length];
        supportsEmission = new bool [itemRenderers.Length];
        usesBaseColor    = new bool [itemRenderers.Length];

        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material mat = itemRenderers[i].material; // per-instance copy

            supportsEmission[i] = mat.HasProperty("_EmissionColor");
            if (supportsEmission[i])
            {
                mat.EnableKeyword("_EMISSION");
                // Mark the material as using realtime emission so Unity does NOT
                // strip the _EMISSION shader variant in builds. Without this, the
                // keyword is enabled at runtime but the variant doesn't exist in
                // the built player — so the glow never appears.
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                originalEmission[i] = mat.GetColor("_EmissionColor");
            }

            if (mat.HasProperty("_BaseColor"))
            {
                usesBaseColor[i]     = true;
                originalBaseColor[i] = mat.GetColor("_BaseColor");
            }
            else if (mat.HasProperty("_Color"))
            {
                usesBaseColor[i]     = false;
                originalBaseColor[i] = mat.GetColor("_Color");
            }
            else
            {
                originalBaseColor[i] = Color.white;
            }
        }

        BuildPromptUI();
    }

    private void Update()
    {
        if (collected) return;

        bool gazed = IsGazedAt();
        ShowPrompt(gazed);

        if (gazed)
        {
            PulseGlow();

            bool blinkingNow = blinkDetector != null && blinkDetector.IsBlinking;
            if (blinkingNow && !wasBlinking)
                Collect();
            wasBlinking = blinkingNow;
        }
        else
        {
            ResetGlow();
            wasBlinking = blinkDetector != null && blinkDetector.IsBlinking;
        }
    }

    private void OnDestroy()
    {
        if (uiPrompt != null && uiPrompt.transform.parent != null)
            Destroy(uiPrompt.transform.parent.gameObject);
    }

    // ── Gaze ─────────────────────────────────────────────────────────────────

    private bool IsGazedAt()
    {
        if (gazeDetector == null || !gazeDetector.IsTracking || playerCamera == null)
            return false;

        Ray ray = gazeDetector.GetGazeRay(playerCamera);

        if (Physics.Raycast(ray, out RaycastHit directHit, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (directHit.collider.transform == transform ||
                directHit.collider.transform.IsChildOf(transform))
                return true;
        }

        RaycastHit[] hits = Physics.SphereCastAll(ray, gazeHitRadius, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform))
                return true;
        }

        // Fallback centre-point check
        Vector3 toItem = transform.position - ray.origin;
        float   along  = Vector3.Dot(ray.direction, toItem);
        if (along > 0f && along <= rayDistance)
        {
            if (Vector3.Distance(ray.origin + ray.direction * along, transform.position) <= gazeHitRadius * 2f)
                return true;
        }

        return false;
    }

    // ── Glow ─────────────────────────────────────────────────────────────────

    private void PulseGlow()
    {
        float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1f);
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;
            if (supportsEmission[i])
                m.SetColor("_EmissionColor", glowColor * glowIntensity * pulse);
            Color tinted = Color.Lerp(originalBaseColor[i], glowColor, pulse * 0.5f);
            if (usesBaseColor[i])             m.SetColor("_BaseColor", tinted);
            else if (m.HasProperty("_Color")) m.SetColor("_Color",     tinted);
        }
    }

    private void ResetGlow()
    {
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;
            if (supportsEmission[i])          m.SetColor("_EmissionColor", originalEmission[i]);
            if (usesBaseColor[i])             m.SetColor("_BaseColor",     originalBaseColor[i]);
            else if (m.HasProperty("_Color")) m.SetColor("_Color",         originalBaseColor[i]);
        }
    }

    // ── Collect ───────────────────────────────────────────────────────────────

    private void Collect()
    {
        if (collected) return;
        collected = true;

        ResetGlow();
        ShowPrompt(false);

        if (FlashlightController.Instance != null)
        {
            FlashlightController.Instance.UnlockFlashlight();
            Debug.Log("[FlashlightPickup] Flashlight picked up — flashlight unlocked.");
        }
        else
        {
            Debug.LogWarning("[FlashlightPickup] FlashlightController.Instance is null — flashlight not unlocked.");
        }

        FlashlightHUD.Instance?.ShowNotification("Press F to turn on / off the flashlight", 5f);

        Destroy(gameObject);
    }

    // ── Prompt UI ─────────────────────────────────────────────────────────────

    private void BuildPromptUI()
    {
        GameObject canvasObj = new GameObject($"FlashlightPickupCanvas_{GetInstanceID()}");
        Canvas canvas        = canvasObj.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder  = 100;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject textObj = new GameObject("PromptText");
        textObj.transform.SetParent(canvas.transform, false);

        uiPrompt           = textObj.AddComponent<Text>();
        uiPrompt.text      = promptMessage;
        uiPrompt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiPrompt.fontSize  = 26;
        uiPrompt.color     = new Color(1f, 0.9f, 0.3f);
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
        if (uiPrompt != null) uiPrompt.gameObject.SetActive(show);
    }
}
