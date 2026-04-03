using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the root of each breakable-wall prefab.
///
/// Prefab setup:
///   Root GO  ← this script + Box Collider
///   └─ WallPanel  ← the child mesh that slides up when broken
///
/// Goggle glow:
///   Assign a Goggle Glow Material in the Inspector (an emissive version of the
///   wall material). When the player activates goggles, the WallPanel's renderer
///   swaps to that material so the wall glows.  Leave it null to disable glow.
///
/// Breaking:
///   Player must have goggles active, be gazing at this wall, then blink.
///   Only the WallPanel slides up and disappears — the root stays briefly
///   then is destroyed, clearing the passage collider.
/// </summary>
public class BreakableWall : MonoBehaviour
{
    [Header("Wall Parts")]
    [Tooltip("The inner wall mesh child that slides up on break. If not assigned, auto-detected as first child with a Renderer.")]
    [SerializeField] private Transform wallPanel;

    [Header("Goggle Glow")]
    [Tooltip("Material applied to the WallPanel renderer when goggles are active — should be an emissive version of the wall material. Leave null to disable.")]
    [SerializeField] private Material goggleGlowMaterial;

    [Header("Break Animation")]
    [SerializeField] private float slideDistance = 3.2f;
    [SerializeField] private float slideSpeed    = 0.45f;

    [Header("Audio")]
    [SerializeField] private AudioClip  breakSound;
    [SerializeField] private AudioSource audioSource;

    [Header("Gaze / Blink Settings")]
    [SerializeField] private float  rayDistance   = 5f;
    [SerializeField] private float  gazeHitRadius = 0.3f;
    [SerializeField] private string promptText    = "Blink to break wall";

    // ── Runtime ───────────────────────────────────────────────────────────────
    private GazeDetector  gazeDetector;
    private BlinkDetector blinkDetector;
    private Camera        playerCamera;
    private bool          isBroken      = false;
    private bool          blinkConsumed = false;
    private Text          uiPrompt;

    // Material glow state
    private Renderer   wallPanelRenderer;
    private Material[] originalSharedMaterials;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        gazeDetector  = FindObjectOfType<GazeDetector>();
        blinkDetector = FindObjectOfType<BlinkDetector>();
        playerCamera  = Camera.main;

