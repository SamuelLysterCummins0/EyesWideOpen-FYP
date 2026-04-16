using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cassette player world interaction — gaze at it and blink to play a recording.
///
/// Unlike pickups, this object stays in the world. On first blink:
///   1. A screen-static flash fires (configurable).
///   2. The 3-D positional audio plays from the cassette's world position.
///   3. A retro "▶ TAPE PLAYING" indicator appears in the top-left corner.
///   4. Optional subtitle lines appear timed to the recording.
///
/// After the clip ends the object dims and shows a "recording heard" prompt so the
/// player knows they have already interacted with it.
///
/// Setup:
///   1. Add to a cassette player prefab that has a Collider.
///   2. Assign audioClip (the dead scientist recording for this level).
///   3. Optionally fill SubtitleLines with what the scientist says.
///   4. Add the prefab as an ExtraItemEntry in BatterySpawnSetup (mode = AgainstWall).
///      The object does NOT need to be added to any pickup system — it is not collected.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CassettePlayerInteraction : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Audio")]
    [Tooltip("The recording that plays when activated.")]
    [SerializeField] private AudioClip audioClip;

    [Tooltip("Volume of the audio played in the player's ears.")]
    [SerializeField] private float volume = 1f;

    [Tooltip("Allow the player to blink again to replay the recording after it ends.")]
    [SerializeField] private bool canReplay = false;

    [Header("Intro Room Integration")]
    [Tooltip("Enable on the intro room's audiotape only. " +
             "Notifies IntroRoomController when the tape starts playing, " +
             "which unlocks the exit door.")]
    [SerializeField] private bool isIntroAudiotape = false;

    [Header("Prompts")]
    [SerializeField] private string promptUnplayed = "Blink to play recording";
    [SerializeField] private string promptPlaying  = "Playing...";
    [SerializeField] private string promptPlayed   = "Recording already heard";

    [Header("Gaze Settings")]
    [SerializeField] private float rayDistance   = 5f;
    [SerializeField] private float gazeHitRadius = 0.5f;

    [Header("Glow — Unplayed")]
    [SerializeField] private Color glowColor     = new Color(0.55f, 0.85f, 1f);   // cold blue
    [SerializeField] private float glowIntensity = 2.5f;
    [SerializeField] private float pulseSpeed    = 2.5f;

    [Header("Glow — Played")]
    [Tooltip("Dim glow shown after the recording has been heard.")]
    [SerializeField] private Color playedGlowColor = new Color(0.35f, 0.35f, 0.35f);

    [Header("Static Flash Effect")]
    [Tooltip("Briefly fills the screen with static noise when the cassette is activated.")]
    [SerializeField] private bool  useStaticEffect   = true;
    [Tooltip("Total duration of the static flash in seconds.")]
    [SerializeField] private float staticDuration    = 0.55f;
    [Tooltip("Peak screen opacity of the static overlay (0 = off, 1 = fully opaque).")]
    [SerializeField] [Range(0f, 1f)] private float staticPeakAlpha = 0.65f;

    [Header("Subtitles")]
    [Tooltip("Lines of text shown on screen while the recording plays. Leave empty to disable.")]
    [SerializeField] private string[] subtitleTexts;
    [Tooltip("Duration in seconds for each subtitle line. Must match the length of Subtitle Texts.\nDefaults to 3s for any line that has no matching entry here.")]
    [SerializeField] private float[]  subtitleDurations;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private GazeDetector  gazeDetector;
    private BlinkDetector blinkDetector;
    private Camera        playerCamera;

    private Renderer[] itemRenderers;
    private Color[]    originalEmission;
    private Color[]    originalBaseColor;
    private bool[]     supportsEmission;
    private bool[]     usesBaseColor;

    private enum CassetteState { Unplayed, Playing, Played }
    private CassetteState state = CassetteState.Unplayed;

    private bool wasBlinking = false;

    // UI elements (built procedurally — no scene canvas required)
    private Text  uiPrompt;
    private Text  nowPlayingText;
    private Text  subtitleText;
    private Image staticOverlay;

    // Canvas roots so OnDestroy can clean them up
    private GameObject mainCanvasRoot;
    private GameObject staticCanvasRoot;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        gazeDetector  = FindObjectOfType<GazeDetector>();
        blinkDetector = FindObjectOfType<BlinkDetector>();
        playerCamera  = Camera.main;

        CacheMaterials();
        BuildUI();
    }

    private void Update()
    {
        bool gazed = IsGazedAt();

        switch (state)
        {
            case CassetteState.Unplayed:
                ShowPrompt(gazed ? promptUnplayed : null);
                if (gazed)
                {
                    PulseGlow(glowColor, glowIntensity);
                    HandleBlinkTrigger();
                }
                else
                {
                    ResetGlow();
                    wasBlinking = GetIsBlinking();
                }
                break;

            case CassetteState.Playing:
                // Object is invisible and will destroy itself when audio ends — nothing to update.
                break;

            case CassetteState.Played:
                // Reached only if Destroy hasn't fired yet — nothing to display.
                break;
        }
    }

    private void OnDestroy()
    {
        if (mainCanvasRoot != null)   Destroy(mainCanvasRoot);
        if (staticCanvasRoot != null) Destroy(staticCanvasRoot);
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    private void HandleBlinkTrigger()
    {
        bool blinkingNow = GetIsBlinking();
        if (blinkingNow && !wasBlinking)
            StartPlayback();
        wasBlinking = blinkingNow;
    }

    private void StartPlayback()
    {
        if (state == CassetteState.Playing) return;
        state = CassetteState.Playing;

        // Unlock the intro room exit door as soon as the player engages with the tape.
        if (isIntroAudiotape)
            IntroRoomController.Instance?.OnAudiotapePickedUp();

        // Hide the object immediately — renderers off, collider off.
        // The AudioSource and coroutines keep running on the invisible GameObject
        // until the clip ends, then the whole object is destroyed.
        foreach (Renderer r in itemRenderers)
            r.enabled = false;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (audioClip != null)
        {
            // Spawn a temporary 2D audio object so the clip plays in the player's ears
            // regardless of where the cassette was in the world. It self-destructs
            // after the clip finishes.
            GameObject audioObj = new GameObject("CassetteAudio_Temp");
            AudioSource tempSource        = audioObj.AddComponent<AudioSource>();
            tempSource.clip               = audioClip;
            tempSource.spatialBlend       = 0f;   // 2D — plays directly in headphones
            tempSource.volume             = volume;
            tempSource.playOnAwake        = false;
            tempSource.loop               = false;
            tempSource.Play();
            Destroy(audioObj, audioClip.length + 0.1f);
        }

        // Hide the interaction prompt immediately — object is now invisible
        ShowPrompt(null);

        if (useStaticEffect)
            StartCoroutine(StaticFlashCoroutine());

        if (subtitleTexts != null && subtitleTexts.Length > 0)
            StartCoroutine(SubtitleCoroutine());

        StartCoroutine(WaitForPlaybackEnd());

        Debug.Log($"[CassettePlayerInteraction] Started playback on '{name}'.");
    }

    private IEnumerator WaitForPlaybackEnd()
    {
        float duration = (audioClip != null) ? audioClip.length : 0.5f;
        yield return new WaitForSeconds(duration);
        OnPlaybackFinished();
    }

    private void OnPlaybackFinished()
    {
        if (state != CassetteState.Playing) return;
        state = CassetteState.Played;

        HideNowPlaying();
        HideSubtitle();

        Debug.Log($"[CassettePlayerInteraction] Playback finished on '{name}'. Destroying.");
        Destroy(gameObject);
    }

    // ── Screen static flash ───────────────────────────────────────────────────

    private IEnumerator StaticFlashCoroutine()
    {
        if (staticOverlay == null) yield break;

        float fadeInTime  = staticDuration * 0.25f;
        float holdTime    = staticDuration * 0.15f;
        float fadeOutTime = staticDuration * 0.60f;

        // Fade in
        for (float t = 0f; t < fadeInTime; t += Time.deltaTime)
        {
            SetOverlayAlpha(Mathf.Lerp(0f, staticPeakAlpha, t / fadeInTime));
            yield return null;
        }

        SetOverlayAlpha(staticPeakAlpha);
        yield return new WaitForSeconds(holdTime);

        // Show "now playing" at peak opacity so it punches through
        ShowNowPlaying();

        // Fade out
        for (float t = 0f; t < fadeOutTime; t += Time.deltaTime)
        {
            SetOverlayAlpha(Mathf.Lerp(staticPeakAlpha, 0f, t / fadeOutTime));
            yield return null;
        }

        SetOverlayAlpha(0f);
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (staticOverlay == null) return;
        Color c = staticOverlay.color;
        c.a = alpha;
        staticOverlay.color = c;
    }

    // ── Subtitles ─────────────────────────────────────────────────────────────

    private IEnumerator SubtitleCoroutine()
    {
        if (subtitleText == null) yield break;

        for (int i = 0; i < subtitleTexts.Length; i++)
        {
            subtitleText.text = $"\"{subtitleTexts[i]}\"";
            subtitleText.gameObject.SetActive(true);
            float dur = (subtitleDurations != null && i < subtitleDurations.Length)
                ? subtitleDurations[i]
                : 3f;
            yield return new WaitForSeconds(dur);
        }

        HideSubtitle();
    }

    private void HideSubtitle()
    {
        if (subtitleText != null) subtitleText.gameObject.SetActive(false);
    }

    private void ShowNowPlaying()
    {
        if (nowPlayingText != null) nowPlayingText.gameObject.SetActive(true);
    }

    private void HideNowPlaying()
    {
        if (nowPlayingText != null) nowPlayingText.gameObject.SetActive(false);
    }

    // ── Gaze detection ────────────────────────────────────────────────────────

    private bool IsGazedAt()
    {
        if (gazeDetector == null || !gazeDetector.IsTracking || playerCamera == null)
            return false;

        Ray ray = gazeDetector.GetGazeRay(playerCamera);

        // Primary: direct raycast
        if (Physics.Raycast(ray, out RaycastHit directHit, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (directHit.collider.transform == transform ||
                directHit.collider.transform.IsChildOf(transform))
                return true;
        }

        // Secondary: sphere cast for looser detection
        RaycastHit[] hits = Physics.SphereCastAll(ray, gazeHitRadius, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform == transform ||
                h.collider.transform.IsChildOf(transform))
                return true;
        }

        // Fallback: distance from gaze ray to object centre
        Vector3 toItem = transform.position - ray.origin;
        float   along  = Vector3.Dot(ray.direction, toItem);
        if (along > 0f && along <= rayDistance)
        {
            if (Vector3.Distance(ray.origin + ray.direction * along, transform.position)
                <= gazeHitRadius * 2f)
                return true;
        }

        return false;
    }

    // ── Glow ─────────────────────────────────────────────────────────────────

    private void PulseGlow(Color color, float intensity)
    {
        float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1f);
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;
            if (supportsEmission[i])
                m.SetColor("_EmissionColor", color * intensity * pulse);
            Color tinted = Color.Lerp(originalBaseColor[i], color, pulse * 0.4f);
            if (usesBaseColor[i])             m.SetColor("_BaseColor", tinted);
            else if (m.HasProperty("_Color")) m.SetColor("_Color",     tinted);
        }
    }

    private void ApplyConstantGlow(Color color, float intensity)
    {
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;
            if (supportsEmission[i])
                m.SetColor("_EmissionColor", color * intensity);
        }
    }

    private void ResetGlow()
    {
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;
            if (supportsEmission[i])          m.SetColor("_EmissionColor", originalEmission[i]);
            if (usesBaseColor[i])             m.SetColor("_BaseColor",     originalBaseColor[i]);
            else if (m.HasProperty("_Color")) m.SetColor("_Color",         originalBaseColor[i]);
        }
    }

    // ── Material caching ──────────────────────────────────────────────────────

    private void CacheMaterials()
    {
        itemRenderers    = GetComponentsInChildren<Renderer>();
        originalEmission = new Color[itemRenderers.Length];
        originalBaseColor= new Color[itemRenderers.Length];
        supportsEmission = new bool [itemRenderers.Length];
        usesBaseColor    = new bool [itemRenderers.Length];

        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material mat = itemRenderers[i].material;  // per-instance copy

            supportsEmission[i] = mat.HasProperty("_EmissionColor");
            if (supportsEmission[i])
            {
                mat.EnableKeyword("_EMISSION");
                originalEmission[i] = mat.GetColor("_EmissionColor");
            }

            if (mat.HasProperty("_BaseColor"))
            {
                usesBaseColor[i]    = true;
                originalBaseColor[i]= mat.GetColor("_BaseColor");
            }
            else if (mat.HasProperty("_Color"))
            {
                usesBaseColor[i]    = false;
                originalBaseColor[i]= mat.GetColor("_Color");
            }
            else
            {
                originalBaseColor[i]= Color.white;
            }
        }
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── Main canvas: prompt + now-playing + subtitles ─────────────────────
        mainCanvasRoot             = new GameObject($"CassetteCanvas_{GetInstanceID()}");
        Canvas mainCanvas          = mainCanvasRoot.AddComponent<Canvas>();
        mainCanvas.renderMode      = RenderMode.ScreenSpaceOverlay;
        mainCanvas.sortingOrder    = 100;
        mainCanvasRoot.AddComponent<CanvasScaler>();
        mainCanvasRoot.AddComponent<GraphicRaycaster>();

        // Interaction prompt (centre-bottom)
        uiPrompt = CreateLabel(mainCanvas.transform, "CassettePrompt",
            fontSize: 26, color: new Color(0.55f, 0.85f, 1f));
        SetAnchors(uiPrompt, anchorX: 0.5f, anchorY: 0.22f, width: 500f, height: 50f);
        uiPrompt.gameObject.SetActive(false);

        // "▶ TAPE PLAYING" — top-left corner, retro look
        nowPlayingText = CreateLabel(mainCanvas.transform, "NowPlayingIndicator",
            fontSize: 20, color: new Color(0.55f, 0.85f, 1f, 0.9f));
        nowPlayingText.text = "\u25B6  TAPE PLAYING";
        RectTransform npRect          = nowPlayingText.GetComponent<RectTransform>();
        npRect.anchorMin               = new Vector2(0.5f, 1f);
        npRect.anchorMax               = new Vector2(0.5f, 1f);
        npRect.pivot                   = new Vector2(0.5f, 1f);
        npRect.sizeDelta               = new Vector2(280f, 40f);
        npRect.anchoredPosition        = new Vector2(0f, -20f);
        nowPlayingText.alignment       = TextAnchor.MiddleCenter;
        nowPlayingText.gameObject.SetActive(false);

        // Subtitle text (centre, above prompt band)
        subtitleText = CreateLabel(mainCanvas.transform, "CassetteSubtitles",
            fontSize: 22, color: new Color(0.9f, 0.9f, 0.9f));
        subtitleText.fontStyle          = FontStyle.Italic;
        subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        subtitleText.verticalOverflow   = VerticalWrapMode.Overflow;
        SetAnchors(subtitleText, anchorX: 0.5f, anchorY: 0.30f, width: 900f, height: 120f);
        subtitleText.gameObject.SetActive(false);

        // ── Static overlay canvas (highest sorting order so it covers everything) ──
        if (useStaticEffect)
        {
            staticCanvasRoot           = new GameObject($"CassetteStaticCanvas_{GetInstanceID()}");
            Canvas oc                  = staticCanvasRoot.AddComponent<Canvas>();
            oc.renderMode              = RenderMode.ScreenSpaceOverlay;
            oc.sortingOrder            = 200;
            staticCanvasRoot.AddComponent<CanvasScaler>();
            staticCanvasRoot.AddComponent<GraphicRaycaster>();

            GameObject overlayGO       = new GameObject("StaticOverlay");
            overlayGO.transform.SetParent(oc.transform, false);
            staticOverlay              = overlayGO.AddComponent<Image>();
            staticOverlay.color        = new Color(0.75f, 0.75f, 0.75f, 0f);
            staticOverlay.raycastTarget= false;

            RectTransform ovr          = staticOverlay.GetComponent<RectTransform>();
            ovr.anchorMin              = Vector2.zero;
            ovr.anchorMax              = Vector2.one;
            ovr.sizeDelta              = Vector2.zero;
            ovr.anchoredPosition       = Vector2.zero;
        }
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private Text CreateLabel(Transform parent, string name, int fontSize, Color color)
    {
        GameObject go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t         = go.AddComponent<Text>();
        t.font         = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize     = fontSize;
        t.color        = color;
        t.alignment    = TextAnchor.MiddleCenter;
        return t;
    }

    private void SetAnchors(Text t, float anchorX, float anchorY, float width, float height)
    {
        RectTransform r   = t.GetComponent<RectTransform>();
        r.anchorMin        = new Vector2(anchorX, anchorY);
        r.anchorMax        = new Vector2(anchorX, anchorY);
        r.sizeDelta        = new Vector2(width, height);
        r.anchoredPosition = Vector2.zero;
    }

    private void ShowPrompt(string message)
    {
        if (uiPrompt == null) return;
        if (message == null) { uiPrompt.gameObject.SetActive(false); return; }
        uiPrompt.text = message;
        uiPrompt.gameObject.SetActive(true);
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private bool GetIsBlinking() => blinkDetector != null && blinkDetector.IsBlinking;
}
