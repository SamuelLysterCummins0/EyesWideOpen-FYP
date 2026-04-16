using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives all insanity visual and audio effects by subscribing to InsanityManager events.
///
/// URP Volume (priority 75) controls:
///   Stage 1 (0–25%)  : mild grain, slight desaturation
///   Stage 2 (25–50%) : grain + chromatic aberration + faint audio echo
///   Stage 3 (50–75%) : heavy grain, vignette pulse, whisper audio, hallucination flashes
///   Stage 4 (75–100%): heavy distortion, frequent hallucinations, audio pitch shift
///
/// Break Event        : screen white flash coroutine
///
/// Setup:
///   1. Add this component to the Main Camera GameObject.
///   2. Assign the hallucinationPrefab (a simple black quad with semi-transparent material).
///   3. Assign whisperClip and breakEventStingClip AudioClips.
///   4. No URP Volume needed in the scene — this script creates one at runtime (priority 75).
///      This sits between PowerManager (50), SirenPhaseManager (60), and GoggleController (100).
/// </summary>
public class InsanityVFX : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Hallucination")]
    [Tooltip("A prefab (simple quad with dark semi-transparent material) that flashes briefly in view.")]
    [SerializeField] private GameObject hallucinationPrefab;
    [Tooltip("How many hallucination instances to pool.")]
    [SerializeField] private int hallucinationPoolSize = 4;
    [Tooltip("How long each hallucination silhouette is visible.")]
    [SerializeField] private float hallucinationDuration = 0.35f;
    [Tooltip("Seconds between hallucinations at Stage 3.")]
    [SerializeField] private float stage3HallucinationInterval = 22f;
    [Tooltip("Seconds between hallucinations at Stage 4.")]
    [SerializeField] private float stage4HallucinationInterval = 10f;

    [Header("Audio")]
    [SerializeField] private AudioClip whisperClip;
    [SerializeField] private AudioClip breakEventStingClip;
    [SerializeField] [Range(0f, 1f)] private float whisperMaxVolume = 0.4f;

    [Header("Break Event Flash")]
    [SerializeField] private float flashDuration = 0.5f;

    // ── Volume override targets per stage ────────────────────────────────────
    // These are the target values; actual Volume parameters are lerped each frame.
    private static readonly float[] GrainIntensityTargets      = { 0.20f, 0.55f, 0.82f, 1.00f };
    // 'response' is FilmGrain's luminance-response parameter (0=uniform, 1=shadows only)
    private static readonly float[] GrainResponseTargets       = { 0.0f,  0.3f,  0.65f, 0.95f };
    private static readonly float[] ChromaticAberrationTargets = { 0f,   0.45f, 0.80f, 1.00f };
    private static readonly float[] SaturationTargets          = { 0f,  -30f,  -65f,  -90f   };
    private static readonly float[] VignetteIntensityTargets   = { 0f,   0.20f, 0.42f, 0.60f };
    private static readonly float[] LensDistortionTargets      = { 0f,  -0.10f,-0.30f,-0.55f };
    private const float LerpSpeed = 3f; // How fast effects blend between stages

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Volume              insanityVolume;
    private FilmGrain           filmGrain;
    private ChromaticAberration chromaticAberration;
    private ColorAdjustments    colorAdjustments;
    private Vignette            vignette;
    private LensDistortion      lensDistortion;

    private AudioSource whisperSource;
    private AudioSource stingSource;

    private Camera     playerCamera;
    private Canvas     flashCanvas;
    private UnityEngine.UI.Image flashImage;

    private readonly Queue<GameObject> hallucinationPool = new Queue<GameObject>();

    private int   currentStage   = 0;
    private float currentInsanity = 0f;
    private float vignettePhase  = 0f; // For vignette pulse in stage 3+
    private Coroutine hallucinationCoroutine;

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Start()
    {
        playerCamera = GetComponent<Camera>();
        if (playerCamera == null)
            playerCamera = Camera.main;

        BuildVolume();
        BuildAudio();
        BuildFlashOverlay();
        BuildHallucinationPool();

        // Subscribe to InsanityManager
        if (InsanityManager.Instance != null)
        {
            InsanityManager.Instance.OnInsanityChanged += OnInsanityChanged;
            InsanityManager.Instance.OnStageChanged    += OnStageChanged;
            InsanityManager.Instance.OnBreakEvent      += OnBreakEvent;
        }
        else
        {
            Debug.LogWarning("[InsanityVFX] InsanityManager.Instance is null — effects will not respond. " +
                             "Ensure InsanityManager is in the scene and executes before InsanityVFX.");
        }
    }

    private void OnDestroy()
    {
        if (InsanityManager.Instance != null)
        {
            InsanityManager.Instance.OnInsanityChanged -= OnInsanityChanged;
            InsanityManager.Instance.OnStageChanged    -= OnStageChanged;
            InsanityManager.Instance.OnBreakEvent      -= OnBreakEvent;
        }

        // Destroy the root-level objects we created (they won't auto-destroy with the camera
        // since they're no longer parented to it)
        if (insanityVolume != null) Destroy(insanityVolume.gameObject);
        if (whisperSource  != null) Destroy(whisperSource.gameObject);
        if (stingSource    != null) Destroy(stingSource.gameObject);
        if (flashImage     != null) Destroy(flashImage.transform.parent.parent.gameObject); // canvasGO
    }

    private void Update()
    {
        if (insanityVolume == null) return;

        // Get the target values for the current stage
        int s = Mathf.Clamp(currentStage, 0, 3);
        float t = LerpSpeed * Time.deltaTime;

        // Lerp all Volume parameters toward their stage targets
        if (filmGrain != null)
        {
            filmGrain.intensity.value = Mathf.Lerp(filmGrain.intensity.value, GrainIntensityTargets[s], t);
            filmGrain.response.value  = Mathf.Lerp(filmGrain.response.value,  GrainResponseTargets[s],  t);
        }

        if (chromaticAberration != null)
            chromaticAberration.intensity.value = Mathf.Lerp(
                chromaticAberration.intensity.value, ChromaticAberrationTargets[s], t);

        if (colorAdjustments != null)
            colorAdjustments.saturation.value = Mathf.Lerp(
                colorAdjustments.saturation.value, SaturationTargets[s], t);

        if (vignette != null)
        {
            float baseIntensity = VignetteIntensityTargets[s];
            float pulse = 0f;
            if (currentStage >= 2)
            {
                // Stage 3: slow breathe (1 cycle/sec). Stage 4: faster, harder throb (2 cycles/sec)
                float pulseSpeed = currentStage >= 3 ? 2.2f : 1.0f;
                float pulseAmp   = currentStage >= 3 ? 0.14f : 0.07f;
                vignettePhase += Time.deltaTime * pulseSpeed;
                pulse = Mathf.Sin(vignettePhase) * pulseAmp;
            }
            vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, baseIntensity + pulse, t);
        }

        if (lensDistortion != null)
            lensDistortion.intensity.value = Mathf.Lerp(
                lensDistortion.intensity.value, LensDistortionTargets[s], t);

        // Whisper audio volume — fade in at stage 3, full at stage 4
        if (whisperSource != null)
        {
            float targetWhisperVol = currentStage == 2 ? whisperMaxVolume * 0.5f
                                   : currentStage >= 3  ? whisperMaxVolume
                                   : 0f;
            whisperSource.volume = Mathf.Lerp(whisperSource.volume, targetWhisperVol, t);

            // Stage 4: pitch distortion based on insanity
            if (currentStage >= 3)
            {
                float pitchNoise = Mathf.PerlinNoise(Time.time * 0.5f, 0f);
                whisperSource.pitch = Mathf.Lerp(0.85f, 1.15f, pitchNoise);
            }
            else
            {
                whisperSource.pitch = 1f;
            }
        }
    }

    // ── Event Handlers ─────────────────────────────────────────────────────────

    private void OnInsanityChanged(float newInsanity)
    {
        currentInsanity = newInsanity;
    }

    private void OnStageChanged(int newStage)
    {
        currentStage = newStage;

        // Stage 3+: start hallucination coroutine
        if (newStage >= 2 && hallucinationCoroutine == null)
        {
            hallucinationCoroutine = StartCoroutine(HallucinationLoop());
        }
        // Below stage 3: stop hallucinations
        else if (newStage < 2 && hallucinationCoroutine != null)
        {
            StopCoroutine(hallucinationCoroutine);
            hallucinationCoroutine = null;
        }
    }

    private void OnBreakEvent()
    {
        StartCoroutine(BreakEventFlash());
        if (stingSource != null && breakEventStingClip != null)
            stingSource.PlayOneShot(breakEventStingClip);
    }

    // ── Hallucination System ───────────────────────────────────────────────────

    private IEnumerator HallucinationLoop()
    {
        while (true)
        {
            float interval = currentStage >= 3
                ? stage4HallucinationInterval
                : stage3HallucinationInterval;

            // Add some randomness so it doesn't feel mechanical
            yield return new WaitForSeconds(interval * UnityEngine.Random.Range(0.7f, 1.3f));

            SpawnHallucination();
        }
    }

    private void SpawnHallucination()
    {
        if (hallucinationPool.Count == 0 || playerCamera == null) return;

        GameObject h = hallucinationPool.Dequeue();

        // Position it at a random point in the player's peripheral vision —
        // offset from screen centre toward an edge, then convert to world space
        float edgeBias  = UnityEngine.Random.Range(0.65f, 0.95f);
        float angle     = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float screenX   = 0.5f + Mathf.Cos(angle) * edgeBias * 0.45f;
        float screenY   = 0.5f + Mathf.Sin(angle) * edgeBias * 0.35f;

        // Place it at a fixed distance in front of camera
        float depth = UnityEngine.Random.Range(3f, 6f);
        Vector3 screenPos = new Vector3(screenX, screenY, depth);
        Vector3 worldPos  = playerCamera.ViewportToWorldPoint(screenPos);

        h.transform.position = worldPos;
        h.transform.LookAt(playerCamera.transform);
        h.SetActive(true);

        StartCoroutine(ReturnHallucinationAfterDelay(h, hallucinationDuration));
    }

    private IEnumerator ReturnHallucinationAfterDelay(GameObject h, float delay)
    {
        yield return new WaitForSeconds(delay);
        h.SetActive(false);
        hallucinationPool.Enqueue(h);
    }

    // ── Break Event Flash ─────────────────────────────────────────────────────

    private IEnumerator BreakEventFlash()
    {
        if (flashImage == null) yield break;

        flashImage.gameObject.SetActive(true);

        // Flash in
        float elapsed = 0f;
        float halfDuration = flashDuration * 0.5f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / halfDuration);
            flashImage.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        // Flash out
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / halfDuration);
            flashImage.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        flashImage.color = new Color(1f, 1f, 1f, 0f);
        flashImage.gameObject.SetActive(false);
    }

    // ── Builder Helpers ───────────────────────────────────────────────────────

    private void BuildVolume()
    {
        GameObject go = new GameObject("InsanityVolume");
        insanityVolume          = go.AddComponent<Volume>();
        insanityVolume.isGlobal = true;
        // Priority 75: above PowerManager (50) and SirenPhaseManager (60), below GoggleController (100)
        insanityVolume.priority = 75;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // IMPORTANT: profile.Add<T>(true) sets ALL parameter overrideStates to true with
        // default values. Each component below must explicitly disable any parameter it does
        // NOT own, otherwise this priority-75 volume will silently cancel effects from
        // lower-priority volumes (PowerManager p50, SirenPhaseManager p60).

        // FilmGrain — owned exclusively by this volume
        filmGrain = profile.Add<FilmGrain>(true);
        filmGrain.active = true;
        filmGrain.intensity.overrideState = true;
        filmGrain.response.overrideState  = true;
        filmGrain.intensity.value         = 0f;
        filmGrain.response.value          = 0f;

        // ChromaticAberration — owned exclusively by this volume
        chromaticAberration = profile.Add<ChromaticAberration>(true);
        chromaticAberration.active = true;
        chromaticAberration.intensity.overrideState = true;
        chromaticAberration.intensity.value         = 0f;

        // ColorAdjustments — ONLY saturation. Explicitly release all other parameters so
        // PowerManager (postExposure) and SirenPhaseManager (colorFilter) can function.
        colorAdjustments = profile.Add<ColorAdjustments>(true);
        colorAdjustments.active = true;
        colorAdjustments.saturation.overrideState   = true;
        colorAdjustments.saturation.value           = 0f;
        colorAdjustments.colorFilter.overrideState  = false; // owned by SirenPhaseManager
        colorAdjustments.postExposure.overrideState = false; // owned by PowerManager
        colorAdjustments.contrast.overrideState     = false;
        colorAdjustments.hueShift.overrideState     = false;

        // Vignette — owned exclusively by this volume
        vignette = profile.Add<Vignette>(true);
        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.intensity.value         = 0f;
        vignette.color.overrideState     = false; // not ours
        vignette.rounded.overrideState   = false; // not ours

        // LensDistortion — barrel distortion that worsens with insanity
        lensDistortion = profile.Add<LensDistortion>(true);
        lensDistortion.active = true;
        lensDistortion.intensity.overrideState  = true;
        lensDistortion.intensity.value          = 0f;
        lensDistortion.xMultiplier.overrideState = false; // not ours
        lensDistortion.yMultiplier.overrideState = false; // not ours

        insanityVolume.profile = profile;
        Debug.Log("[InsanityVFX] URP Volume built at priority 75.");
    }

    private void BuildAudio()
    {
        // Audio sources are created at scene root (not parented to camera)
        // so SUPERCharacterAIO's GetComponentInChildren scans cannot find them.

        // Whisper looping source
        GameObject whisperGO = new GameObject("InsanityWhisperAudio");
        whisperSource = whisperGO.AddComponent<AudioSource>();
        whisperSource.loop        = true;
        whisperSource.volume      = 0f;
        whisperSource.spatialBlend = 0f; // 2D — insanity is internal, not spatial
        if (whisperClip != null)
        {
            whisperSource.clip = whisperClip;
            whisperSource.Play();
        }

        // Sting one-shot source
        GameObject stingGO = new GameObject("InsanityStingAudio");
        stingSource = stingGO.AddComponent<AudioSource>();
        stingSource.spatialBlend = 0f;
    }

    private void BuildFlashOverlay()
    {
        // Creates a full-screen white Canvas for the Break Event flash.
        // NOT parented to the camera — leaving it as a root scene object prevents
        // SUPERCharacterAIO from finding it via GetComponentInChildren<Canvas>()
        // and incorrectly attaching its own UI elements to it.
        GameObject canvasGO = new GameObject("InsanityFlashCanvas");
        // No SetParent call — stays at scene root
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999; // On top of everything

        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        GameObject imgGO = new GameObject("FlashImage");
        imgGO.transform.SetParent(canvasGO.transform, false);
        flashImage = imgGO.AddComponent<UnityEngine.UI.Image>();
        flashImage.color = new Color(1f, 1f, 1f, 0f);

        // Stretch to fill entire screen
        RectTransform rt = imgGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        imgGO.SetActive(false);
    }

    private void BuildHallucinationPool()
    {
        if (hallucinationPrefab == null)
        {
            Debug.LogWarning("[InsanityVFX] hallucinationPrefab not assigned — hallucinations disabled. " +
                             "Create a simple dark quad prefab and assign it in the Inspector.");
            return;
        }

        for (int i = 0; i < hallucinationPoolSize; i++)
        {
            GameObject h = Instantiate(hallucinationPrefab);
            h.SetActive(false);
            DontDestroyOnLoad(h); // Persist across scene loads if any
            hallucinationPool.Enqueue(h);
        }
    }
}
