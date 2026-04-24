using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;

public class SirenPhaseManager : MonoBehaviour
{
    public static SirenPhaseManager Instance { get; private set; }

    [Header("Timing — per level (index = level number)")]
    [Tooltip("Duration of the active siren phase (seconds), per level.")]
    [SerializeField] private float[] phaseDurations = { 0f, 25f, 30f, 35f };

    [Header("Warning Phase")]
    [SerializeField] private float warningDuration  = 5f;
    [SerializeField] private float allClearDuration = 2f;

    [Header("NPC Speed Override")]
    [Tooltip("Speed for Weeping Angels during siren phase.")]
    [SerializeField] private float angelSirenSpeed  = 5.5f;

    [Header("Audio")]
    [SerializeField] private AudioClip warningAlarmClip;
    [SerializeField] private AudioClip activeAlarmClip;
    [SerializeField] private AudioClip allClearClip;
    [SerializeField] [Range(0f, 1f)] private float alarmVolume = 0.7f;

    [Header("UI")]
    [Tooltip("TMP_Text used for the ALERT countdown. Assign in Inspector.")]
    [SerializeField] private TMP_Text countdownText;
    [Tooltip("Parent GameObject of the countdown UI — toggled on/off during phases.")]
    [SerializeField] private GameObject countdownContainer;

    [Header("Atmosphere Volume")]
    [Tooltip("How dark the scene gets during the siren phase. 0 = no change, -2 = noticeably darker, -4 = very dark.")]
    [SerializeField] private float sirenPostExposure   = -2.5f;
    [Tooltip("Color filter tint applied to the whole scene during the siren phase.")]
    [SerializeField] private Color sirenColorFilter    = new Color(1f, 0.15f, 0.15f);
    [Tooltip("Vignette intensity during the siren phase (0 = off, 1 = full black edges).")]
    [SerializeField] [Range(0f, 1f)] private float sirenVignetteIntensity = 0.45f;
    [Tooltip("How many seconds the volume takes to fade in at the start of warning / fade out at all clear.")]
    [SerializeField] private float atmosphereFadeDuration = 1.5f;
    [Tooltip("How many full dark→red pulses per second during the active phase.")]
    [SerializeField] private float sirenPulseSpeed = 0.5f;
    [Tooltip("PostExposure at the bright/red peak of the pulse. 0 = full brightness, -1 = slightly dark.")]
    [SerializeField] private float sirenPeakExposure = -0.3f;

    [Header("Roof Emissive")]
    [Tooltip("The shared material used by roof tiles — same material as assigned in PowerManager.")]
    [SerializeField] private Material roofMaterial;
    [Tooltip("Emissive color applied to the roof during the active siren phase (use HDR for brightness).")]
    [SerializeField] private Color sirenRoofColor = new Color(2f, 0f, 0f); // HDR red
    [Tooltip("Shader property name for the emission colour. Check your shader's property list — common names: _EmissionColor, _Emission_Color, _EmissiveColor.")]
    [SerializeField] private string emissionPropertyName = "_EmissionColor";
    [Tooltip("Shader keyword that enables emission. Leave blank if your shader does not require a keyword (some custom shaders don't).")]
    [SerializeField] private string emissionKeyword = "_EMISSION";

    private bool isPhaseActive = false;
    /// <summary>True while the siren Active phase is running.</summary>
    public bool IsPhaseActive => isPhaseActive;

    private int currentLevel = 0;

    // NPC registries
    private readonly List<NPCMovement> registeredAngels   = new List<NPCMovement>();
    private readonly List<PacerNPC>    registeredPacers   = new List<PacerNPC>();
    

    // Original angel speeds (restored after siren)
    private readonly Dictionary<NPCMovement, float> originalAngelSpeeds = new Dictionary<NPCMovement, float>();

    private readonly List<Material> roofInstances     = new List<Material>();
    private readonly List<Color>    roofOriginalColors = new List<Color>();

    private Volume           atmosphereVolume;
    private ColorAdjustments atmColorAdj;
    private Vignette         atmVignette;
    private Coroutine        atmosphereFadeCoroutine;
    private Coroutine        pulseCoroutine;

    private AudioSource alarmSource;

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

        if (InsanityManager.Instance != null)
            InsanityManager.Instance.OnBreakEvent += OnBreakEvent;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (InsanityManager.Instance != null)
            InsanityManager.Instance.OnBreakEvent -= OnBreakEvent;

