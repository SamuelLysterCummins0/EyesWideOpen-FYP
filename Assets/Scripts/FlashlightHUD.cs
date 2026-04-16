using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages flashlight-related HUD elements:
///   - Battery bar at bottom-center (visible once the flashlight is picked up).
///   - Transient center-screen notification messages (pickup hints, battery charged, etc.).
///     Uses WaitForSecondsRealtime so notifications work even when Time.timeScale == 0
///     (e.g. while the goggles InstructionUI panel is open).
///
/// Setup:
///   1. Attach to any persistent scene GameObject (e.g. the Player or an existing Manager).
///   2. No Inspector wiring needed — all UI is built in code.
/// </summary>
public class FlashlightHUD : MonoBehaviour
{
    public static FlashlightHUD Instance { get; private set; }

    // ── Battery Bar ────────────────────────────────────────────────────────────
    private GameObject   batteryBarRoot;
    private Image        batteryBarFill;
    private RectTransform batteryBarFillRect;

    // Width of the fill area at 100% battery (set once in BuildBatteryBar)
    private const float kBatteryMaxFillWidth = 234f; // barRoot(240) - left pad(3) - right pad(3)

    // ── Notification Text ──────────────────────────────────────────────────────
    private Text      notificationText;
    private Coroutine notificationCoroutine;

    // ── Canvas ────────────────────────────────────────────────────────────────
    private Canvas hudCanvas;

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        BuildCanvas();
        BuildBatteryBar();
        BuildNotificationText();

