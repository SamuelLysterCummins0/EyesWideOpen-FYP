using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the intro room exit door alongside SafeRoomDoor.
/// Reads the door prefab's existing TextMeshProUGUI child (same pattern as
/// ItemPickUp / SafeRoomDoor) and drives contextual messages based on the
/// IntroRoomController state.
///
/// Phase 1 — AwaitingFaceScan  : proximity text "COMPLETE FACE SCAN"  (no gaze yet)
/// Phase 2 — AwaitingAudiotape : gaze text       "FIND AUDIOTAPE"
/// Phase 3 — ReadyToExit       : gaze + blink opens the door via SafeRoomDoor.Interact()
/// </summary>
[RequireComponent(typeof(SafeRoomDoor))]
public class IntroDoorInteraction : MonoBehaviour
{
    [Header("Gaze Detection")]
    [SerializeField] private float     gazeRayDistance  = 10f;
    [SerializeField] private float     sphereCastRadius = 0.35f;
    [SerializeField] private LayerMask doorLayer        = ~0;

    [Header("Proximity (pre-scan phase)")]
    [SerializeField] private float proximityRadius = 3f;

    // ── Runtime ────────────────────────────────────────────────────────────────
    private SafeRoomDoor    safeDoor;
    private GazeDetector    gazeDetector;
    private BlinkDetector   blinkDetector;
    private Camera          playerCamera;
    private Transform       player;
    private TextMeshProUGUI promptText;   // found on the door prefab's child

    private bool isOpen      = false;
    private bool isGazedAt   = false;
    private bool wasBlinking = false;

    // Fallback prompt used when the door prefab has no TMP child
    private GameObject promptCanvas;
    private Text       legacyPromptText;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Start()
    {
        safeDoor      = GetComponent<SafeRoomDoor>();
        gazeDetector  = FindObjectOfType<GazeDetector>();
        blinkDetector = FindObjectOfType<BlinkDetector>();
        playerCamera  = Camera.main;
        player        = GameObject.FindWithTag("Player")?.transform;

        // Try to use a TMP text already on the door prefab.
        promptText = GetComponentInChildren<TextMeshProUGUI>();
        if (promptText != null)
        {
            promptText.enabled = false;
        }
        else
        {
            // Prefab has no TMP child — build a screen-space canvas at runtime.
            BuildFallbackPrompt();
        }
    }

    private void BuildFallbackPrompt()
    {
        promptCanvas = new GameObject("IntroDoorPromptCanvas");
        Canvas canvas = promptCanvas.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        promptCanvas.AddComponent<CanvasScaler>();
        promptCanvas.AddComponent<GraphicRaycaster>();

        GameObject textObj = new GameObject("IntroDoorPromptText");
        textObj.transform.SetParent(promptCanvas.transform, false);

        legacyPromptText = textObj.AddComponent<Text>();
        legacyPromptText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        legacyPromptText.fontSize  = 28;
        legacyPromptText.alignment = TextAnchor.MiddleCenter;

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.5f, 0.25f);
        rect.anchorMax        = new Vector2(0.5f, 0.25f);
        rect.sizeDelta        = new Vector2(600f, 60f);
        rect.anchoredPosition = Vector2.zero;

        promptCanvas.SetActive(false);
    }

    private void OnDestroy()
    {
        if (promptCanvas != null) Destroy(promptCanvas);
    }

    private void Update()
    {
        if (isOpen || player == null) return;

        IntroRoomController ctrl = IntroRoomController.Instance;

        // ── Phase 1: no gaze cursor — proximity only ───────────────────────────
        if (ctrl == null || ctrl.CurrentState == IntroRoomController.IntroState.AwaitingFaceScan)
        {
            bool near = Vector3.Distance(player.position, transform.position) <= proximityRadius;
            ShowPrompt(near, "COMPLETE FACE SCAN", false);
            return;
        }

        // ── Phase 2 & 3: gaze-driven ───────────────────────────────────────────
        DetectGaze();
        HandleBlink();
    }

    // ── Gaze ───────────────────────────────────────────────────────────────────

    private void DetectGaze()
    {
        if (gazeDetector == null || playerCamera == null) return;

        Ray  ray     = gazeDetector.GetGazeRay(playerCamera);
        bool hitDoor = false;

        if (Physics.Raycast(ray, out RaycastHit hit, gazeRayDistance, doorLayer))
            hitDoor = IsThisDoor(hit.collider);

        if (!hitDoor)
        {
            foreach (RaycastHit h in Physics.SphereCastAll(ray, sphereCastRadius, gazeRayDistance, doorLayer))
                if (IsThisDoor(h.collider)) { hitDoor = true; break; }
        }

        if (hitDoor == isGazedAt) return;
        isGazedAt = hitDoor;
        RefreshPrompt();
    }

    private bool IsThisDoor(Collider col)
        => col.gameObject == gameObject || col.transform.IsChildOf(transform);

    private void RefreshPrompt()
    {
        if (!isGazedAt) { ShowPrompt(false, "", false); return; }

        IntroRoomController ctrl = IntroRoomController.Instance;
        bool unlocked = ctrl != null && ctrl.IsDoorUnlocked();
        string msg    = unlocked ? "[BLINK]  Open door" : ctrl?.GetDoorMessage() ?? "";
        ShowPrompt(true, msg, unlocked);
    }

    // ── Blink ──────────────────────────────────────────────────────────────────

    private void HandleBlink()
    {
        if (!isGazedAt || blinkDetector == null) return;
        if (IntroRoomController.Instance == null || !IntroRoomController.Instance.IsDoorUnlocked()) return;

        bool blinking = blinkDetector.IsBlinking;
        if (blinking && !wasBlinking)
            OpenDoor();
        wasBlinking = blinking;
    }

    private void OpenDoor()
    {
        isOpen = true;
        ShowPrompt(false, "", false);
        // Delegate the actual slide + audio to SafeRoomDoor
        safeDoor.Interact();
        Debug.Log("[IntroDoor] Exit door opened.");
    }

    // ── Prompt ─────────────────────────────────────────────────────────────────

    private void ShowPrompt(bool visible, string text, bool unlocked)
    {
        Color col = unlocked
            ? new Color(0.15f, 1f, 0.45f)   // green — can open
            : new Color(1f, 0.25f, 0.15f);  // red   — blocked

        // TMP path (prefab has a text child)
        if (promptText != null)
        {
            promptText.enabled = visible;
            if (visible) { promptText.text = text; promptText.color = col; }
            return;
        }

        // Fallback legacy-Text path
        if (legacyPromptText != null && promptCanvas != null)
        {
            promptCanvas.SetActive(visible);
            if (visible) { legacyPromptText.text = text; legacyPromptText.color = col; }
        }
    }
}
