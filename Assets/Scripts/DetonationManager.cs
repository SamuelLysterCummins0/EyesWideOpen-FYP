using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;

/// <summary>
/// Manages the detonation sequence on the last level.
///
/// Flow:
///   Button pressed → DetonationCountdown (detonationTime seconds) → PlayerDied()
///
/// During the countdown:
///   • Red lighting identical to the Siren phase (colour filter, vignette, roof emissive)
///   • Countdown UI text: "💥 DETONATION IN Xs"
///   • NPCs are NOT given sirenOverride — they keep normal gaze-freeze behaviour
///
/// On player death OR timer expiry:
///   • Respawn is redirected to the detonation room entrance (via GameManager override)
///   • Sequence fully resets — player must press the button again
///
/// On player reaching level 0 spawn room in time:
///   • GameManager.TriggerVictory() is called (by WinTrigger)
///   • OnVictory() cleans up lighting and UI
///
/// Setup:
///   1. Add this component to the same persistent manager GameObject as SirenPhaseManager.
///   2. Assign countdownText (TMP_Text) and countdownContainer in the Inspector
///      — can share the same UI elements as SirenPhaseManager if desired.
///   3. Assign roofMaterial (same material as SirenPhaseManager / PowerManager).
///   4. Optionally assign detonationAlarmClip for a looping alarm during the countdown.
///   5. Set lastLevelIndex to numberOfLevels - 1 (default 4 for a 5-level dungeon).
/// </summary>
public class DetonationManager : MonoBehaviour
{
    public static DetonationManager Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Timing")]
    [Tooltip("How many seconds the player has to escape from the last level to level 0 after pressing the detonation button.")]
    [SerializeField] private float detonationTime = 120f;

    [Header("Level")]
    [Tooltip("Index of the last/detonation level. Set to numberOfLevels - 1 (3 for a 4-level dungeon).")]
    [SerializeField] private int lastLevelIndex = 3;

    [Header("UI")]
    [Tooltip("TMP_Text used to display the detonation countdown. Can share with SirenPhaseManager.")]
    [SerializeField] private TMP_Text countdownText;
    [Tooltip("Parent GameObject of the countdown UI — toggled on/off during the sequence.")]
    [SerializeField] private GameObject countdownContainer;

    [Header("Atmosphere Volume")]
    [Tooltip("How dark the scene gets during the countdown (mirrors SirenPhaseManager.sirenPostExposure).")]
    [SerializeField] private float detonationPostExposure = -2.5f;
    [Tooltip("Red colour filter tint applied during the countdown.")]
    [SerializeField] private Color detonationColorFilter  = new Color(1f, 0.15f, 0.15f);
    [Tooltip("Vignette intensity during the countdown (0 = off, 1 = full black edges).")]
    [SerializeField] [Range(0f, 1f)] private float detonationVignetteIntensity = 0.45f;
    [Tooltip("Fade duration when restoring normal lighting after the sequence ends.")]
    [SerializeField] private float atmosphereFadeDuration = 1.5f;

    [Header("Roof Emissive")]
    [Tooltip("The shared roof material — same as assigned in SirenPhaseManager / PowerManager.")]
    [SerializeField] private Material roofMaterial;
    [Tooltip("Roof emissive colour during the countdown (HDR red).")]
    [SerializeField] private Color detonationRoofColor = new Color(2f, 0f, 0f);
    [SerializeField] private string emissionPropertyName = "_EmissionColor";
    [SerializeField] private string emissionKeyword      = "_EMISSION";

    [Header("Audio")]
    [Tooltip("Optional looping alarm clip that plays during the countdown.")]
    [SerializeField] private AudioClip detonationAlarmClip;
    [SerializeField] [Range(0f, 1f)] private float alarmVolume = 0.7f;

    // ── State ──────────────────────────────────────────────────────────────────

    /// <summary>True while the detonation countdown is running.</summary>
    public bool IsDetonationActive { get; private set; }

    private Coroutine detonationCoroutine;
    private Coroutine fadeCoroutine;

    // ── Atmosphere Volume ──────────────────────────────────────────────────────

    private Volume           detonationVolume;
    private ColorAdjustments detColorAdj;
    private Vignette         detVignette;

    // ── Roof Emissive ──────────────────────────────────────────────────────────

    private readonly List<Material> roofInstances      = new List<Material>();
    private readonly List<Color>    roofOriginalColors = new List<Color>();

