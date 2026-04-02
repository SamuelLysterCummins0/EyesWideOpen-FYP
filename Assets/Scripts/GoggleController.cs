using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages goggle night-vision using a high-priority URP Volume override.
/// The Volume desaturates the scene and applies a purple tint + vignette —
/// exactly like night-vision goggles but in purple instead of green.
/// A subtle scanline UI layer is added on top for the tech feel.
/// </summary>
public class GoggleController : MonoBehaviour
{
    public static GoggleController Instance { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    private bool isActive = false;
    public  bool IsActive => isActive;

    private readonly List<BreakableWall> registeredWalls = new List<BreakableWall>();

    // ── Double-blink detection ────────────────────────────────────────────────
    private BlinkDetector blinkDetector;
    private bool  prevBlinking   = false;
    private float lastBlinkTime  = -999f;
    private const float kDoubleBlink = 0.45f;   // max gap (seconds) between two blinks

    // ── Volume effect ─────────────────────────────────────────────────────────
    private Volume            goggleVolume;
    private ColorAdjustments  colorAdjustments;
    private Vignette          vignetteEffect;

    // ── Scanline UI overlay (subtle — Volume handles the main effect) ─────────
    private GameObject        scanlineCanvas;

    // ── Audio ─────────────────────────────────────────────────────────────────
    [Header("Sound")]
    [Tooltip("Sound played when goggles are switched on.")]
    public AudioClip gogglesOnSound;
    [Tooltip("Sound played when goggles are switched off.")]
    public AudioClip gogglesOffSound;
    private AudioSource audioSource;

    // Whether the player has collected the goggles item
    private bool gogglesUnlocked = false;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake  = false;
            audioSource.spatialBlend = 0f; // 2D sound
        }

        blinkDetector = FindObjectOfType<BlinkDetector>();
    }

    private void Update()
    {
        DetectDoubleBlink();
    }

    /// <summary>
    /// Detects two blinks within kDoubleBlink seconds and toggles the goggles.
    /// Uses the same rising-edge pattern as DoorWinkInteraction / BreakableWall.
    /// </summary>
    /// <summary>
    /// Called by GazeItemPickup when the goggles are collected.
    /// Enables double-blink activation from that point on.
    /// </summary>
    public void UnlockGoggles()
    {
        gogglesUnlocked = true;
    }

    private void DetectDoubleBlink()
    {
        if (blinkDetector == null || !gogglesUnlocked) return;

        bool nowBlinking = blinkDetector.IsBlinking;

        if (nowBlinking && !prevBlinking)           // rising edge — blink just started
        {
            float gap = Time.time - lastBlinkTime;

            if (gap <= kDoubleBlink)
            {
                // Second blink arrived within the window → toggle goggles
                UseGoggles();
                lastBlinkTime = -999f;              // reset so a third blink won't re-fire
            }
            else
            {
                // First blink of a potential double — record the time
                lastBlinkTime = Time.time;
            }
        }

        prevBlinking = nowBlinking;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void UseGoggles()
    {
        if (!gogglesUnlocked) return;
        isActive = !isActive;
        ApplyVisionEffect(isActive);
        RefreshAllWallCracks();

        // Play sound
        AudioClip clip = isActive ? gogglesOnSound : gogglesOffSound;
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    public void RegisterWall(BreakableWall wall)
    {
        if (wall != null && !registeredWalls.Contains(wall))
        {
            registeredWalls.Add(wall);
            wall.SetCrackVisible(isActive);
        }
    }

    public void UnregisterWall(BreakableWall wall)
    {
        registeredWalls.Remove(wall);
    }

    // ── Vision effect ─────────────────────────────────────────────────────────

    private void ApplyVisionEffect(bool enable)
    {
        if (goggleVolume == null)
            BuildVolumeEffect();

        goggleVolume.gameObject.SetActive(enable);

        // Scanline overlay: show/hide
        if (scanlineCanvas == null && enable)
            BuildScanlineOverlay();
        if (scanlineCanvas != null)
            scanlineCanvas.SetActive(enable);
    }

    // ── URP Volume ────────────────────────────────────────────────────────────

    private void BuildVolumeEffect()
    {
        GameObject go = new GameObject("GoggleVisionVolume");
        DontDestroyOnLoad(go);

        goggleVolume            = go.AddComponent<Volume>();
        goggleVolume.isGlobal   = true;
        goggleVolume.priority   = 100; // overrides the scene's existing Global Volume

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // Desaturate + purple colour grade
        colorAdjustments = profile.Add<ColorAdjustments>(true);
        colorAdjustments.active = true;
        colorAdjustments.saturation.overrideState   = true;
        colorAdjustments.saturation.value           = -75f;   // mostly greyscale
        colorAdjustments.colorFilter.overrideState  = true;
        colorAdjustments.colorFilter.value          = new Color(0.72f, 0.22f, 1f); // purple tint
        colorAdjustments.contrast.overrideState     = true;
        colorAdjustments.contrast.value             = 22f;    // punchy contrast like NVGs

        // Dark-edged vignette — circular lens feel
        vignetteEffect = profile.Add<Vignette>(true);
        vignetteEffect.active = true;
        vignetteEffect.color.overrideState        = true;
        vignetteEffect.color.value                = new Color(0.1f, 0f, 0.2f);
        vignetteEffect.intensity.overrideState    = true;
        vignetteEffect.intensity.value            = 0.52f;
        vignetteEffect.smoothness.overrideState   = true;
        vignetteEffect.smoothness.value           = 0.35f;
        vignetteEffect.rounded.overrideState      = true;
        vignetteEffect.rounded.value              = true;     // circular, not rectangular

        goggleVolume.profile = profile;
        go.SetActive(false); // starts inactive
    }

    // ── Scanline overlay (thin UI layer on top of Volume) ────────────────────

    private void BuildScanlineOverlay()
    {
        scanlineCanvas = new GameObject("GoggleScanlines");
        DontDestroyOnLoad(scanlineCanvas);

        Canvas canvas = scanlineCanvas.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 98;
        scanlineCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        scanlineCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        GameObject panel = new GameObject("Scanlines");
        panel.transform.SetParent(scanlineCanvas.transform, false);

        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        UnityEngine.UI.RawImage img = panel.AddComponent<UnityEngine.UI.RawImage>();
        img.texture       = BuildScanlineTexture();
        img.uvRect        = new Rect(0f, 0f, 1f, Screen.height / 6f);
        img.color         = new Color(0f, 0f, 0f, 0.18f);
        img.raycastTarget = false;
    }

    private Texture2D BuildScanlineTexture()
    {
        Texture2D tex = new Texture2D(2, 4, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Repeat;
        for (int x = 0; x < 2; x++)
        {
            tex.SetPixel(x, 0, new Color(0f, 0f, 0f, 0.5f));
            tex.SetPixel(x, 1, new Color(0f, 0f, 0f, 0.15f));
            tex.SetPixel(x, 2, Color.clear);
            tex.SetPixel(x, 3, Color.clear);
        }
        tex.Apply();
        return tex;
    }

    // ── Wall crack refresh ─────────────────────────────────────────────────────

    private void RefreshAllWallCracks()
    {
        for (int i = registeredWalls.Count - 1; i >= 0; i--)
        {
            if (registeredWalls[i] == null) { registeredWalls.RemoveAt(i); continue; }
            registeredWalls[i].SetCrackVisible(isActive);
        }
    }
}
