using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Fully procedural in-game pause menu.
/// Attach to any persistent GameObject in your game scene.
///
/// Pause suspends:
///   - Time.timeScale = 0  (physics, Update-based logic)
///   - AudioListener.pause (all AudioSources)
///   - GazeDetector.SetGazeActive(false)
///   - BlinkDetector component disabled
///   - SUPERCharacterAIO player controller disabled
///
/// All systems are restored on Resume with their original state.
/// </summary>
public class InGamePauseMenu : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Optional HUD button that opens the menu. Tab/Escape also works.")]
    [SerializeField] private Button openMenuButton;
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

    [Header("Scene")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    // Auto-found references
    private GazeDetector     _gazeDetector;
    private BlinkDetector    _blinkDetector;
    private GazeCalibration  _gazeCalibration;

    // Pre-pause state — restored on resume
    private bool _gazeWasActive;
    private bool _controllerWasEnabled;

    // Is the menu currently open
    private bool _isPaused;

    // True while a recalibration triggered from the pause menu is running —
    // used so the OnCalibrationComplete handler knows to restore gameplay
    // state rather than leaving the player controller disabled.
    private bool _isRecalibrating;

    // Generated panels
    private Canvas     _overlayCanvas;
    private GameObject _pausePanel;
    private GameObject _settingsPanel;
    private GameObject _controlsPanel;

    // ─── Horror palette (matches main menu) ──────────────────────────────────
    private static readonly Color PanelBg   = new Color(0.05f, 0.05f, 0.05f, 0.97f);
    private static readonly Color Accent     = new Color(0.55f, 0.08f, 0.08f, 1f);
    private static readonly Color AccentDim  = new Color(0.55f, 0.08f, 0.08f, 0.30f);
    private static readonly Color TextCol    = new Color(0.88f, 0.88f, 0.88f, 1f);
    private static readonly Color TextDim    = new Color(0.40f, 0.40f, 0.40f, 1f);
    private static readonly Color BtnNormal  = new Color(0.12f, 0.12f, 0.12f, 1f);
    private static readonly Color BtnHover   = new Color(0.22f, 0.06f, 0.06f, 1f);
    private static readonly Color BtnPress   = new Color(0.40f, 0.08f, 0.08f, 1f);
    private static readonly Color TrackCol   = new Color(0.20f, 0.20f, 0.20f, 1f);
    private static readonly Color SliderFill = new Color(0.55f, 0.08f, 0.08f, 1f);
    private static readonly Color DimBg      = new Color(0f, 0f, 0f, 0.87f);

    private const string PrefMasterVol  = "MasterVolume";
    private const string PrefFullscreen = "Fullscreen";
    private const string PrefMouseSens  = "MouseSensitivity";

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Auto-find gaze systems — they're unique in the scene
        _gazeDetector    = FindObjectOfType<GazeDetector>();
        _blinkDetector   = FindObjectOfType<BlinkDetector>();
        _gazeCalibration = FindObjectOfType<GazeCalibration>();

        CreateOverlayCanvas();
        _pausePanel    = BuildPausePanel();
        _settingsPanel = BuildSettingsPanel();
        _controlsPanel = BuildControlsPanel();

        // Apply saved sensitivity to the player controller immediately
        ApplySavedSensitivity();
    }

    private void Start()
    {
        if (openMenuButton != null)
            openMenuButton.onClick.AddListener(TogglePause);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            // Don't open/close the pause menu while the death screen is showing or
            // while the player is inside a locker (Esc exits the locker there instead).
            if (GameManager.Instance != null && GameManager.Instance.IsDeathScreenActive()) return;
            if (LockerInteraction.IsHidingInLocker) return;
            // Don't interrupt an in-progress recalibration.
            if (_isRecalibrating) return;
            TogglePause();
        }
    }

    private void OnDestroy()
    {
        // Safety: always restore timeScale if this object is destroyed while paused
        if (_isPaused)
            ForceResume();
    }

    // ─── Pause / Resume ───────────────────────────────────────────────────────

    public void TogglePause()
    {
        if (_isPaused) Resume();
        else           Pause();
    }

    private void Pause()
    {
        _isPaused = true;

        // ── Freeze time ──
        Time.timeScale = 0f;

        // ── Silence all audio ──
        AudioListener.pause = true;

        // ── Disable gaze tracking ──
        if (_gazeDetector != null)
        {
            _gazeWasActive = _gazeDetector.IsGazeActive;
            _gazeDetector.SetGazeActive(false);
        }

        // ── Disable blink detection ──
        if (_blinkDetector != null)
            _blinkDetector.enabled = false;

        // ── Disable player controller — cache its state so Resume restores correctly ──
        if (GameManager.Instance != null && GameManager.Instance.playerController != null)
        {
            _controllerWasEnabled = GameManager.Instance.playerController.enabled;
            GameManager.Instance.playerController.enabled = false;
        }
        else
        {
            _controllerWasEnabled = false;
        }

        // ── Show cursor ──
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // ── Show pause panel ──
        _settingsPanel.SetActive(false);
        _controlsPanel.SetActive(false);
        _pausePanel.SetActive(true);
    }

    private void Resume()
    {
        _isPaused = false;

        // ── Hide panels ──
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(false);
        _controlsPanel.SetActive(false);

        // ── Restore time ──
        Time.timeScale = 1f;

        // ── Restore audio ──
        AudioListener.pause = false;

        // ── Restore gaze ──
        if (_gazeDetector != null)
            _gazeDetector.SetGazeActive(_gazeWasActive);

        // ── Re-enable blink detection ──
        if (_blinkDetector != null)
            _blinkDetector.enabled = true;

        // ── Re-enable player controller — only if it was enabled before we paused ──
        if (GameManager.Instance != null && GameManager.Instance.playerController != null && _controllerWasEnabled)
            GameManager.Instance.playerController.enabled = true;

        // ── Lock cursor ──
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void ForceResume()
    {
        Time.timeScale      = 1f;
        AudioListener.pause = false;
        Cursor.lockState    = CursorLockMode.Locked;
        Cursor.visible      = false;
    }

    // ─── Panel Builders ───────────────────────────────────────────────────────

    private GameObject BuildPausePanel()
    {
        // Card height grew to 686 to accommodate the extra "RESET CURRENT LEVEL"
        // and "REGENERATE DUNGEON" rows (each full-width button is 52 tall +
        // 10 spacing ≈ 62 px per row).
        var panel   = CreateOverlayDimmer("PausePanel");
        var content = CreateCardContent(panel, "PAUSED", 686);

        AddMenuButton(content, "CONTINUE",                  () => Resume());
        AddMenuButton(content, "SETTINGS",                  () => { _pausePanel.SetActive(false); _settingsPanel.SetActive(true); });
        AddMenuButton(content, "CONTROLS",                  () => { _pausePanel.SetActive(false); _controlsPanel.SetActive(true); });
        AddMenuButton(content, "RECALIBRATE HEAD TRACKING", OnRecalibrateClicked);
        AddMenuButton(content, "SAVE GAME",                 OnSaveClicked);
        AddMenuButton(content, "RESET CURRENT LEVEL",       OnResetLevelClicked);
        AddMenuButton(content, "REGENERATE DUNGEON",        OnRegenerateDungeonClicked);
        AddMenuButton(content, "EXIT TO MENU",              OnExitToMenuClicked);

        panel.SetActive(false);
        return panel;
    }

    private GameObject BuildSettingsPanel()
    {
        var panel   = CreateOverlayDimmer("SettingsPanel");
        var content = CreateCardContent(panel, "SETTINGS", 680);

        AddSectionLabel(content, "AUDIO");
        AddSliderRow(content, "Master Volume",
            PlayerPrefs.GetFloat(PrefMasterVol, 1f), 0f, 1f, OnMasterVolumeChanged);

        AddSectionLabel(content, "DISPLAY");
        AddToggleRow(content, "Fullscreen",
            PlayerPrefs.GetInt(PrefFullscreen, 1) == 1, OnFullscreenChanged);

        AddSectionLabel(content, "CONTROLS");
        AddSliderRow(content, "Mouse Sensitivity",
            PlayerPrefs.GetFloat(PrefMouseSens, 8f), 1f, 20f, OnSensitivityChanged);

        AddDivider(content);
        AddActionButton(content, "BACK",
            () => { _settingsPanel.SetActive(false); _pausePanel.SetActive(true); });

        panel.SetActive(false);
        return panel;
    }

    // ─── Button Handlers ──────────────────────────────────────────────────────

    private void OnSaveClicked()
    {
        if (SaveGameManager.Instance == null)
        {
            Debug.LogWarning("[InGamePauseMenu] SaveGameManager not found — cannot save.");
            return;
        }

        // SaveCurrentGame reads level + position from GameManager automatically.
        // Defaults to slot 0 if no slot was chosen from the menu.
        SaveGameManager.Instance.SaveCurrentGame();
        int slot  = SaveGameManager.Instance.GetActiveSlot();
        int level = GameManager.Instance != null ? GameManager.Instance.GetCurrentLevel() : 0;
        Debug.Log($"[InGamePauseMenu] Game saved — Level {level + 1}, Slot {slot + 1}");
    }

    private void OnExitToMenuClicked()
    {
        ForceResume();
        SceneManager.LoadScene(mainMenuScene);
    }

    // Soft-resets the current level's state (NPCs, code numbers, computer terminal,
    // siren phase, flashlight, stamina, player position). Useful when a level has
    // bugged out — missing number, stuck NPC, softlocked siren, etc.
    //
    // We call Resume() FIRST so that timeScale, audio, cursor, and the player
    // controller are all restored to their normal runtime states. Calling
    // GameManager.ResetCurrentLevel() while Time.timeScale == 0 would cause the
    // teleport + NPC respawn operations to run in a frozen world.
    private void OnResetLevelClicked()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[InGamePauseMenu] GameManager.Instance is null — cannot reset level.");
            return;
        }

        Resume();
        GameManager.Instance.ResetCurrentLevel();
    }

    // Full dungeon regeneration — tears down every level and re-runs the procedural
    // generator from scratch. Use this when a level has GEOMETRY bugs that state
    // reset cannot fix: computer room never spawned, safe room door opening wrong,
    // corner tiles destroyed at runtime, etc. The generator rolls a fresh RNG seed
    // on re-run, so the new layout typically resolves the bug. Player's current
    // level index is preserved — they are teleported into the new dungeon at the
    // matching level's spawn room rather than sent back to Level 0.
    //
    // As with OnResetLevelClicked, we call Resume() first so the generator's
    // coroutines run in an un-frozen world (Time.timeScale = 1 is required for
    // yield return to advance).
    private void OnRegenerateDungeonClicked()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[InGamePauseMenu] GameManager.Instance is null — cannot regenerate dungeon.");
            return;
        }

        Resume();
        GameManager.Instance.RegenerateDungeon();
    }

    // Re-runs the 9-point gaze calibration. Calibration requires real time
    // (Time.timeScale > 0) and keyboard input, so we unpause everything except
    // the player controller — the player shouldn't move during recalibration.
    private void OnRecalibrateClicked()
    {
        if (_gazeCalibration == null)
        {
            Debug.LogWarning("[InGamePauseMenu] No GazeCalibration in scene — cannot recalibrate.");
            return;
        }

        _isRecalibrating = true;
        _isPaused        = false;

        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(false);

        Time.timeScale      = 1f;
        AudioListener.pause = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Subscribe a one-shot listener that restores gameplay when calibration ends.
        UnityEngine.Events.UnityAction onComplete = null;
        onComplete = () =>
        {
            _gazeCalibration.OnCalibrationComplete.RemoveListener(onComplete);
            FinishRecalibration();
        };
        _gazeCalibration.OnCalibrationComplete.AddListener(onComplete);

        _gazeCalibration.StartCalibration();
    }

    private void FinishRecalibration()
    {
        _isRecalibrating = false;

        // Re-enable the player controller if it was active before pause.
        if (GameManager.Instance != null && GameManager.Instance.playerController != null && _controllerWasEnabled)
            GameManager.Instance.playerController.enabled = true;

        if (_blinkDetector != null)
            _blinkDetector.enabled = true;

        if (_gazeDetector != null)
            _gazeDetector.SetGazeActive(_gazeWasActive);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    // ─── Settings Handlers ────────────────────────────────────────────────────

    private void OnMasterVolumeChanged(float val)
    {
        AudioListener.volume = val;
        PlayerPrefs.SetFloat(PrefMasterVol, val);
    }

    private void OnFullscreenChanged(bool val)
    {
        Screen.fullScreen = val;
        PlayerPrefs.SetInt(PrefFullscreen, val ? 1 : 0);
    }

    private void OnSensitivityChanged(float val)
    {
        PlayerPrefs.SetFloat(PrefMouseSens, val);
        ApplySavedSensitivity(val);
    }

    private void ApplySavedSensitivity(float? overrideVal = null)
    {
        float val = overrideVal ?? PlayerPrefs.GetFloat(PrefMouseSens, 8f);
        if (GameManager.Instance?.playerController != null)
            GameManager.Instance.playerController.Sensitivity = val;
    }

    // ─── Canvas Setup ─────────────────────────────────────────────────────────

    private void CreateOverlayCanvas()
    {
        var go = new GameObject("PauseMenuCanvas");
        _overlayCanvas = go.AddComponent<Canvas>();
        _overlayCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 50;   // below death screen (100), above HUD

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        go.AddComponent<GraphicRaycaster>();
    }

    // ─── UI Factory ───────────────────────────────────────────────────────────

    private GameObject CreateOverlayDimmer(string name)
    {
        var go   = new GameObject(name);
        go.transform.SetParent(_overlayCanvas.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = DimBg;
        return go;
    }

    private GameObject CreateCardContent(GameObject parent, string title, float cardHeight)
    {
        var card     = new GameObject("Card");
        card.transform.SetParent(parent.transform, false);
        var cardRect = card.AddComponent<RectTransform>();
        cardRect.anchorMin        = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax        = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta        = new Vector2(560, cardHeight);
        cardRect.anchoredPosition = Vector2.zero;
        card.AddComponent<Image>().color = PanelBg;

        // Top accent bar
        var bar     = new GameObject("AccentBar");
        bar.transform.SetParent(card.transform, false);
        var barRect = bar.AddComponent<RectTransform>();
        barRect.anchorMin        = new Vector2(0, 1);
        barRect.anchorMax        = new Vector2(1, 1);
        barRect.sizeDelta        = new Vector2(0, 4);
        barRect.anchoredPosition = new Vector2(0, -2);
        bar.AddComponent<Image>().color = Accent;

        // Title
        var titleGo   = new GameObject("Title");
        titleGo.transform.SetParent(card.transform, false);
        var titleRect = titleGo.AddComponent<RectTransform>();
        titleRect.anchorMin        = new Vector2(0, 1);
        titleRect.anchorMax        = new Vector2(1, 1);
        titleRect.sizeDelta        = new Vector2(-40, 56);
        titleRect.anchoredPosition = new Vector2(0, -38);
        var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        titleTmp.text      = title;
        titleTmp.fontSize  = 24;
        titleTmp.color     = Accent;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;

        // Content area
        var content     = new GameObject("Content");
        content.transform.SetParent(card.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(28, 20);
        contentRect.offsetMax = new Vector2(-28, -76);

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing               = 10;
        vlg.childAlignment        = TextAnchor.UpperCenter;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(0, 0, 6, 6);

        return content;
    }

    // Large full-width button used on the main pause panel
    private void AddMenuButton(GameObject parent, string text,
        UnityEngine.Events.UnityAction onClick)
    {
        var go  = new GameObject($"Btn_{text}");
        go.transform.SetParent(parent.transform, false);
        var le  = go.AddComponent<LayoutElement>();
        le.minHeight       = 52;
        le.preferredHeight = 52;

        var img  = go.AddComponent<Image>();
        img.color = BtnNormal;

        var btn  = go.AddComponent<Button>();
        var cs   = btn.colors;
        cs.normalColor      = BtnNormal;
        cs.highlightedColor = BtnHover;
        cs.pressedColor     = BtnPress;
        cs.selectedColor    = BtnHover;
        btn.colors          = cs;
        btn.targetGraphic   = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        var textGo   = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp     = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = 17;
        tmp.color     = TextCol;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    // Accent-coloured action button (e.g. Back / Close)
    private void AddActionButton(GameObject parent, string text,
        UnityEngine.Events.UnityAction onClick)
    {
        var spacer   = new GameObject("Spacer");
        spacer.transform.SetParent(parent.transform, false);
        spacer.AddComponent<LayoutElement>().preferredHeight = 8;

        var go  = new GameObject($"Btn_{text}");
        go.transform.SetParent(parent.transform, false);
        var le  = go.AddComponent<LayoutElement>();
        le.minHeight       = 48;
        le.preferredHeight = 48;

        var img  = go.AddComponent<Image>();
        img.color = Accent;

        var btn  = go.AddComponent<Button>();
        var cs   = btn.colors;
        cs.normalColor      = Accent;
        cs.highlightedColor = new Color(Accent.r + 0.12f, Accent.g + 0.05f, Accent.b + 0.05f, 1f);
        cs.pressedColor     = new Color(Accent.r - 0.10f, Accent.g, Accent.b, 1f);
        cs.selectedColor    = cs.highlightedColor;
        btn.colors          = cs;
        btn.targetGraphic   = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        var textGo   = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp     = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = 16;
        tmp.color     = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    private void AddSectionLabel(GameObject parent, string text)
    {
        var go  = new GameObject($"Label_{text}");
        go.transform.SetParent(parent.transform, false);
        var le  = go.AddComponent<LayoutElement>();
        le.minHeight       = 28;
        le.preferredHeight = 28;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = 12;
        tmp.color     = new Color(Accent.r + 0.1f, Accent.g + 0.08f, Accent.b + 0.08f, 0.9f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Left;
    }

    private void AddSliderRow(GameObject parent, string labelText, float initVal,
        float min, float max, UnityEngine.Events.UnityAction<float> onChange)
    {
        var row = new GameObject($"Row_{labelText}");
        row.transform.SetParent(parent.transform, false);
        var le  = row.AddComponent<LayoutElement>();
        le.minHeight       = 44;
        le.preferredHeight = 44;

        var hg = row.AddComponent<HorizontalLayoutGroup>();
        hg.spacing               = 14;
        hg.childAlignment        = TextAnchor.MiddleLeft;
        hg.childControlHeight    = true;
        hg.childForceExpandHeight = false;

        var lbl    = new GameObject("Label");
        lbl.transform.SetParent(row.transform, false);
        var lblLE  = lbl.AddComponent<LayoutElement>();
        lblLE.minWidth       = 190;
        lblLE.preferredWidth = 190;
        lblLE.flexibleWidth  = 0;
        lbl.AddComponent<RectTransform>();
        var lblTmp = lbl.AddComponent<TextMeshProUGUI>();
        lblTmp.text      = labelText;
        lblTmp.fontSize  = 15;
        lblTmp.color     = TextCol;
        lblTmp.alignment = TextAlignmentOptions.MidlineLeft;

        var sliderGo = BuildSlider(row, initVal, min, max, onChange);
        var sliderLE = sliderGo.AddComponent<LayoutElement>();
        sliderLE.flexibleWidth   = 1;
        sliderLE.minHeight       = 30;
        sliderLE.preferredHeight = 30;
    }

    private GameObject BuildSlider(GameObject parent, float initVal, float min, float max,
        UnityEngine.Events.UnityAction<float> onChange)
    {
        var go     = new GameObject("Slider");
        go.transform.SetParent(parent.transform, false);
        var slider = go.AddComponent<Slider>();
        slider.minValue    = min;
        slider.maxValue    = max;
        slider.value       = initVal;
        slider.wholeNumbers = false;

        var bg     = new GameObject("Background");
        bg.transform.SetParent(go.transform, false);
        var bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.3f);
        bgRect.anchorMax = new Vector2(1, 0.7f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = TrackCol;

        var fillArea     = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        var fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.3f);
        fillAreaRect.anchorMax = new Vector2(1, 0.7f);
        fillAreaRect.offsetMin = new Vector2(5, 0);
        fillAreaRect.offsetMax = new Vector2(-5, 0);

        var fill     = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fill.AddComponent<Image>().color = SliderFill;

        var handleArea     = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(go.transform, false);
        var handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(10, 0);
        handleAreaRect.offsetMax = new Vector2(-10, 0);

        var handle     = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        var handleRect = handle.AddComponent<RectTransform>();
        handleRect.anchorMin        = new Vector2(0.5f, 0.5f);
        handleRect.anchorMax        = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta        = new Vector2(16, 16);
        var handleImg  = handle.AddComponent<Image>();
        handleImg.color = Color.white;

        slider.fillRect      = fill.GetComponent<RectTransform>();
        slider.handleRect    = handleRect;
        slider.targetGraphic = handleImg;

        if (onChange != null)
            slider.onValueChanged.AddListener(onChange);

        return go;
    }

    private void AddToggleRow(GameObject parent, string labelText, bool initVal,
        UnityEngine.Events.UnityAction<bool> onChange)
    {
        var row = new GameObject($"Row_{labelText}");
        row.transform.SetParent(parent.transform, false);
        var le  = row.AddComponent<LayoutElement>();
        le.minHeight       = 44;
        le.preferredHeight = 44;

        var hg = row.AddComponent<HorizontalLayoutGroup>();
        hg.spacing               = 14;
        hg.childAlignment        = TextAnchor.MiddleLeft;
        hg.childControlHeight    = true;
        hg.childForceExpandHeight = false;

        var lbl    = new GameObject("Label");
        lbl.transform.SetParent(row.transform, false);
        var lblLE  = lbl.AddComponent<LayoutElement>();
        lblLE.minWidth       = 190;
        lblLE.preferredWidth = 190;
        lblLE.flexibleWidth  = 0;
        lbl.AddComponent<RectTransform>();
        var lblTmp = lbl.AddComponent<TextMeshProUGUI>();
        lblTmp.text      = labelText;
        lblTmp.fontSize  = 15;
        lblTmp.color     = TextCol;
        lblTmp.alignment = TextAlignmentOptions.MidlineLeft;

        var toggleGo = new GameObject("Toggle");
        toggleGo.transform.SetParent(row.transform, false);
        var toggleLE = toggleGo.AddComponent<LayoutElement>();
        toggleLE.minWidth        = 40;
        toggleLE.preferredWidth  = 40;
        toggleLE.minHeight       = 40;
        toggleLE.preferredHeight = 40;
        toggleLE.flexibleWidth   = 0;
        toggleGo.AddComponent<RectTransform>();

        var bgGo   = new GameObject("Background");
        bgGo.transform.SetParent(toggleGo.transform, false);
        var bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var bgImg  = bgGo.AddComponent<Image>();
        bgImg.color = TrackCol;

        var checkGo   = new GameObject("Checkmark");
        checkGo.transform.SetParent(bgGo.transform, false);
        var checkRect = checkGo.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.15f, 0.15f);
        checkRect.anchorMax = new Vector2(0.85f, 0.85f);
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;
        var checkImg  = checkGo.AddComponent<Image>();
        checkImg.color = Accent;

        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic       = checkImg;
        toggle.isOn          = initVal;
        if (onChange != null)
            toggle.onValueChanged.AddListener(onChange);
    }

    private void AddDivider(GameObject parent)
    {
        var go  = new GameObject("Divider");
        go.transform.SetParent(parent.transform, false);
        var le  = go.AddComponent<LayoutElement>();
        le.minHeight       = 2;
        le.preferredHeight = 2;
        go.AddComponent<Image>().color = AccentDim;
    }

    // ─── Controls Panel ───────────────────────────────────────────────────────

    private GameObject BuildControlsPanel()
    {
        var panel   = CreateOverlayDimmer("ControlsPanel");
        var content = CreateCardContent(panel, "CONTROLS", 500);

        AddControlRow(content, "ESC",           "Exit Locker/Computer");
        AddControlRow(content, "W / A / S / D", "Move");
        AddControlRow(content, "SHIFT",          "Sprint");
        AddControlRow(content, "CTRL",           "Crouch");
        AddControlRow(content, "F",              "Toggle Flashlight");
        AddControlRow(content, "TAB",            "Camera Settings");
        AddControlRow(content, "BLINK",          "Interact");
        AddControlRow(content, "M",              "Menu");

        AddDivider(content);
        AddActionButton(content, "BACK",
            () => { _controlsPanel.SetActive(false); _pausePanel.SetActive(true); });

        panel.SetActive(false);
        return panel;
    }

    /// <summary>Adds a single key → action row to the controls panel.</summary>
    private void AddControlRow(GameObject parent, string key, string action)
    {
        var row = new GameObject($"Control_{key}");
        row.transform.SetParent(parent.transform, false);
        var le  = row.AddComponent<LayoutElement>();
        le.minHeight       = 38;
        le.preferredHeight = 38;

        var hg = row.AddComponent<HorizontalLayoutGroup>();
        hg.spacing                = 14;
        hg.childAlignment         = TextAnchor.MiddleLeft;
        hg.childControlHeight     = true;
        hg.childForceExpandHeight = false;

        // ── Key badge (dark box, accent text) ──
        var keyGo  = new GameObject("KeyBadge");
        keyGo.transform.SetParent(row.transform, false);
        var keyLE  = keyGo.AddComponent<LayoutElement>();
        keyLE.minWidth       = 120;
        keyLE.preferredWidth = 120;
        keyLE.flexibleWidth  = 0;
        keyGo.AddComponent<Image>().color = BtnNormal;

        var keyTextGo   = new GameObject("KeyText");
        keyTextGo.transform.SetParent(keyGo.transform, false);
        var keyTextRect = keyTextGo.AddComponent<RectTransform>();
        keyTextRect.anchorMin = Vector2.zero;
        keyTextRect.anchorMax = Vector2.one;
        keyTextRect.offsetMin = new Vector2(6, 2);
        keyTextRect.offsetMax = new Vector2(-6, -2);
        var keyTmp  = keyTextGo.AddComponent<TextMeshProUGUI>();
        keyTmp.text      = key;
        keyTmp.fontSize  = 13;
        keyTmp.color     = Accent;
        keyTmp.fontStyle = FontStyles.Bold;
        keyTmp.alignment = TextAlignmentOptions.Center;

        // ── Action description ──
        var actGo  = new GameObject("ActionText");
        actGo.transform.SetParent(row.transform, false);
        var actLE  = actGo.AddComponent<LayoutElement>();
        actLE.flexibleWidth = 1;
        actGo.AddComponent<RectTransform>();
        var actTmp = actGo.AddComponent<TextMeshProUGUI>();
        actTmp.text      = action;
        actTmp.fontSize  = 14;
        actTmp.color     = TextCol;
        actTmp.alignment = TextAlignmentOptions.MidlineLeft;
    }
}