        // Auto-detect wallPanel if not assigned in Inspector
        if (wallPanel == null)
        {
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null) wallPanel = r.transform;
        }

        // Cache wall panel renderer and its original materials for glow swapping.
        // Use GetComponentInChildren — the renderer may be on a grandchild
        // (e.g. Root → Empty.003 → WallHole_B_Debris which has the Mesh Renderer).
        if (wallPanel != null)
        {
            wallPanelRenderer = wallPanel.GetComponentInChildren<Renderer>();
            if (wallPanelRenderer != null)
                originalSharedMaterials = wallPanelRenderer.sharedMaterials;
        }

        // Register with GoggleController so it can trigger glow on/off
        if (GoggleController.Instance != null)
            GoggleController.Instance.RegisterWall(this);

        BuildPromptUI();
    }

    private void OnDestroy()
    {
        if (GoggleController.Instance != null)
            GoggleController.Instance.UnregisterWall(this);

        // Clean up the prompt canvas we created at runtime
        // uiPrompt is on textObj, textObj's parent IS the canvas root
        if (uiPrompt != null && uiPrompt.transform.parent != null)
            Destroy(uiPrompt.transform.parent.gameObject);
    }

    private void Update()
    {
        if (isBroken) return;

        if (PowerManager.Instance != null && PowerManager.Instance.IsOutageLevelPoweredOff)
        {
            SetPromptVisible(false);
            return;
        }

        bool gogglesOn = GoggleController.Instance != null && GoggleController.Instance.IsActive;

        if (!gogglesOn)
        {
            SetPromptVisible(false);
            return;
        }

        bool gazedAt = IsGazedAt();
        SetPromptVisible(gazedAt);

        if (gazedAt && blinkDetector != null)
        {
            if (blinkDetector.IsBlinking && !blinkConsumed)
            {
                Break();
                blinkConsumed = true;
            }
            if (!blinkDetector.IsBlinking)
                blinkConsumed = false;
        }
        else
        {
            blinkConsumed = false;
        }
    }

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GoggleController to make the wall glow (or stop glowing).
    /// Swaps the WallPanel's renderer material when a glow material is assigned.
    /// </summary>
    public void SetCrackVisible(bool visible)
    {
        if (wallPanelRenderer == null || goggleGlowMaterial == null) return;

        if (visible)
        {
            Material[] glowArray = new Material[wallPanelRenderer.sharedMaterials.Length];
            for (int i = 0; i < glowArray.Length; i++)
                glowArray[i] = goggleGlowMaterial;
            wallPanelRenderer.sharedMaterials = glowArray;
        }
        else
        {
            wallPanelRenderer.sharedMaterials = originalSharedMaterials;
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private bool IsGazedAt()
    {
        if (gazeDetector == null || !gazeDetector.IsTracking || playerCamera == null)
            return false;

        Ray ray = gazeDetector.GetGazeRay(playerCamera);

        // Primary: simple raycast — most reliable for looking directly at flat surfaces
        if (Physics.Raycast(ray, out RaycastHit directHit, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (directHit.collider.transform == transform ||
                directHit.collider.transform.IsChildOf(transform))
                return true;
        }

        // Secondary: SphereCastAll — catches off-centre gaze and cases where another
        // collider blocks the thin ray but the sphere still reaches this object.
        RaycastHit[] hits = Physics.SphereCastAll(ray, gazeHitRadius, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
                return true;
        }

        // Fallback: ray-to-centre distance check — fires even when SphereCast misses
        // the flat face of a BoxCollider on a dead-on approach.
        Vector3 toWall = transform.position - ray.origin;
        float   along  = Vector3.Dot(ray.direction, toWall);
        if (along > 0f && along <= rayDistance)
        {
            Vector3 closest = ray.origin + ray.direction * along;
            if (Vector3.Distance(closest, transform.position) <= gazeHitRadius * 2f)
                return true;
        }

        return false;
    }

    private void Break()
    {
        if (isBroken) return;
        isBroken = true;

        // Restore original material before the wall disappears
        if (wallPanelRenderer != null && originalSharedMaterials != null)
            wallPanelRenderer.sharedMaterials = originalSharedMaterials;

        SetPromptVisible(false);

        if (audioSource != null && breakSound != null)
            audioSource.PlayOneShot(breakSound);

        // Disable all colliders on the root immediately so the player can walk through
        foreach (Collider col in GetComponents<Collider>())
            col.enabled = false;

        if (wallPanel != null)
            StartCoroutine(SlideAndDestroy());
        else
            Destroy(gameObject, 0.1f);
    }

    private IEnumerator SlideAndDestroy()
    {
        Vector3 start   = wallPanel.localPosition;
        Vector3 target  = start + Vector3.up * slideDistance;
        float   elapsed = 0f;

        while (elapsed < slideSpeed)
        {
            elapsed += Time.deltaTime;
            wallPanel.localPosition = Vector3.Lerp(start, target, elapsed / slideSpeed);
            yield return null;
        }

        wallPanel.localPosition = target;
        Destroy(wallPanel.gameObject, 0.1f);
    }

    private void SetPromptVisible(bool visible)
    {
        if (uiPrompt != null)
            uiPrompt.gameObject.SetActive(visible);
    }

    private void BuildPromptUI()
    {
        // Canvas root
        GameObject canvasObj = new GameObject($"BreakableWallCanvas_{GetInstanceID()}");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Text child  — uiPrompt.transform.parent IS canvasObj.transform
        GameObject textObj = new GameObject("PromptText");
        textObj.transform.SetParent(canvas.transform, false);

        uiPrompt           = textObj.AddComponent<Text>();
        uiPrompt.text      = promptText;
        uiPrompt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiPrompt.fontSize  = 26;
        uiPrompt.color     = new Color(0.85f, 0.7f, 1f);
        uiPrompt.alignment = TextAnchor.MiddleCenter;

        RectTransform rect    = uiPrompt.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.5f, 0.22f);
        rect.anchorMax        = new Vector2(0.5f, 0.22f);
        rect.sizeDelta        = new Vector2(500, 50);
        rect.anchoredPosition = Vector2.zero;

        uiPrompt.gameObject.SetActive(false);
    }
}
