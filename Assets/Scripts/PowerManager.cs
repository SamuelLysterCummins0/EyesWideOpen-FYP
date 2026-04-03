using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages the power-outage state on a specific level.
///
/// When the player enters the outage level, a high-priority URP Volume drops
/// post-exposure to darkPostExposure (-5 EV ≈ 3 % brightness), darkening the
/// entire frame — including bloom — instantly.  When the powerbox is activated,
/// a smooth coroutine fades exposure back to 0 and restores the roof emissive.
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
    [Tooltip("Seconds for the power-restore fade.")]
    [SerializeField] private float restoreDuration = 1.5f;

    [Header("Roof Emissive")]
    [Tooltip("The shared material used by roof tiles. Its _EmissionColor will be scaled on outage and restored on power-on.")]
    [SerializeField] private Material roofMaterial;
    [Tooltip("Emissive multiplier when power is off. 0 = completely off, 0.1 = 10% brightness.")]
    [SerializeField] [Range(0f, 1f)] private float darknessEmissiveMultiplier = 0f;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool isPowerOn = true;
    public  bool IsPowerOn => isPowerOn;

    /// <summary>
    /// True only when the player is currently on the outage level AND power is still off.
    /// Use this for interaction guards — prevents blocking other levels.
    /// </summary>
    public bool IsOutageLevelPoweredOff =>
        !isPowerOn &&
        outageLevel >= 0 &&
        GameManager.Instance != null &&
        GameManager.Instance.GetCurrentLevel() == outageLevel;

    private int        outageLevel  = -1;
    private GameObject outageParent;     // Level_N parent — used to scope renderer search

    // ── Volume ────────────────────────────────────────────────────────────────
    private Volume           darknessVolume;
    private ColorAdjustments colorAdj;

    // ── Emissive ──────────────────────────────────────────────────────────────
    // Per-renderer instance materials + original colors — more reliable than MPB with URP emission
    private readonly List<Material> roofMaterialInstances = new List<Material>();
    private readonly List<Color>    roofOriginalColors    = new List<Color>();

    private Coroutine restoreCoroutine;

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
    /// Called by HiddenRoomSetup during level generation to register which level
    /// has the powerbox / outage.  Must be called before the player enters that level.
    /// </summary>
    public void RegisterOutageLevel(int levelIndex, GameObject levelParent)
    {
        outageLevel  = levelIndex;
        outageParent = levelParent;
        isPowerOn    = false;

        // Instance materials are built lazily in CacheRoofRenderers when the player
        // first enters the level, so nothing to do here yet.
    }

    /// <summary>
    /// Called by GameManager.SetCurrentLevel each time the player changes floors.
    /// Activates darkness instantly if this is the outage level and power is still off.
    /// </summary>
    public void OnEnterLevel(int levelIndex)
    {
        Debug.Log($"[PowerManager] OnEnterLevel({levelIndex}) — outageLevel={outageLevel}, isPowerOn={isPowerOn}, roofMaterial={(roofMaterial != null ? roofMaterial.name : "NULL")}");

        if (levelIndex != outageLevel)
        {
            if (darknessVolume != null)
                darknessVolume.gameObject.SetActive(false);
            return;
        }

        if (isPowerOn) return; // Already restored on a previous visit

        // Apply darkness immediately — the power was always off before the player arrived
        EnsureVolumeBuilt();
        darknessVolume.gameObject.SetActive(true);
        colorAdj.postExposure.value = darkPostExposure;
        // Cache renderers now that the level is fully generated
        CacheRoofRenderers();
        ApplyEmissive(darknessEmissiveMultiplier);

        CodeNumberHUD hud = FindObjectOfType<CodeNumberHUD>(true);
        if (hud != null) hud.ShowPowerMessage();
    }

    /// <summary>
    /// Called by PowerboxInteraction when the player blinks at the powerbox.
    /// Fades the scene back to normal brightness over restoreDuration seconds.
    /// </summary>
    public void TurnOnPower()
    {
        if (isPowerOn) return;
        isPowerOn = true;

        if (restoreCoroutine != null) StopCoroutine(restoreCoroutine);
        restoreCoroutine = StartCoroutine(FadeToNormal());

        // Restore the HUD digit slots and re-sync any already-collected codes
        CodeNumberHUD hud = FindObjectOfType<CodeNumberHUD>(true);
        if (hud != null) hud.RestoreCodeDisplay();

        if (CodeNumberManager.Instance != null && GameManager.Instance != null)
            CodeNumberManager.Instance.ActivateLevel(GameManager.Instance.GetCurrentLevel());
    }

    // ── Fade coroutine ────────────────────────────────────────────────────────

    private IEnumerator FadeToNormal()
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
            ApplyEmissive(Mathf.Lerp(darknessEmissiveMultiplier, 1f, t));
            yield return null;
        }

        colorAdj.postExposure.value = 0f;
        ApplyEmissive(1f);
        darknessVolume.gameObject.SetActive(false);
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    private void EnsureVolumeBuilt()
    {
        if (darknessVolume != null) return;

        GameObject go = new GameObject("DarknessVolume");

        darknessVolume          = go.AddComponent<Volume>();
        darknessVolume.isGlobal = true;
        // Priority 50: sits above the scene's default Global Volume but below
        // GoggleController's night-vision volume (priority 100)
        darknessVolume.priority = 50;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        colorAdj = profile.Add<ColorAdjustments>(true);
        colorAdj.active = true;
        colorAdj.postExposure.overrideState = true;
        colorAdj.postExposure.value         = 0f;

        darknessVolume.profile = profile;
        go.SetActive(false);
    }

    // ── Emissive ──────────────────────────────────────────────────────────────
    // Uses renderer.material (instance materials) so _EmissionColor is set directly
    // on each renderer's own copy — more reliable than MPB with URP emission.

    private void ApplyEmissive(float multiplier)
    {
        for (int i = 0; i < roofMaterialInstances.Count; i++)
        {
            Material m = roofMaterialInstances[i];
            if (m == null) continue;
            m.SetColor("_EmissionColor", roofOriginalColors[i] * multiplier);
        }
    }

    private void CacheRoofRenderers()
    {
        roofMaterialInstances.Clear();
        roofOriginalColors.Clear();

        if (roofMaterial == null)
        {
            Debug.LogWarning("[PowerManager] roofMaterial is not assigned — emissive will not change.");
            return;
        }

        Renderer[] candidates = outageParent != null
            ? outageParent.GetComponentsInChildren<Renderer>()
            : FindObjectsOfType<Renderer>();

        string matName = roofMaterial.name;
        foreach (Renderer r in candidates)
        {
            // renderer.materials returns per-instance copies — we can modify them freely
            Material[] mats = r.materials;
            bool found = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].name.StartsWith(matName))
                {
                    // Ensure emission keyword is on so colour changes are visible
                    mats[i].EnableKeyword("_EMISSION");
                    roofMaterialInstances.Add(mats[i]);
                    roofOriginalColors.Add(mats[i].GetColor("_EmissionColor"));
                    found = true;
                    break;
                }
            }
            // Write back the modified array so Unity registers the instance
            if (found) r.materials = mats;
        }

        Debug.Log($"[PowerManager] CacheRoofRenderers: found {roofMaterialInstances.Count} roof material instances.");
    }
}
