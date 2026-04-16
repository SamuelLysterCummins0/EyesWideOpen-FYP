using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Receives Timeline signals and drives everything that can't
/// be done with pure keyframes: light flicker, camera shake,
/// screen fade, and scene loading.
///
/// The elevator "fall" is faked — no geometry actually moves
/// downward. The illusion comes from escalating camera shake on
/// the ElevatorCar, a subtle downward drift, red emissive ceiling,
/// and audio.
///
/// Attach to Director. Wire all Inspector fields.
/// </summary>
public class IntroCinematicController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string mainGameSceneName = "MainGame";

    [Header("Elevator Emissive Ceiling")]
    [Tooltip("The Renderer on the elevator ceiling mesh with the emissive material.")]
    [SerializeField] private Renderer ceilingRenderer;
    [Tooltip("Index of the emissive material on that renderer (usually 0).")]
    [SerializeField] private int ceilingMaterialIndex = 0;
    [SerializeField] private Color normalEmissiveColor  = new Color(1f, 0.95f, 0.8f); // warm white
    [SerializeField] private Color redEmissiveColor     = new Color(1f, 0.05f, 0f);   // deep red
    [SerializeField] private float redEmissiveIntensity = 2f;                         // HDR multiplier

    [Header("Camera Shake")]
    [Tooltip("ElevatorCar root — shaking this moves everything inside it together: walls, character, camera.")]
    [SerializeField] private Transform elevatorCar;
    [SerializeField] private float shakeStartIntensity = 0.02f;
    [SerializeField] private float shakeEndIntensity   = 0.12f;
    [SerializeField] private float shakeDuration       = 8f;

    [Header("Elevator Doors")]
    [SerializeField] private ElevatorDoors elevatorDoors;

    [Header("Crash Impact")]
    [Tooltip("How hard the elevator slams down on impact — higher = more violent.")]
    [SerializeField] private float crashImpactIntensity = 0.4f;
    [Tooltip("How long the impact punch lasts in seconds before resetting.")]
    [SerializeField] private float crashImpactDuration  = 0.3f;

    [Header("Fade")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeInDuration  = 1.0f;
    [SerializeField] private float fadeOutDuration = 1.5f;

    private Coroutine shakeCoroutine;
    private Vector3   elevatorCarOriginalPos;

    // ── Timeline Signal Receivers ─────────────────────────────────────────────

    /// <summary>Very start of cinematic — fades screen in from black.</summary>
    public void OnCinematicStart()
    {
        StartCoroutine(FadeIn());
    }

    /// <summary>Player reaches elevator button — doors open.</summary>
    public void OnOpenElevatorDoors()
    {
        elevatorDoors?.OpenForCinematic();
    }

    /// <summary>Elevator jolts to a hard stop.</summary>
    public void OnHardStop()
    {
        StartCoroutine(FlickerLights());
        StartCoroutine(SingleShakePunch(0.18f));
    }

    /// <summary>Elevator begins "falling" — escalating shake starts.</summary>
    public void OnFallStart()
    {
        // Cache position now — Timeline has moved ElevatorCar into place by this point
        if (elevatorCar != null) elevatorCarOriginalPos = elevatorCar.localPosition;

        if (shakeCoroutine != null) StopCoroutine(shakeCoroutine);
        shakeCoroutine = StartCoroutine(EscalatingShake());
    }

    /// <summary>Crash — sharp impact punch, shake stops, screen fades to black.</summary>
    public void OnCrash()
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeCoroutine = null;
        }

        StartCoroutine(CrashImpact());
    }

    /// <summary>After fade completes — loads the main game scene.</summary>
    public void OnLoadScene()
    {
        SceneManager.LoadSceneAsync(mainGameSceneName);
    }

    // ── Crash Impact ──────────────────────────────────────────────────────────

    /// <summary>
    /// Slams the elevator sharply downward on impact, shakes violently for a
    /// brief moment, then resets and starts the fade to black.
    /// </summary>
    private IEnumerator CrashImpact()
    {
        if (elevatorCar == null) yield break;

        // Phase 1 — snap downward hard (the hit)
        elevatorCar.localPosition = elevatorCarOriginalPos + Vector3.down * crashImpactIntensity;
        yield return null;

        // Phase 2 — violent rapid shake that decays quickly
        float elapsed = 0f;
        while (elapsed < crashImpactDuration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / crashImpactDuration); // decays to zero
            elevatorCar.localPosition = elevatorCarOriginalPos
                + Random.insideUnitSphere * crashImpactIntensity * t;
            yield return null;
        }

        // Phase 3 — reset and fade
        elevatorCar.localPosition = elevatorCarOriginalPos;
        StartCoroutine(FadeOut());
    }

    // ── Shake ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shakes the ElevatorCar with escalating intensity over shakeDuration seconds.
    /// Because the camera lives inside ElevatorCar, it shakes with it automatically
    /// without conflicting with the CameraRoot Animation Track.
    /// </summary>
    private IEnumerator EscalatingShake()
    {
        if (elevatorCar == null) yield break;

        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            float t         = elapsed / shakeDuration;
            float intensity = Mathf.Lerp(shakeStartIntensity, shakeEndIntensity, t);

            elevatorCar.localPosition = elevatorCarOriginalPos + Random.insideUnitSphere * intensity;

            yield return null;
        }
    }

    /// <summary>Single sharp punch for the hard stop jolt — fades out quickly.</summary>
    private IEnumerator SingleShakePunch(float intensity)
    {
        if (elevatorCar == null) yield break;

        // Cache position at the moment of the hard stop
        Vector3 carOrigin = elevatorCar.localPosition;

        float duration = 0.25f;
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / duration);
            elevatorCar.localPosition = carOrigin + Random.insideUnitSphere * intensity * t;
            yield return null;
        }

        elevatorCar.localPosition = carOrigin;
    }

    // ── Emissive Ceiling ──────────────────────────────────────────────────────

    private IEnumerator FlickerLights()
    {
        if (ceilingRenderer == null) yield break;

        Material mat      = ceilingRenderer.materials[ceilingMaterialIndex];
        bool     isNormal = true;

        // Rapid flicker between normal colour and off (black)
        for (int i = 0; i < 8; i++)
        {
            isNormal = !isNormal;
            mat.SetColor("_EmissionColor", isNormal ? normalEmissiveColor : Color.black);
            yield return new WaitForSeconds(0.07f);
        }

        // Settle on red
        mat.SetColor("_EmissionColor", redEmissiveColor * redEmissiveIntensity);
    }

    // ── Fade ──────────────────────────────────────────────────────────────────

    private IEnumerator FadeIn()
    {
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeInDuration);
            yield return null;
        }
        fadeCanvasGroup.alpha = 0f;
    }

    private IEnumerator FadeOut()
    {
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeOutDuration);
            yield return null;
        }
        fadeCanvasGroup.alpha = 1f;
    }
}