    // ── Audio ──────────────────────────────────────────────────────────────────

    private AudioSource alarmSource;

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        BuildAlarmAudio();
        EnsureVolumeBuilt();

        if (countdownContainer != null)
            countdownContainer.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (detonationVolume != null)
            Destroy(detonationVolume.gameObject);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GazeButtonInteraction when the player presses the detonation button.
    /// Starts the countdown and activates all lighting effects.
    /// </summary>
    public void StartDetonationSequence()
    {
        if (IsDetonationActive)
        {
            Debug.Log("[DetonationManager] StartDetonationSequence ignored — already active.");
            return;
        }

        Debug.Log($"[DetonationManager] Detonation sequence started! Timer: {detonationTime}s");

        IsDetonationActive = true;

        // Tell GameManager to respawn the player at the detonation room entrance on death
        if (GameManager.Instance != null && DetonationRoomSetup.Instance != null)
            GameManager.Instance.SetDetonationRespawnPoint(
                DetonationRoomSetup.Instance.GetEntrancePosition(lastLevelIndex));

        // Activate lighting
        CacheRoofRenderers();
        EnsureVolumeBuilt();
        detonationVolume.gameObject.SetActive(true);
        if (detColorAdj != null) detColorAdj.postExposure.value = detonationPostExposure;
        SetRoofEmissive(detonationRoofColor);

        // Show UI
        if (countdownContainer != null) countdownContainer.SetActive(true);

        // Play alarm
        if (alarmSource != null && detonationAlarmClip != null)
        {
            alarmSource.clip   = detonationAlarmClip;
            alarmSource.loop   = true;
            alarmSource.volume = alarmVolume;
            alarmSource.Play();
        }

        // Start countdown
        if (detonationCoroutine != null) StopCoroutine(detonationCoroutine);
        detonationCoroutine = StartCoroutine(DetonationCountdown());
    }

    /// <summary>
    /// Called by GameManager.Respawn() after the player is teleported back to the
    /// detonation room entrance. Fully resets the sequence so the button must be pressed again.
    /// </summary>
    public void OnRespawn()
    {
        Debug.Log("[DetonationManager] OnRespawn — resetting detonation sequence.");
        ResetSequence();
    }

    /// <summary>
    /// Called by GameManager.TriggerVictory() when the player reaches level 0's spawn room in time.
    /// Cleans up lighting/UI without triggering another death.
    /// </summary>
    public void OnVictory()
    {
        Debug.Log("[DetonationManager] OnVictory — player escaped!");
        ResetSequence();
    }

    // ── Countdown ─────────────────────────────────────────────────────────────

    private IEnumerator DetonationCountdown()
    {
        float elapsed = 0f;

        while (elapsed < detonationTime)
        {
            elapsed += Time.deltaTime;
            float remaining = detonationTime - elapsed;

            if (countdownText != null)
                countdownText.text = $"💥 DETONATION IN {Mathf.CeilToInt(remaining)}s";

            yield return null;
        }

        // Time's up — treat as player death
        Debug.Log("[DetonationManager] Timer expired — detonation! Triggering player death.");

        // NOTE: IsDetonationActive stays TRUE here. It is cleared in OnRespawn(),
        // which GameManager.Respawn() calls after teleporting the player back.
        // This ensures the respawn override is still active when Respawn() reads it.
        detonationCoroutine = null;

        GameManager.Instance?.PlayerDied();
    }

    // ── Reset / Cleanup ────────────────────────────────────────────────────────

    private void ResetSequence()
    {
        IsDetonationActive = false;

        // Stop countdown coroutine
        if (detonationCoroutine != null)
        {
            StopCoroutine(detonationCoroutine);
            detonationCoroutine = null;
        }

        // Stop alarm
        if (alarmSource != null) alarmSource.Stop();

        // Fade lighting back to normal
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeAtmosphereOut(atmosphereFadeDuration));

        // Restore roof
        RestoreRoofEmissive();

        // Update UI
        if (countdownText != null) countdownText.text = string.Empty;
        if (countdownContainer != null) countdownContainer.SetActive(false);

