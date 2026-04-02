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
    [Tooltip("The shared material used by roof tiles. Its _EmissionColor will be zeroed on outage and restored on power-on.")]
    [SerializeField] private Material roofMaterial;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool isPowerOn = true;
    public  bool IsPowerOn => isPowerOn;

    private int        outageLevel  = -1;
    private GameObject outageParent;     // Level_N parent — used to scope renderer search

    // ── Volume ────────────────────────────────────────────────────────────────
    private Volume           darknessVolume;
    private ColorAdjustments colorAdj;

    // ── Emissive ──────────────────────────────────────────────────────────────
    private readonly List<Renderer> roofRenderers = new List<Renderer>();
    private Color                   originalEmissive;
    private MaterialPropertyBlock   mpb;

    private Coroutine restoreCoroutine;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        mpb = new MaterialPropertyBlock();

        // Cache original emissive from the material asset (before any runtime changes)
        if (roofMaterial != null && roofMaterial.HasProperty("_EmissionColor"))
            originalEmissive = roofMaterial.GetColor("_EmissionColor");
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

        // Re-cache original emissive in case the material was late-assigned
        if (roofMaterial != null && roofMaterial.HasProperty("_EmissionColor"))
            originalEmissive = roofMaterial.GetColor("_EmissionColor");
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
        ApplyEmissive(Color.black);

        // Cache renderers now that the level is fully generated
        CacheRoofRenderers();
        ApplyEmissive(Color.black);

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
            ApplyEmissive(Color.Lerp(Color.black, originalEmissive, t));
            yield return null;
        }

        colorAdj.postExposure.value = 0f;
        ApplyEmissive(originalEmissive);
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

    // ── Emissive (MaterialPropertyBlock — non-destructive, doesn't modify the asset) ──

    private void ApplyEmissive(Color emissiveColor)
    {
        if (roofMaterial == null) return;

        foreach (Renderer r in roofRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_EmissionColor", emissiveColor);
            r.SetPropertyBlock(mpb);
        }
    }

    private void CacheRoofRenderers()
    {
        roofRenderers.Clear();
        if (roofMaterial == null) return;

        // Search only within the outage level's parent to avoid touching other levels
        Renderer[] candidates = outageParent != null
            ? outageParent.GetComponentsInChildren<Renderer>()
            : FindObjectsOfType<Renderer>();

        foreach (Renderer r in candidates)
        {
            foreach (Material mat in r.sharedMaterials)
            {
                if (mat == roofMaterial) { roofRenderers.Add(r); break; }
            }
        }
    }
}
