using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages power-outage state per level.
///
/// Multiple levels can each have their own powerbox and outage state.
/// When the player enters an outage level (and power is still off), a high-priority
/// URP Volume drops post-exposure to darkPostExposure (~3% brightness).  When the
/// powerbox on that level is activated, a smooth coroutine fades exposure back to
/// normal and restores the roof emissive.
///
/// Setup:
///   1. Add this component anywhere in the scene (e.g. on the DungeonGenerator GO).
///   2. Assign the roof tile material in the Inspector.
///   3. GameManager.SetCurrentLevel calls OnEnterLevel.
///   4. HiddenRoomSetup calls RegisterOutageLevel during generation.
///   5. PowerboxInteraction calls TurnOnPower on blink.
/// </summary>
public class PowerManager : MonoBehaviour
{
    public static PowerManager Instance { get; private set; }

    [Header("Darkness")]
    [Tooltip("Post-exposure EV when power is off. -5 = ~3% brightness (very dark). Tweak to taste.")]
    [SerializeField] private float darkPostExposure = -5f;
    [Tooltip("Seconds for the lights-out fade when entering an outage level.")]
    [SerializeField] private float outageDuration = 1.5f;
    [Tooltip("Seconds for the power-restore fade.")]
    [SerializeField] private float restoreDuration = 1.5f;

    [Header("Roof Emissive")]
    [Tooltip("The shared material used by roof tiles. Its emission will be scaled on outage and restored on power-on.")]
    [SerializeField] private Material roofMaterial;
    [Tooltip("Emissive multiplier when power is off. 0 = completely off, 0.1 = 10% brightness.")]
    [SerializeField] [Range(0f, 1f)] private float darknessEmissiveMultiplier = 0f;
    [Tooltip("Shader property name for the emission colour.")]
    [SerializeField] private string emissionPropertyName = "_EmissionColor";
    [Tooltip("Shader keyword that enables emission. Leave blank if not required.")]
    [SerializeField] private string emissionKeyword = "_EMISSION";

    // ── Per-level state ───────────────────────────────────────────────────────
    // true  = power restored (or never went out)
    // false = power is off (outage active)
    private readonly Dictionary<int, bool>           levelPowerOn    = new Dictionary<int, bool>();
    private readonly Dictionary<int, GameObject>     levelParents    = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, List<Material>> levelRoofMats   = new Dictionary<int, List<Material>>();
    private readonly Dictionary<int, List<Color>>    levelRoofColors = new Dictionary<int, List<Color>>();

    // Index of the level whose darkness volume is currently shown
    private int activeOutageLevel = -1;

    // ── Computed properties ───────────────────────────────────────────────────

    /// <summary>True if power is on for the player's current level.</summary>
    public bool IsPowerOn
    {
        get
        {
            if (GameManager.Instance == null) return true;
            int level = GameManager.Instance.GetCurrentLevel();
            return !levelPowerOn.ContainsKey(level) || levelPowerOn[level];
        }
    }

    /// <summary>
    /// True only when the player is currently on an outage level AND power is still off.
    /// Use this for interaction guards — prevents blocking other levels.
    /// </summary>
    public bool IsOutageLevelPoweredOff
    {
        get
        {
            if (GameManager.Instance == null) return false;
            int level = GameManager.Instance.GetCurrentLevel();
            return levelPowerOn.ContainsKey(level) && !levelPowerOn[level];
        }
    }

    // ── Volume ────────────────────────────────────────────────────────────────
    private Volume           darknessVolume;
    private ColorAdjustments colorAdj;

    private Coroutine fadeCoroutine;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (darknessVolume != null)
            Destroy(darknessVolume.gameObject);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by HiddenRoomSetup during level generation to register an outage level.
    /// Safe to call multiple times for different levels.
    /// </summary>
    public void RegisterOutageLevel(int levelIndex, GameObject levelParent)
    {
        levelPowerOn[levelIndex]  = false;
        levelParents[levelIndex]  = levelParent;
        Debug.Log($"[PowerManager] RegisterOutageLevel: L{levelIndex} registered for power outage.");
    }