        // Clear the respawn override in GameManager
        GameManager.Instance?.ClearDetonationRespawn();
    }

    // ── Roof Emissive (mirrors SirenPhaseManager exactly) ─────────────────────

    private void CacheRoofRenderers()
    {
        roofInstances.Clear();
        roofOriginalColors.Clear();

        if (roofMaterial == null)
        {
            Debug.LogWarning("[DetonationManager] roofMaterial not assigned — roof will not change colour.");
            return;
        }

        string matName        = roofMaterial.name;
        Renderer[] candidates = FindObjectsOfType<Renderer>();

        foreach (Renderer r in candidates)
        {
            Material[] mats = r.materials;
            bool found = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string instanceName = mats[i].name.Replace(" (Instance)", "");
                if (instanceName != matName) continue;

                if (!string.IsNullOrEmpty(emissionKeyword))
                    mats[i].EnableKeyword(emissionKeyword);

                if (!mats[i].HasProperty(emissionPropertyName)) continue;

                roofInstances.Add(mats[i]);
                roofOriginalColors.Add(mats[i].GetColor(emissionPropertyName));
                found = true;
                break;
            }
            if (found) r.materials = mats;
        }

        Debug.Log($"[DetonationManager] Cached {roofInstances.Count} roof renderer(s).");
    }

    private void SetRoofEmissive(Color color)
    {
        for (int i = 0; i < roofInstances.Count; i++)
            if (roofInstances[i] != null && roofInstances[i].HasProperty(emissionPropertyName))
                roofInstances[i].SetColor(emissionPropertyName, color);
    }

    private void RestoreRoofEmissive()
    {
        for (int i = 0; i < roofInstances.Count; i++)
            if (roofInstances[i] != null && roofInstances[i].HasProperty(emissionPropertyName))
                roofInstances[i].SetColor(emissionPropertyName, roofOriginalColors[i]);
    }

    // ── Atmosphere Volume (mirrors SirenPhaseManager exactly) ─────────────────

    private void EnsureVolumeBuilt()
    {
        if (detonationVolume != null) return;

        GameObject go        = new GameObject("DetonationAtmosphereVolume");
        detonationVolume          = go.AddComponent<Volume>();
        detonationVolume.isGlobal = true;
        detonationVolume.priority = 65; // above SirenPhaseManager (60), below InsanityVFX (75)

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

        detColorAdj = profile.Add<ColorAdjustments>(true);
        detColorAdj.active = true;
        detColorAdj.colorFilter.overrideState  = true;
        detColorAdj.colorFilter.value          = detonationColorFilter;
        detColorAdj.postExposure.overrideState = true;
        detColorAdj.postExposure.value         = 0f;
        detColorAdj.saturation.overrideState   = false;
        detColorAdj.contrast.overrideState     = false;
        detColorAdj.hueShift.overrideState     = false;

        var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
        bloom.active = true;
        bloom.tint.overrideState      = true;
        bloom.tint.value              = detonationColorFilter;
        bloom.intensity.overrideState = false;
        bloom.threshold.overrideState = false;
        bloom.scatter.overrideState   = false;

        detVignette = profile.Add<Vignette>(true);
        detVignette.active = true;
        detVignette.color.overrideState     = true;
        detVignette.color.value             = new Color(0.4f, 0f, 0f);
        detVignette.intensity.overrideState = true;
        detVignette.intensity.value         = detonationVignetteIntensity;
        detVignette.rounded.overrideState   = false;

        detonationVolume.profile = profile;
        go.SetActive(false);
        Debug.Log("[DetonationManager] Atmosphere volume built at priority 65.");
    }

    private IEnumerator FadeAtmosphereOut(float duration)
    {
        if (detonationVolume == null) yield break;
        detonationVolume.gameObject.SetActive(true);

        float elapsed  = 0f;
        float startExp = detColorAdj != null ? detColorAdj.postExposure.value : 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (detColorAdj != null)
                detColorAdj.postExposure.value = Mathf.Lerp(startExp, 0f, elapsed / duration);
            yield return null;
        }

        if (detColorAdj != null) detColorAdj.postExposure.value = 0f;
        detonationVolume.gameObject.SetActive(false);
        fadeCoroutine = null;
    }

    // ── Audio ──────────────────────────────────────────────────────────────────

    private void BuildAlarmAudio()
    {
        GameObject go = new GameObject("DetonationAlarmAudio");
        go.transform.SetParent(transform);
        alarmSource = go.AddComponent<AudioSource>();
        alarmSource.spatialBlend = 0f; // 2D — heard everywhere
        alarmSource.volume       = alarmVolume;
        alarmSource.playOnAwake  = false;
    }
}
