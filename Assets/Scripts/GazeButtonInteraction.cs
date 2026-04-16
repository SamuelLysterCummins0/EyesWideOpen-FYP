using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gaze-and-blink interaction for the detonation button inside the detonation room.
/// Pattern mirrors GazeItemPickup: look at the button (it pulses red), blink to press it.
///
/// Setup:
///   1. Add this component to the detonation button mesh object inside the DetonationRoom prefab.
///   2. The button object needs at least one Collider (Box or Sphere).
///   3. GazeDetector and BlinkDetector are found automatically at runtime.
///
/// Notes:
///   • The button is only interactive when DetonationManager.IsDetonationActive is FALSE
///     (prevents re-triggering while sequence is already running).
///   • On respawn, DetonationManager.OnRespawn() re-enables this component so the player
///     can press it again.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GazeButtonInteraction : MonoBehaviour
{
    [Header("Gaze / Blink Settings")]
    [SerializeField] private float  rayDistance   = 6f;
    [SerializeField] private float  gazeHitRadius = 0.5f;
    [SerializeField] private string promptText    = "Blink to detonate";

    [Header("Glow")]
    [SerializeField] private Color glowColor     = new Color(1f, 0.2f, 0.2f); // red pulse
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

    private bool isGazedAt   = false;
    private bool wasBlinking = false;

    private Text uiPrompt;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        gazeDetector  = FindObjectOfType<GazeDetector>();
        blinkDetector = FindObjectOfType<BlinkDetector>();
        playerCamera  = Camera.main;

        // Cache instance materials so we can tint just this button
        itemRenderers     = GetComponentsInChildren<Renderer>();
        originalEmission  = new Color[itemRenderers.Length];
        originalBaseColor = new Color[itemRenderers.Length];
        supportsEmission  = new bool[itemRenderers.Length];
        usesBaseColor     = new bool[itemRenderers.Length];

        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material mat = itemRenderers[i].material; // creates instance copy

            supportsEmission[i] = mat.HasProperty("_EmissionColor");
            if (supportsEmission[i])
            {
                mat.EnableKeyword("_EMISSION");
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
        // Button is only interactive when no detonation sequence is running
        if (DetonationManager.Instance != null && DetonationManager.Instance.IsDetonationActive)
        {
            ResetGlow();
            ShowPrompt(false);
            wasBlinking = blinkDetector != null && blinkDetector.IsBlinking;
            return;
        }

        isGazedAt = CheckGaze();

        if (isGazedAt)
        {
            PulseGlow();
            ShowPrompt(true);

            bool blinkingNow = blinkDetector != null && blinkDetector.IsBlinking;
            if (blinkingNow && !wasBlinking)
                PressButton();
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

        // Primary: direct raycast
        if (Physics.Raycast(ray, out RaycastHit directHit, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (directHit.collider.transform == transform ||
                directHit.collider.transform.IsChildOf(transform))
                return true;
        }

        // Secondary: sphere cast for looser detection
        RaycastHit[] hits = Physics.SphereCastAll(ray, gazeHitRadius, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
                return true;
        }

        // Fallback: closest point on ray to object centre
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

    // ── Press ─────────────────────────────────────────────────────────────────

    private void PressButton()
    {
        Debug.Log("[GazeButtonInteraction] Detonation button pressed!");
        ResetGlow();
        ShowPrompt(false);

        DetonationManager.Instance?.StartDetonationSequence();
    }

    // ── Prompt UI ─────────────────────────────────────────────────────────────

    private void BuildPromptUI()
    {
        GameObject canvasObj = new GameObject($"DetonationButtonCanvas_{GetInstanceID()}");
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
        uiPrompt.color     = new Color(1f, 0.3f, 0.3f);
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