    /// <summary>
    /// Called by GameManager.SetCurrentLevel each time the player changes floors.
    /// Activates or deactivates darkness depending on this level's power state.
    /// </summary>
    public void OnEnterLevel(int levelIndex)
    {
        Debug.Log($"[PowerManager] OnEnterLevel({levelIndex}) — outage levels registered: {levelPowerOn.Count}, roofMaterial={(roofMaterial != null ? roofMaterial.name : "NULL")}");

        // Not an outage level, or power already restored — hide darkness
        if (!levelPowerOn.ContainsKey(levelIndex) || levelPowerOn[levelIndex])
        {
            if (darknessVolume != null)
                darknessVolume.gameObject.SetActive(false);
            return;
        }

        // Entering an outage level while power is still off
        activeOutageLevel = levelIndex;
        EnsureVolumeBuilt();
        CacheRoofRenderers(levelIndex);

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeToDark(levelIndex));

        CodeNumberHUD hud = FindObjectOfType<CodeNumberHUD>(true);
        if (hud != null) hud.ShowPowerMessage();
    }

    /// <summary>
    /// Called by PowerboxInteraction when the player blinks at the powerbox.
    /// Restores power on the player's current level.
    /// </summary>
    public void TurnOnPower()
    {
        int level = GameManager.Instance != null
            ? GameManager.Instance.GetCurrentLevel()
            : activeOutageLevel;

        if (!levelPowerOn.ContainsKey(level) || levelPowerOn[level])
        {
            Debug.LogWarning($"[PowerManager] TurnOnPower: L{level} is not a registered outage level or power is already on.");
            return;
        }

        Debug.Log($"[PowerManager] TurnOnPower: restoring power on L{level}.");
        levelPowerOn[level] = true;
        activeOutageLevel   = level;

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeToNormal(level));

        CodeNumberHUD hud = FindObjectOfType<CodeNumberHUD>(true);
        if (hud != null) hud.RestoreCodeDisplay();

        if (CodeNumberManager.Instance != null && GameManager.Instance != null)
            CodeNumberManager.Instance.ActivateLevel(level);
    }

    // ── Save / Load helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Returns true if power has been restored on this level (outage resolved).
    /// Returns false for non-outage levels (never registered) — handled by the array
    /// default so SaveGameManager records only truly-restored levels.
    /// </summary>
    public bool GetPowerRestored(int levelIndex)
    {
        // Only true when the level IS an outage level AND power was turned back on.
        return levelPowerOn.TryGetValue(levelIndex, out bool on) && on;
    }

    /// <summary>
    /// Silently restores power for a saved outage level without playing the fade animation.
    /// Called by SaveGameManager after dungeon regeneration on load.
    /// </summary>
    public void RestorePowerStateQuiet(int levelIndex)
    {
        if (!levelPowerOn.ContainsKey(levelIndex) || levelPowerOn[levelIndex]) return;

        levelPowerOn[levelIndex] = true;
        Debug.Log($"[PowerManager] Quietly restored power for L{levelIndex}.");

        // Make code numbers on this level interactable (they were blocked while power was off)
        CodeNumberManager.Instance?.ActivateLevel(levelIndex);
    }

    // ── Fade coroutines ───────────────────────────────────────────────────────

    private IEnumerator FadeToDark(int level)
    {
        darknessVolume.gameObject.SetActive(true);

        float elapsed  = 0f;
        float startExp = colorAdj.postExposure.value;

        while (elapsed < outageDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / outageDuration);
            colorAdj.postExposure.value = Mathf.Lerp(startExp, darkPostExposure, t);
            ApplyEmissive(Mathf.Lerp(1f, darknessEmissiveMultiplier, t), level);
            yield return null;
        }

        colorAdj.postExposure.value = darkPostExposure;
        ApplyEmissive(darknessEmissiveMultiplier, level);
    }

    private IEnumerator FadeToNormal(int level)
    {
        EnsureVolumeBuilt();
        darknessVolume.gameObject.SetActive(true);

        float elapsed  = 0f;
        float startExp = colorAdj.postExposure.value;

        while (elapsed < restoreDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / restoreDuration);
            colorAdj.postExposure.value = Mathf.Lerp(startExp, 0f, t);
            ApplyEmissive(Mathf.Lerp(darknessEmissiveMultiplier, 1f, t), level);
            yield return null;
        }

        colorAdj.postExposure.value = 0f;
        ApplyEmissive(1f, level);
        darknessVolume.gameObject.SetActive(false);
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    private void EnsureVolumeBuilt()
    {
        if (darknessVolume != null) return;

        GameObject go = new GameObject("DarknessVolume");

        darknessVolume          = go.AddComponent<Volume>();
        darknessVolume.isGlobal = true;
        // Priority 50: above scene default but below GoggleController (100),
        // SirenPhaseManager (60), and InsanityVFX (75)
        darknessVolume.priority = 50;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        colorAdj = profile.Add<ColorAdjustments>(true);
        colorAdj.active = true;
        colorAdj.postExposure.overrideState = true;
        colorAdj.postExposure.value         = 0f;
        colorAdj.colorFilter.overrideState  = false;
        colorAdj.saturation.overrideState   = false;
        colorAdj.contrast.overrideState     = false;
        colorAdj.hueShift.overrideState     = false;

        darknessVolume.profile = profile;
        go.SetActive(false);
    }

    // ── Emissive ──────────────────────────────────────────────────────────────

    private void ApplyEmissive(float multiplier, int level)
    {
        if (!levelRoofMats.ContainsKey(level)) return;

        List<Material> mats   = levelRoofMats[level];
        List<Color>    colors = levelRoofColors[level];

        for (int i = 0; i < mats.Count; i++)
        {
            Material m = mats[i];
            if (m == null || !m.HasProperty(emissionPropertyName)) continue;
            m.SetColor(emissionPropertyName, colors[i] * multiplier);
        }
    }

    private void CacheRoofRenderers(int level)
    {
        if (!levelRoofMats.ContainsKey(level))
        {
            levelRoofMats[level]   = new List<Material>();
            levelRoofColors[level] = new List<Color>();
        }
        else
        {
            levelRoofMats[level].Clear();
            levelRoofColors[level].Clear();
        }

        if (roofMaterial == null)
        {
            Debug.LogWarning("[PowerManager] roofMaterial is not assigned — emissive will not change.");
            return;
        }

        if (string.IsNullOrEmpty(emissionPropertyName))
        {
            Debug.LogWarning("[PowerManager] emissionPropertyName is empty — set it in the Inspector.");
            return;
        }

        GameObject parent = levelParents.ContainsKey(level) ? levelParents[level] : null;
        Renderer[] candidates = parent != null
            ? parent.GetComponentsInChildren<Renderer>()
            : FindObjectsOfType<Renderer>();

        string         matName     = roofMaterial.name;
        int            checkedCount = 0;
        List<Material> instanceList = levelRoofMats[level];
        List<Color>    colorList    = levelRoofColors[level];

        foreach (Renderer r in candidates)
        {
            Material[] mats = r.materials;
            bool found = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string instanceName = mats[i].name.Replace(" (Instance)", "");
                if (instanceName != matName) continue;

                checkedCount++;

                if (!string.IsNullOrEmpty(emissionKeyword))
                    mats[i].EnableKeyword(emissionKeyword);

                if (!mats[i].HasProperty(emissionPropertyName))
                {
                    Debug.LogWarning($"[PowerManager] Material '{mats[i].name}' on L{level} does not have property '{emissionPropertyName}'.");
                    continue;
                }

                instanceList.Add(mats[i]);
                colorList.Add(mats[i].GetColor(emissionPropertyName));
                found = true;
                break;
            }
            if (found) r.materials = mats;
        }

        if (instanceList.Count == 0)
            Debug.LogWarning($"[PowerManager] No roof renderers found for L{level} matching material '{matName}'. Checked {checkedCount} candidate(s).");
        else
            Debug.Log($"[PowerManager] CacheRoofRenderers L{level}: found {instanceList.Count} roof renderer(s).");
    }
}
