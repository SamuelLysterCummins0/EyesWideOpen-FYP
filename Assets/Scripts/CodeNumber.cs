using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Represents a single collectible code digit scratched onto a wall.
/// The player collects it by gazing at it for dwellTime seconds.
///
/// Gaze detection mirrors GazeKeypadInteraction: uses GazeDetector.GetGazeRay()
/// then Physics.Raycast and checks if the hit belongs to this object.
///
/// Prefab setup:
///   - Root: has BoxCollider + this script
///   - Child "Quad": MeshRenderer (Unlit or Standard shader) facing +Z (the inward normal)
///   - Child "LabelCanvas": World-space Canvas → TMP_Text (the digit + scratch marks)
///   - Child "ProgressRing": SpriteRenderer for the radial fill progress indicator (optional)
/// </summary>
public class CodeNumber : MonoBehaviour
{
    [Header("Digit Data (set by CodeNumberManager)")]
    [SerializeField] private int digit;
    [SerializeField] private int orderIndex; // 0-3 (determines scratch marks shown)

    [Header("Gaze Settings")]
    [SerializeField] private float dwellTime = 1.5f;
    [SerializeField] private float maxGazeDistance = 8f;

    [Header("Visual Feedback")]
    [SerializeField] private Color gazeGlowColor = new Color(1f, 0.85f, 0.3f); // Warm yellow glow
    [SerializeField] private float glowIntensity = 2f;
    [SerializeField] private Renderer quadRenderer;      // The physical quad on the wall
    [SerializeField] private TMP_Text digitLabel;        // TMP text showing digit + scratch marks
    [SerializeField] private SpriteRenderer progressRing; // Optional fill indicator

    [Header("Audio")]
    [SerializeField] private AudioClip collectSound;
    [SerializeField] private AudioSource audioSource;

    // ── Runtime State ────────────────────────────────────────────────────────
    private GazeDetector gazeDetector;
    private Camera playerCamera;
    private float gazeLookTime = 0f;
    private bool isGazedAt = false;
    private bool isCollected = false;

    private Color originalEmissionColor;
    private Color originalTextColor;             // Cached on Awake, restored when gaze leaves
    private Action<int, int> onCollectedCallback; // (orderIndex, digit)

    // Scratch marks by position (1=first found, 4=last found).
    private static readonly string[] scratchMarks = { "/", "//", "///", "////" };

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by CodeNumberManager right after instantiation.
    /// </summary>
    public void Initialise(int digitValue, int order, Action<int, int> callback)
    {
        digit = digitValue;
        orderIndex = order;
        onCollectedCallback = callback;

        RefreshLabel();
    }

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        // Cache the TMP text's original colour so we can restore it when gaze leaves.
        originalTextColor = (digitLabel != null) ? digitLabel.color : Color.white;

        // Quad renderer emission setup — kept for backwards-compat if a quad is assigned,
        // but the preferred visual is TMP-based glow (quad field can be left null).
        if (quadRenderer != null)
        {
            quadRenderer.material.EnableKeyword("_EMISSION");
            originalEmissionColor = quadRenderer.material.HasProperty("_EmissionColor")
                ? quadRenderer.material.GetColor("_EmissionColor")
                : Color.black;
        }
    }

    private void Start()
    {
        playerCamera = Camera.main;
        gazeDetector = FindObjectOfType<GazeDetector>();

        if (gazeDetector == null)
            Debug.LogWarning("[CodeNumber] GazeDetector not found in scene.");

        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (digitLabel == null) return;

        string marks = (orderIndex >= 0 && orderIndex < scratchMarks.Length)
            ? scratchMarks[orderIndex]
            : "";

        // Show the number large, scratch marks below it as a positional hint.
        digitLabel.text = $"{digit}\n<size=60%>{marks}</size>";
    }

    // ── Gaze Update Loop (mirrors GazeKeypadInteraction pattern) ─────────────

    private void Update()
    {
        if (isCollected) return;

        if (PowerManager.Instance != null && PowerManager.Instance.IsOutageLevelPoweredOff)
        {
            ResetGaze();
            return;
        }

        if (gazeDetector == null || !gazeDetector.IsTracking)
        {
            ResetGaze();
            return;
        }

        Ray gazeRay = gazeDetector.GetGazeRay(playerCamera);

        if (Physics.Raycast(gazeRay, out RaycastHit hit, maxGazeDistance))
        {
            // Accept hit on this object or any of its children (same check as keypad).
            bool hitThis = hit.collider.transform == transform ||
                           hit.collider.transform.IsChildOf(transform);

            if (hitThis)
            {
                if (!isGazedAt) OnGazeEnter();

                gazeLookTime += Time.deltaTime;
                float progress = Mathf.Clamp01(gazeLookTime / dwellTime);
                OnGazeStay(progress);

                if (gazeLookTime >= dwellTime)
                    Collect();
            }
            else
            {
                ResetGaze();
            }
        }
        else
        {
            ResetGaze();
        }
    }

    // ── Gaze Feedback ─────────────────────────────────────────────────────────

    private void OnGazeEnter()
    {
        isGazedAt = true;
        // Start glow — pulsing happens each frame in OnGazeStay.
    }

    private void OnGazeStay(float progress)
    {
        // Pulse value shared by both text and ring (matches keypad PingPong pattern).
        float pulse = Mathf.PingPong(Time.time * 2f, 1f);

        // Pulse the TMP text colour between its normal colour and the glow colour.
        if (digitLabel != null)
            digitLabel.color = Color.Lerp(originalTextColor, gazeGlowColor, pulse);

        // If a quad renderer is assigned, pulse its emission too (optional, can be null).
        if (quadRenderer != null)
            quadRenderer.material.SetColor("_EmissionColor", gazeGlowColor * glowIntensity * pulse);

        // Progress ring: scales up with dwell progress AND pulses in colour intensity.
        if (progressRing != null)
        {
            progressRing.enabled = true;
            progressRing.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, progress);
            // Pulse between a dim version and the full glow colour.
            Color dimGlow = new Color(gazeGlowColor.r, gazeGlowColor.g, gazeGlowColor.b, 0.2f);
            progressRing.color = Color.Lerp(dimGlow, gazeGlowColor, pulse);
        }
    }

    private void ResetGaze()
    {
        if (!isGazedAt) return;
        isGazedAt = false;
        gazeLookTime = 0f;

        // Restore text to its original colour.
        if (digitLabel != null)
            digitLabel.color = originalTextColor;

        // Restore quad emission if present.
        if (quadRenderer != null)
            quadRenderer.material.SetColor("_EmissionColor", originalEmissionColor);

        if (progressRing != null)
            progressRing.enabled = false;
    }

    // ── Collection ────────────────────────────────────────────────────────────

    private void Collect()
    {
        if (isCollected) return;
        isCollected = true;

        // Audio feedback.
        if (collectSound != null && audioSource != null)
            audioSource.PlayOneShot(collectSound);

        // Hide the number from the world.
        if (quadRenderer != null) quadRenderer.enabled = false;
        if (digitLabel != null) digitLabel.enabled = false;
        if (progressRing != null) progressRing.enabled = false;

        // Disable collider so it can't be re-triggered.
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Notify the manager.
        onCollectedCallback?.Invoke(orderIndex, digit);

        // Fade out and destroy after the audio finishes.
        float delay = (collectSound != null) ? collectSound.length : 0.5f;
        Destroy(gameObject, delay + 0.1f);
    }
}
