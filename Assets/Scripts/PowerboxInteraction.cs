using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the powerbox prefab root (needs a Collider).
///
/// The player gazes at the powerbox and blinks to restore power.
/// On activation: stops all child ParticleSystems (electricity VFX),
/// optionally disables an Animator, and calls PowerManager.TurnOnPower().
///
/// Setup:
///   1. Add this component to the powerbox prefab root.
///   2. Ensure the root (or a child) has a Collider so the raycast can hit it.
///   3. electricityParticles auto-fills from child ParticleSystems if left empty.
///   4. Assign electricityAnimator if the electricity uses an Animator.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PowerboxInteraction : MonoBehaviour
{
    [Header("Gaze / Blink")]
    [SerializeField] private float  rayDistance   = 6f;
    [SerializeField] private float  gazeHitRadius = 0.4f;
    [SerializeField] private string promptText    = "Blink to restore power";

    [Header("Electricity VFX")]
    [Tooltip("Assign the electricity spark ParticleSystem here — must be set manually.")]
    [SerializeField] private ParticleSystem sparkParticles;
    [Tooltip("Optional Animator to disable when power is restored.")]
    [SerializeField] private Animator electricityAnimator;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private GazeDetector  gazeDetector;
    private BlinkDetector blinkDetector;
    private Camera        playerCamera;

    private bool activated   = false;
    private bool wasBlinking = false;

    private Text uiPrompt;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Resolve particle reference early so OnEnable can use it before Start runs.
        if (sparkParticles == null)
            sparkParticles = GetComponentInChildren<ParticleSystem>();

        if (sparkParticles == null)
            Debug.LogWarning($"[Powerbox] {name} has no Spark Particles assigned and none found in children!");
    }

    private void OnEnable()
    {
        // Restart electricity VFX every time the level becomes visible.
        // This covers both the first visit (Start hasn't run yet) and re-visits
        // after the level was hidden and shown again (Start won't fire a second time).
        if (!activated && sparkParticles != null)
        {
            sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            sparkParticles.Play(true);
        }
    }

    private void Start()
    {
        gazeDetector  = FindObjectOfType<GazeDetector>();
        blinkDetector = FindObjectOfType<BlinkDetector>();
        playerCamera  = Camera.main;

        // sparkParticles already resolved in Awake; particles already started in OnEnable.

        BuildPromptUI();
    }

    private void Update()
    {
        if (activated) return;

        bool gazed = IsGazedAt();
        ShowPrompt(gazed);

        if (gazed)
        {
            bool blinkingNow = blinkDetector != null && blinkDetector.IsBlinking;
            if (blinkingNow && !wasBlinking)
                Activate();
            wasBlinking = blinkingNow;
        }
        else
        {
            wasBlinking = blinkDetector != null && blinkDetector.IsBlinking;
        }
    }

    private void OnDestroy()
    {
        if (uiPrompt != null && uiPrompt.transform.parent != null)
            Destroy(uiPrompt.transform.parent.gameObject);
    }

    // ── Activation ────────────────────────────────────────────────────────────

    private void Activate()
    {
        activated = true;
        ShowPrompt(false);

        if (sparkParticles != null)
            sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        if (electricityAnimator != null)
            electricityAnimator.enabled = false;

        if (PowerManager.Instance != null)
            PowerManager.Instance.TurnOnPower();
    }

    // ── Gaze ─────────────────────────────────────────────────────────────────

    private bool IsGazedAt()
    {
        if (gazeDetector == null || !gazeDetector.IsTracking || playerCamera == null)
            return false;

        Ray ray = gazeDetector.GetGazeRay(playerCamera);

        // Primary: direct raycast (same as keypad — most reliable)
        if (Physics.Raycast(ray, out RaycastHit directHit, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (directHit.collider.transform == transform ||
                directHit.collider.transform.IsChildOf(transform))
                return true;
        }

        // Secondary: sphere cast for off-centre gaze tolerance
        RaycastHit[] hits = Physics.SphereCastAll(ray, gazeHitRadius, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform))
                return true;
        }

        return false;
    }

    // ── Prompt UI ─────────────────────────────────────────────────────────────

    private void BuildPromptUI()
    {
        GameObject canvasObj = new GameObject($"PowerboxCanvas_{GetInstanceID()}");
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
        uiPrompt.color     = new Color(1f, 0.85f, 0.1f); // amber/yellow — matches electricity
        uiPrompt.alignment = TextAnchor.MiddleCenter;

        RectTransform rect    = uiPrompt.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.5f, 0.22f);
        rect.anchorMax        = new Vector2(0.5f, 0.22f);
        rect.sizeDelta        = new Vector2(420, 50);
        rect.anchoredPosition = Vector2.zero;

        uiPrompt.gameObject.SetActive(false);
    }

    private void ShowPrompt(bool show)
    {
        if (uiPrompt != null)
            uiPrompt.gameObject.SetActive(show);
    }
}