        // Hide battery bar until flashlight is unlocked.
        // If startUnlocked is true on FlashlightController the check runs next frame via Update().
        ShowBatteryBar(false);
    }

    private void Update()
    {
        if (FlashlightController.Instance == null) return;

        // Auto-show bar as soon as the flashlight is unlocked (handles startUnlocked = true too).
        bool shouldShow = FlashlightController.Instance.IsUnlocked;
        if (batteryBarRoot != null && batteryBarRoot.activeSelf != shouldShow)
            batteryBarRoot.SetActive(shouldShow);

        if (shouldShow)
            UpdateBatteryBar(FlashlightController.Instance.Battery);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Show or hide the battery bar manually.</summary>
    public void ShowBatteryBar(bool show)
    {
        if (batteryBarRoot != null)
            batteryBarRoot.SetActive(show);
    }

    /// <summary>Update battery bar size and colour. <paramref name="percent"/> is 0–100.</summary>
    public void UpdateBatteryBar(float percent)
    {
        if (batteryBarFill == null || batteryBarFillRect == null) return;

        float t = Mathf.Clamp01(percent / 100f);

        // Physically resize the fill so the bar shrinks/grows — more readable than colour alone
        batteryBarFillRect.sizeDelta = new Vector2(kBatteryMaxFillWidth * t,
                                                   batteryBarFillRect.sizeDelta.y);

        // Smooth colour gradient: yellow (full) → orange (mid) → red (critical)
        batteryBarFill.color = t > 0.4f
            ? Color.Lerp(new Color(1f, 0.55f, 0f), new Color(1f, 0.93f, 0.15f), (t - 0.4f) / 0.6f)
            : Color.Lerp(Color.red, new Color(1f, 0.55f, 0f), t / 0.4f);
    }

    /// <summary>
    /// Show a message in the center of the screen for <paramref name="duration"/> seconds,
    /// then fade it out over 1 second.  Safe to call while Time.timeScale == 0.
    /// </summary>
    public void ShowNotification(string text, float duration = 3.5f)
    {
        if (notificationText == null) return;

        if (notificationCoroutine != null)
            StopCoroutine(notificationCoroutine);

        notificationText.text  = text;
        notificationText.color = Color.white;
        notificationText.gameObject.SetActive(true);
        notificationCoroutine  = StartCoroutine(FadeOutNotification(duration));
    }

    // ── UI Construction ────────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        GameObject go = new GameObject("FlashlightHUDCanvas");
        DontDestroyOnLoad(go);

        hudCanvas            = go.AddComponent<Canvas>();
        hudCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 101; // renders above other HUD canvases

        CanvasScaler scaler          = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode           = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution   = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight    = 0.5f;

        go.AddComponent<GraphicRaycaster>();
    }

    private void BuildBatteryBar()
    {
        // ── Root container ─────────────────────────────────────────────────────
        batteryBarRoot = new GameObject("FlashlightBatteryBar");
        batteryBarRoot.transform.SetParent(hudCanvas.transform, false);

        RectTransform rootRt    = batteryBarRoot.AddComponent<RectTransform>();
        rootRt.anchorMin        = new Vector2(0.5f, 0f);
        rootRt.anchorMax        = new Vector2(0.5f, 0f);
        rootRt.pivot            = new Vector2(0.5f, 0f);
        rootRt.sizeDelta        = new Vector2(240f, 40f);
        rootRt.anchoredPosition = new Vector2(0f, 28f);

        // ── "FLASHLIGHT" label ─────────────────────────────────────────────────
        GameObject labelObj    = new GameObject("Label");
        labelObj.transform.SetParent(batteryBarRoot.transform, false);
        Text label             = labelObj.AddComponent<Text>();
        label.text             = "FLASHLIGHT";
        label.font             = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize         = 13;
        label.color            = new Color(0.88f, 0.88f, 0.88f, 0.9f);
        label.alignment        = TextAnchor.MiddleCenter;
        RectTransform labelRt  = label.GetComponent<RectTransform>();
        labelRt.anchorMin      = new Vector2(0f, 1f);
        labelRt.anchorMax      = new Vector2(1f, 1f);
        labelRt.pivot          = new Vector2(0.5f, 0f);
        labelRt.sizeDelta      = new Vector2(0f, 16f);
        labelRt.anchoredPosition = new Vector2(0f, 2f);

        // ── Dark background track ──────────────────────────────────────────────
        GameObject bgObj   = new GameObject("Background");
        bgObj.transform.SetParent(batteryBarRoot.transform, false);
        Image bg           = bgObj.AddComponent<Image>();
        bg.color           = new Color(0.08f, 0.08f, 0.08f, 0.8f);
        bg.raycastTarget   = false;
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin     = Vector2.zero;
        bgRt.anchorMax     = Vector2.one;
        bgRt.sizeDelta     = Vector2.zero;
        bgRt.anchoredPosition = Vector2.zero;

        // ── Coloured fill — anchored at the left edge, width scales with battery ─
        // Uses RectTransform width rather than Image.Type.Filled (works without a sprite).
        // anchorMin/Max share the same X so sizeDelta.x is the absolute pixel width.
        // pivot.x = 0 means the bar grows/shrinks from the left.
        GameObject fillObj       = new GameObject("Fill");
        fillObj.transform.SetParent(bgObj.transform, false);
        batteryBarFill           = fillObj.AddComponent<Image>();
        batteryBarFill.color     = new Color(1f, 0.93f, 0.15f);
        batteryBarFill.raycastTarget = false;
        batteryBarFillRect       = batteryBarFill.GetComponent<RectTransform>();
        batteryBarFillRect.anchorMin        = new Vector2(0f, 0f);
        batteryBarFillRect.anchorMax        = new Vector2(0f, 1f); // left-anchored, full height stretch
        batteryBarFillRect.pivot            = new Vector2(0f, 0.5f);
        batteryBarFillRect.anchoredPosition = new Vector2(3f, 0f);  // 3 px from left edge
        batteryBarFillRect.sizeDelta        = new Vector2(kBatteryMaxFillWidth, -6f); // full width, 3px inset top+bottom
    }

    private void BuildNotificationText()
    {
        GameObject go = new GameObject("NotificationText");
        go.transform.SetParent(hudCanvas.transform, false);

        notificationText           = go.AddComponent<Text>();
        notificationText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        notificationText.fontSize  = 30;
        notificationText.color     = Color.white;
        notificationText.alignment = TextAnchor.MiddleCenter;
        notificationText.text      = "";

        // Drop shadow for readability against any background
        Shadow shadow         = go.AddComponent<Shadow>();
        shadow.effectColor    = new Color(0f, 0f, 0f, 0.75f);
        shadow.effectDistance = new Vector2(2f, -2f);

        RectTransform rt    = notificationText.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(800f, 80f);
        rt.anchoredPosition = new Vector2(0f, 50f); // slightly above screen centre

        go.SetActive(false);
    }

    // ── Fade coroutine ─────────────────────────────────────────────────────────

    private IEnumerator FadeOutNotification(float duration)
    {
        const float fadeDuration = 1f;
        float holdTime = Mathf.Max(0f, duration - fadeDuration);

        yield return new WaitForSecondsRealtime(holdTime);

        // Fade alpha from 1 → 0 over fadeDuration seconds (real time, not game time)
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            notificationText.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        notificationText.gameObject.SetActive(false);
        notificationCoroutine = null;
    }
}