        if (atmosphereVolume != null)
            Destroy(atmosphereVolume.gameObject);
    }

    public void RegisterAngel(NPCMovement angel)
    {
        if (!registeredAngels.Contains(angel))
        {
            registeredAngels.Add(angel);
            originalAngelSpeeds[angel] = angel.speed;
        }
    }

    public void DeregisterAngel(NPCMovement angel)
    {
        registeredAngels.Remove(angel);
        originalAngelSpeeds.Remove(angel);
    }

    public void RegisterPacer(PacerNPC pacer)
    {
        if (!registeredPacers.Contains(pacer)) registeredPacers.Add(pacer);
    }

    public void DeregisterPacer(PacerNPC pacer) => registeredPacers.Remove(pacer);

    

    public void OnLevelChanged(int level)
    {
        currentLevel = level;

        StopAllCoroutines();
        isPhaseActive = false;
        ResetNPCs();
        RestoreRoofEmissive();
        atmosphereFadeCoroutine = null;
        pulseCoroutine          = null;
        if (atmosphereVolume != null)
        {
            atmColorAdj.postExposure.value = 0f;
            atmosphereVolume.gameObject.SetActive(false);
        }
        if (countdownContainer != null) countdownContainer.SetActive(false);

    }

    public void CancelSiren()
    {
        if (!isPhaseActive) return;

        StopAllCoroutines();
        isPhaseActive           = false;
        atmosphereFadeCoroutine = null;
        pulseCoroutine          = null;

        ResetNPCs();
        RestoreRoofEmissive();

        if (atmosphereVolume != null)
        {
            atmColorAdj.postExposure.value = 0f;
            atmosphereVolume.gameObject.SetActive(false);
        }
        if (countdownContainer != null)
            countdownContainer.SetActive(false);

        if (InsanityManager.Instance != null)
            InsanityManager.Instance.IsSirenActive = false;
    }

    public void ForceTriggerSiren()
    {
        if (isPhaseActive) return;

        StopAllCoroutines();
        isPhaseActive          = false;
        atmosphereFadeCoroutine = null; // StopAllCoroutines killed these — clear stale references
        pulseCoroutine          = null;

        // Reset volume to off so the fade-in starts from zero
        EnsureVolumeBuilt();
        atmColorAdj.postExposure.value = 0f;
        atmosphereVolume.gameObject.SetActive(false);

        float duration = currentLevel < phaseDurations.Length
            ? phaseDurations[currentLevel]
            : phaseDurations[^1];

        // Level 0 has duration 0 — always use at least 25s for the debug trigger
        if (duration <= 0f) duration = 25f;

        StartCoroutine(ForcedSirenSequence(duration));
    }

    private IEnumerator ForcedSirenSequence(float duration)
    {
        yield return StartCoroutine(WarningPhase());
        yield return StartCoroutine(ActivePhase(duration));
        yield return StartCoroutine(AllClearPhase());
    }

    public void TriggerOnCodeCollected(int levelIndex)
    {
        if (isPhaseActive) return;

        // Keep currentLevel in sync — guards against the case where the player reached this
        // level without SetCurrentLevel being called (e.g. via a checkpoint shortcut).
        currentLevel = levelIndex;

        StopAllCoroutines();
        isPhaseActive           = false;
        atmosphereFadeCoroutine = null;
        pulseCoroutine          = null;

        EnsureVolumeBuilt();
        atmColorAdj.postExposure.value = 0f;
        atmosphereVolume.gameObject.SetActive(false);

        float duration = currentLevel < phaseDurations.Length
            ? phaseDurations[currentLevel]
            : phaseDurations[^1];
        if (duration <= 0f) duration = 25f;

        StartCoroutine(CodeCollectedSirenSequence(duration));
    }

    private IEnumerator CodeCollectedSirenSequence(float duration)
    {
        yield return StartCoroutine(WarningPhase());
        yield return StartCoroutine(ActivePhase(duration));
        yield return StartCoroutine(AllClearPhase());
    }

    public void ForceNPCDetection(float radius)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        Vector3 playerPos = player.transform.position;

        foreach (NPCMovement angel in registeredAngels)
        {
            if (angel == null) continue;
            if (Vector3.Distance(angel.transform.position, playerPos) <= radius)
            {
                angel.sirenOverride = true;
                StartCoroutine(ResetAngelAfterDelay(angel, 5f));
            }
        }
    }

    private IEnumerator WarningPhase()
    {
        // Cache roof renderers once at the start of each siren event
        CacheRoofRenderers();

        // Warning phase: dark→red pulse on both atmosphere and roof emissive
        EnsureVolumeBuilt();
        atmosphereVolume.gameObject.SetActive(true);
        if (pulseCoroutine != null) StopCoroutine(pulseCoroutine);
        pulseCoroutine = StartCoroutine(SirenPulseLoop());

        if (alarmSource != null && warningAlarmClip != null)
        {
            alarmSource.clip   = warningAlarmClip;
            alarmSource.loop   = false;
            alarmSource.volume = alarmVolume;
            alarmSource.Play();
        }

        if (countdownContainer != null) countdownContainer.SetActive(true);

        float elapsed = 0f;
        while (elapsed < warningDuration)
        {
            elapsed += Time.deltaTime;
            float remaining = warningDuration - elapsed;

            if (countdownText != null)
                countdownText.text = $"⚠ ALERT IN {Mathf.CeilToInt(remaining)}...";

            yield return null;
        }

        // Stop pulse — lock to solid red going into active phase
        if (pulseCoroutine != null) { StopCoroutine(pulseCoroutine); pulseCoroutine = null; }
        SetRoofEmissive(sirenRoofColor);
        atmColorAdj.postExposure.value = sirenPostExposure;
    }

    private IEnumerator ActivePhase(float duration)
    {
        isPhaseActive = true;

        if (InsanityManager.Instance != null)
            InsanityManager.Instance.IsSirenActive = true;

        // Active phase: solid red — volume and roof stay locked, no pulse
        EnsureVolumeBuilt();
        atmosphereVolume.gameObject.SetActive(true);
        SetRoofEmissive(sirenRoofColor);
        atmColorAdj.postExposure.value = sirenPostExposure;

        // Apply NPC overrides
        ApplySirenOverride(true);

        // Play active alarm loop
        if (alarmSource != null && activeAlarmClip != null)
        {
            alarmSource.clip   = activeAlarmClip;
            alarmSource.loop   = true;
            alarmSource.volume = alarmVolume;
            alarmSource.Play();
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float remaining = duration - elapsed;

            if (countdownText != null)
                countdownText.text = $"🚨 ALERT — {Mathf.CeilToInt(remaining)}s";

            yield return null;
        }
    }

    private IEnumerator AllClearPhase()
    {

        if (alarmSource != null) alarmSource.Stop();

        isPhaseActive = false;
        ApplySirenOverride(false);

        if (InsanityManager.Instance != null)
            InsanityManager.Instance.IsSirenActive = false;

        // Stop pulse and fade atmosphere back out
        if (pulseCoroutine != null) { StopCoroutine(pulseCoroutine); pulseCoroutine = null; }
        if (atmosphereFadeCoroutine != null) StopCoroutine(atmosphereFadeCoroutine);
        atmosphereFadeCoroutine = StartCoroutine(FadeAtmosphereOut(atmosphereFadeDuration));

        // Restore roof to original colour
        RestoreRoofEmissive();

        if (alarmSource != null && allClearClip != null)
            alarmSource.PlayOneShot(allClearClip, alarmVolume);

        if (countdownText != null)
            countdownText.text = "✓ ALL CLEAR";

        yield return new WaitForSeconds(allClearDuration);

        if (countdownContainer != null) countdownContainer.SetActive(false);
    }

    private void ApplySirenOverride(bool active)
    {
        foreach (NPCMovement angel in registeredAngels)
        {
            if (angel == null) continue;
            angel.sirenOverride = active;
            angel.speed = active
                ? angelSirenSpeed
                : (originalAngelSpeeds.TryGetValue(angel, out float s) ? s : angel.speed);
            if (angel.agent != null)
            {
                angel.agent.speed     = angel.speed;
                angel.agent.isStopped = false;
            }
        }

        foreach (PacerNPC pacer in registeredPacers)
            if (pacer != null) pacer.SetSirenActive(active);

        
    }

    private void ResetNPCs() => ApplySirenOverride(false);

    private IEnumerator ResetAngelAfterDelay(NPCMovement angel, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (angel != null && !isPhaseActive)
            angel.sirenOverride = false;
    }

    private void OnBreakEvent() => ForceNPCDetection(12f);

    private void CacheRoofRenderers()
    {
        roofInstances.Clear();
        roofOriginalColors.Clear();

        if (roofMaterial == null)
        {
            Debug.LogWarning("[SirenPhaseManager] roofMaterial not assigned — roof emissive will not change.");
            return;
        }

        if (string.IsNullOrEmpty(emissionPropertyName))
        {
            Debug.LogWarning("[SirenPhaseManager] emissionPropertyName is empty — set it in the Inspector.");
            return;
        }

        string matName = roofMaterial.name;
        Renderer[] candidates = FindObjectsOfType<Renderer>();
        int checkedCount = 0;

        foreach (Renderer r in candidates)
        {
            Material[] mats = r.materials;
            bool found = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                // Strip Unity's " (Instance)" suffix before comparing
                string instanceName = mats[i].name.Replace(" (Instance)", "");
                if (instanceName != matName) continue;

                checkedCount++;

                // Enable emission keyword only if one is specified
                if (!string.IsNullOrEmpty(emissionKeyword))
                    mats[i].EnableKeyword(emissionKeyword);

                // Verify the shader actually has this property
                if (!mats[i].HasProperty(emissionPropertyName))
                {
                    Debug.LogWarning($"[SirenPhaseManager] Material '{mats[i].name}' does not have property '{emissionPropertyName}'. " +
                                     $"Check the shader and update emissionPropertyName in the Inspector.");
                    continue;
                }

                roofInstances.Add(mats[i]);
                roofOriginalColors.Add(mats[i].GetColor(emissionPropertyName));
                found = true;
                break;
            }
            if (found) r.materials = mats;
        }

        if (roofInstances.Count == 0)
            Debug.LogWarning($"[SirenPhaseManager] No roof renderers found matching material '{matName}'. " +
                             $"Checked {checkedCount} candidate(s). Make sure the roofMaterial name matches exactly.");
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

    // Mirrors PowerManager's approach: build once, SetActive to show/hide,
    // lerp postExposure inside a coroutine (same as FadeToNormal).
    private void EnsureVolumeBuilt()
    {
        if (atmosphereVolume != null) return;

        GameObject go = new GameObject("SirenAtmosphereVolume");
        atmosphereVolume          = go.AddComponent<Volume>();
        atmosphereVolume.isGlobal = true;
        atmosphereVolume.priority = 60; // above PowerManager (50), below InsanityVFX (75)

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // ColorAdjustments — only colorFilter and postExposure. Release others.
        atmColorAdj = profile.Add<ColorAdjustments>(true);
        atmColorAdj.active = true;
        atmColorAdj.colorFilter.overrideState  = true;
        atmColorAdj.colorFilter.value          = sirenColorFilter;
        atmColorAdj.postExposure.overrideState = true;
        atmColorAdj.postExposure.value         = 0f; // lerped to sirenPostExposure on activate
        atmColorAdj.saturation.overrideState   = false; // owned by InsanityVFX
        atmColorAdj.contrast.overrideState     = false;
        atmColorAdj.hueShift.overrideState     = false;

        // Bloom tint — fully-qualified namespace avoids conflict with any non-URP Bloom type
        var atmBloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
        atmBloom.active = true;
        atmBloom.tint.overrideState      = true;
        atmBloom.tint.value              = sirenColorFilter; // same red tint as color filter
        atmBloom.intensity.overrideState = false; // don't touch intensity, only tint
        atmBloom.threshold.overrideState = false;
        atmBloom.scatter.overrideState   = false;

        // Vignette — only intensity and color, release others
        atmVignette = profile.Add<Vignette>(true);
        atmVignette.active = true;
        atmVignette.color.overrideState     = true;
        atmVignette.color.value             = new Color(0.4f, 0f, 0f);
        atmVignette.intensity.overrideState = true;
        atmVignette.intensity.value         = sirenVignetteIntensity;
        atmVignette.rounded.overrideState   = false;

        atmosphereVolume.profile = profile;
        go.SetActive(false); // hidden until siren starts — same as PowerManager
    }

    private IEnumerator SirenPulseLoop()
    {
        while (true)
        {
            // t goes 0→1→0 smoothly: 0 = dark phase, 1 = red phase
            float t = (Mathf.Sin(Time.time * sirenPulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;

            // Atmosphere: lerp postExposure between the dark trough and the red peak
            if (atmColorAdj != null)
                atmColorAdj.postExposure.value = Mathf.Lerp(sirenPostExposure, sirenPeakExposure, t);

            // Roof emissive: lerp between black (dark) and sirenRoofColor (red)
            SetRoofEmissive(Color.Lerp(Color.black, sirenRoofColor, t));

            yield return null;
        }
    }

    private IEnumerator FadeAtmosphereIn(float duration)
    {
        EnsureVolumeBuilt();
        atmosphereVolume.gameObject.SetActive(true);

        float elapsed  = 0f;
        float startExp = atmColorAdj.postExposure.value;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            atmColorAdj.postExposure.value = Mathf.Lerp(startExp, sirenPostExposure, elapsed / duration);
            yield return null;
        }
        atmColorAdj.postExposure.value = sirenPostExposure;
        atmosphereFadeCoroutine = null;
    }

    private IEnumerator FadeAtmosphereOut(float duration)
    {
        if (atmosphereVolume == null) yield break;
        atmosphereVolume.gameObject.SetActive(true);

        float elapsed  = 0f;
        float startExp = atmColorAdj.postExposure.value;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            atmColorAdj.postExposure.value = Mathf.Lerp(startExp, 0f, elapsed / duration);
            yield return null;
        }
        atmColorAdj.postExposure.value = 0f;
        atmosphereVolume.gameObject.SetActive(false); // fully off — same as PowerManager
        atmosphereFadeCoroutine = null;
    }

    private void BuildAlarmAudio()
    {
        GameObject go = new GameObject("SirenAlarmAudio");
        go.transform.SetParent(transform);
        alarmSource = go.AddComponent<AudioSource>();
        alarmSource.spatialBlend = 0f;
        alarmSource.volume       = alarmVolume;
    }
}
