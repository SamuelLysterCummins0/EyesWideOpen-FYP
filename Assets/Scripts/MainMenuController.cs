using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Existing Scene Buttons — drag from Inspector")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button exitButton;

    [Header("Scenes")]
    [SerializeField] private string newGameScene  = "Intro";      // cinematic intro
    [SerializeField] private string loadGameScene = "MainGame";   // skip cinematic on load

    // Generated overlays
    private Canvas     _overlayCanvas;
    private GameObject _settingsPanel;
    private GameObject _loadPanel;        // Load button flow
    private GameObject _startFlowPanel;   // Continue / New Game / Back
    private GameObject _newGameSlotPanel; // New Game slot picker

    // Slot area containers (rebuilt on each open)
    private GameObject _loadSlotsArea;
    private GameObject _newGameSlotsArea;

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
    private static readonly Color ClearCol   = new Color(0.30f, 0.06f, 0.06f, 1f);

    private const string PrefMasterVol  = "MasterVolume";
    private const string PrefFullscreen = "Fullscreen";
    private const string PrefMouseSens  = "MouseSensitivity";

    private void Awake()
    {
        EnsureSaveManager();
        CreateOverlayCanvas();
        _settingsPanel   = BuildSettingsPanel();
        _loadPanel       = BuildSlotPanel("LOAD GAME",  false);
        _startFlowPanel  = BuildStartFlowPanel();
        _newGameSlotPanel = BuildSlotPanel("NEW GAME — Choose Slot", true);
        ApplySavedSettings();
    }

    private void Start()
    {
        WireMainButtons();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void EnsureSaveManager()
    {
        if (SaveGameManager.Instance == null)
            new GameObject("SaveGameManager").AddComponent<SaveGameManager>();
    }

    private void CreateOverlayCanvas()
    {
        var go = new GameObject("MenuOverlayCanvas");
        _overlayCanvas = go.AddComponent<Canvas>();
        _overlayCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 100;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        go.AddComponent<GraphicRaycaster>();
    }

    private void WireMainButtons()
    {
        if (startButton    != null) startButton.onClick.AddListener(OnStartClicked);
        if (loadButton     != null) loadButton.onClick.AddListener(OnLoadClicked);
        if (settingsButton != null) settingsButton.onClick.AddListener(() => _settingsPanel.SetActive(true));
        if (exitButton     != null) exitButton.onClick.AddListener(OnExitClicked);
    }

    private void ApplySavedSettings()
    {
        AudioListener.volume = PlayerPrefs.GetFloat(PrefMasterVol, 1f);
        Screen.fullScreen    = PlayerPrefs.GetInt(PrefFullscreen, 1) == 1;
    }

    private GameObject BuildStartFlowPanel()
    {
        var panel   = CreateOverlayDimmer("StartFlowPanel");
        var content = CreateCardContent(panel, "START", 360, bottomPad: 0);

        AddMenuButton(content, "CONTINUE",  OnContinueClicked);
        AddMenuButton(content, "NEW GAME",  OnNewGameClicked);

        AddDivider(content);

        AddMenuButton(content, "BACK", () =>
        {
            panel.SetActive(false);
        }, isAccent: false, dimmed: true);

        panel.SetActive(false);
        return panel;
    }

    private GameObject BuildSlotPanel(string title, bool isNewGame)
    {
        var panel = CreateOverlayDimmer(isNewGame ? "NewGameSlotPanel" : "LoadPanel");

        // ── Card ──
        var card     = new GameObject("Card");
        card.transform.SetParent(panel.transform, false);
        var cardRect = card.AddComponent<RectTransform>();
        cardRect.anchorMin        = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax        = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta        = new Vector2(660, 480);
        cardRect.anchoredPosition = Vector2.zero;
        card.AddComponent<Image>().color = PanelBg;

        // ── Top accent bar ──
        AddAnchoredBar(card);

        // ── Title ──
        AddAnchoredTitle(card, title);

        // ── Slots scrollable area (fills card, leaves 80px at bottom) ──
        var slotsArea  = new GameObject("SlotsArea");
        slotsArea.transform.SetParent(card.transform, false);
        var saRect     = slotsArea.AddComponent<RectTransform>();
        saRect.anchorMin = Vector2.zero;
        saRect.anchorMax = Vector2.one;
        saRect.offsetMin = new Vector2(24, 76);   // leave 76px at bottom for action bar
        saRect.offsetMax = new Vector2(-24, -72); // leave 72px at top for title
        var saVLG      = slotsArea.AddComponent<VerticalLayoutGroup>();
        saVLG.spacing               = 8;
        saVLG.childControlWidth     = true;
        saVLG.childControlHeight    = true;
        saVLG.childForceExpandWidth  = true;
        saVLG.childForceExpandHeight = false;
        saVLG.padding = new RectOffset(0, 0, 8, 0);

        // ── Bottom action bar (fixed, anchored to card bottom) ──
        var bar     = new GameObject("BottomBar");
        bar.transform.SetParent(card.transform, false);
        var barRect = bar.AddComponent<RectTransform>();
        barRect.anchorMin        = new Vector2(0, 0);
        barRect.anchorMax        = new Vector2(1, 0);
        barRect.sizeDelta        = new Vector2(-48, 56);
        barRect.anchoredPosition = new Vector2(0, 36);
        var barHLG   = bar.AddComponent<HorizontalLayoutGroup>();
        barHLG.spacing               = 10;
        barHLG.childControlWidth     = true;
        barHLG.childControlHeight    = true;
        barHLG.childForceExpandWidth  = true;
        barHLG.childForceExpandHeight = true;

        AddBarButton(bar, "CLOSE", () => panel.SetActive(false), isAccent: false);

        panel.SetActive(false);

        // Store the slots area reference
        if (isNewGame) _newGameSlotsArea = slotsArea;
        else           _loadSlotsArea    = slotsArea;

        return panel;
    }

    private GameObject BuildSettingsPanel()
    {
        var panel   = CreateOverlayDimmer("SettingsPanel");
        var content = CreateCardContent(panel, "SETTINGS", 680, bottomPad: 0);

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
        AddMenuButton(content, "CLOSE", () => panel.SetActive(false), isAccent: true);

        panel.SetActive(false);
        return panel;
    }

    private void PopulateLoadSlots()
    {
        ClearChildren(_loadSlotsArea);

        for (int i = 0; i < SaveGameManager.SLOT_COUNT; i++)
        {
            int      idx  = i;
            var      save = SaveGameManager.Instance.GetSaveInfo(idx);
            string   info = save.hasData
                ? $"Slot {i + 1}  —  Level {save.level + 1}  |  {save.saveDate}  {save.saveTime}"
                : $"Slot {i + 1}  —  EMPTY";

            AddSlotRow(_loadSlotsArea, info, save.hasData,
                onLoad:  save.hasData ? () => LoadSlot(idx) : (UnityEngine.Events.UnityAction)null,
                onClear: save.hasData ? () => ClearSlot(idx, isNewGame: false) : (UnityEngine.Events.UnityAction)null);
        }
    }

    private void PopulateNewGameSlots()
    {
        ClearChildren(_newGameSlotsArea);

        for (int i = 0; i < SaveGameManager.SLOT_COUNT; i++)
        {
            int      idx  = i;
            var      save = SaveGameManager.Instance.GetSaveInfo(idx);
            string   info = save.hasData
                ? $"Slot {i + 1}  —  Overwrite  Level {save.level + 1}  |  {save.saveDate}"
                : $"Slot {i + 1}  —  Empty (Start here)";

            // New game slots are always clickable — picking an occupied one overwrites it
            AddSlotRow(_newGameSlotsArea, info, alwaysClickable: true,
                onLoad:  () => StartNewGameInSlot(idx),
                onClear: save.hasData ? () => ClearSlot(idx, isNewGame: true) : (UnityEngine.Events.UnityAction)null);
        }
    }

    private void ClearSlot(int slotIndex, bool isNewGame)
    {
        SaveGameManager.Instance.DeleteSlot(slotIndex);
        if (isNewGame) PopulateNewGameSlots();
        else           PopulateLoadSlots();
    }

    private void OnStartClicked()
    {
        _startFlowPanel.SetActive(true);
    }

    private void OnContinueClicked()
    {
        // Find the most recent save (highest slotIndex that hasData, or slot with most recent date)
        SaveData best     = null;
        int      bestSlot = -1;
        for (int i = 0; i < SaveGameManager.SLOT_COUNT; i++)
        {
            var s = SaveGameManager.Instance.GetSaveInfo(i);
            if (s.hasData) { best = s; bestSlot = i; } // last valid wins — simple heuristic
        }

        if (best != null)
        {
            LoadSlot(bestSlot);
        }
        else
        {
            // No saves — act like New Game
            _startFlowPanel.SetActive(false);
            _newGameSlotPanel.SetActive(true);
            PopulateNewGameSlots();
        }
    }

    private void OnNewGameClicked()
    {
        _startFlowPanel.SetActive(false);
        PopulateNewGameSlots();
        _newGameSlotPanel.SetActive(true);
    }

    private void OnLoadClicked()
    {
        PopulateLoadSlots();
        _loadPanel.SetActive(true);
    }

    private void OnExitClicked()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void LoadSlot(int slotIndex)
    {
        SaveGameManager.Instance.SetActiveSlot(slotIndex);
        SceneManager.LoadScene(loadGameScene); // skip cinematic, go straight to MainGame
    }

    private void StartNewGameInSlot(int slotIndex)
    {
        SaveGameManager.Instance.ClearActiveSlot();
        // We pre-assign the slot so the first in-game Save goes to the right place
        SaveGameManager.Instance.SetActiveSlot(slotIndex);
        // But don't set _pendingLoad — it's a fresh game, not a restore
        // Use the slot purely to know where to save; clear the pendingLoad flag
        SaveGameManager.Instance.ClearActiveSlot();
        // Re-assign without arming the restore
        // (Directly set backing field via a dedicated method)
        SaveGameManager.Instance.SetNewGameSlot(slotIndex);
        SceneManager.LoadScene(newGameScene);  // play cinematic for new game
    }

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
    }

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

    private GameObject CreateCardContent(GameObject parent, string title,
        float cardHeight, float bottomPad)
    {
        var card     = new GameObject("Card");
        card.transform.SetParent(parent.transform, false);
        var cardRect = card.AddComponent<RectTransform>();
        cardRect.anchorMin        = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax        = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta        = new Vector2(560, cardHeight);
        cardRect.anchoredPosition = Vector2.zero;
        card.AddComponent<Image>().color = PanelBg;

        AddAnchoredBar(card);
        AddAnchoredTitle(card, title);

        float bottomOffset = 20f + bottomPad;
        var content     = new GameObject("Content");
        content.transform.SetParent(card.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(28, bottomOffset);
        contentRect.offsetMax = new Vector2(-28, -72);

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

    private void AddAnchoredBar(GameObject card)
    {
        var bar     = new GameObject("AccentBar");
        bar.transform.SetParent(card.transform, false);
        var barRect = bar.AddComponent<RectTransform>();
        barRect.anchorMin        = new Vector2(0, 1);
        barRect.anchorMax        = new Vector2(1, 1);
        barRect.sizeDelta        = new Vector2(0, 4);
        barRect.anchoredPosition = new Vector2(0, -2);
        bar.AddComponent<Image>().color = Accent;
    }

    private void AddAnchoredTitle(GameObject card, string title)
    {
        var go   = new GameObject("Title");
        go.transform.SetParent(card.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0, 1);
        rect.anchorMax        = new Vector2(1, 1);
        rect.sizeDelta        = new Vector2(-40, 52);
        rect.anchoredPosition = new Vector2(0, -36);
        var tmp  = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = title;
        tmp.fontSize  = 22;
        tmp.color     = Accent;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    private void AddMenuButton(GameObject parent, string text,
        UnityEngine.Events.UnityAction onClick, bool isAccent = false, bool dimmed = false)
    {
        var go  = new GameObject($"Btn_{text}");
        go.transform.SetParent(parent.transform, false);
        var le  = go.AddComponent<LayoutElement>();
        le.minHeight       = 50;
        le.preferredHeight = 50;

        Color baseCol = isAccent ? Accent : (dimmed ? new Color(0.09f, 0.09f, 0.09f) : BtnNormal);
        var img  = go.AddComponent<Image>();
        img.color = baseCol;

        var btn  = go.AddComponent<Button>();
        var cs   = btn.colors;
        cs.normalColor      = baseCol;
        cs.highlightedColor = isAccent
            ? new Color(Accent.r + 0.1f, Accent.g + 0.04f, Accent.b + 0.04f, 1f)
            : BtnHover;
        cs.pressedColor  = BtnPress;
        cs.selectedColor = cs.highlightedColor;
        btn.colors       = cs;
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        MakeTMPLabel(go, text, 16, FontStyles.Bold, isAccent || dimmed ? Color.white : TextCol);
    }

    private void AddSlotRow(GameObject parent, string label, bool alwaysClickable,
        UnityEngine.Events.UnityAction onLoad, UnityEngine.Events.UnityAction onClear)
    {
        var row = new GameObject("SlotRow");
        row.transform.SetParent(parent.transform, false);
        var le  = row.AddComponent<LayoutElement>();
        le.minHeight       = 60;
        le.preferredHeight = 60;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing               = 6;
        hlg.childControlWidth     = true;
        hlg.childControlHeight    = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;

        // ── Main slot button (takes remaining width) ──
        var mainGo  = new GameObject("SlotBtn");
        mainGo.transform.SetParent(row.transform, false);
        var mainLE  = mainGo.AddComponent<LayoutElement>();
        mainLE.flexibleWidth = 1;

        bool clickable = alwaysClickable || onLoad != null;
        Color baseCol  = clickable ? BtnNormal : new Color(0.08f, 0.08f, 0.08f, 1f);
        var mainImg    = mainGo.AddComponent<Image>();
        mainImg.color  = baseCol;

        var mainBtn    = mainGo.AddComponent<Button>();
        var mcs        = mainBtn.colors;
        mcs.normalColor      = baseCol;
        mcs.highlightedColor = clickable ? BtnHover : baseCol;
        mcs.pressedColor     = BtnPress;
        mcs.disabledColor    = new Color(0.07f, 0.07f, 0.07f, 0.6f);
        mainBtn.colors       = mcs;
        mainBtn.targetGraphic = mainImg;
        mainBtn.interactable  = clickable;
        if (clickable && onLoad != null) mainBtn.onClick.AddListener(onLoad);

        MakeTMPLabel(mainGo, label, 14, FontStyles.Normal,
            clickable ? TextCol : TextDim, TextAlignmentOptions.MidlineLeft, new Vector2(14, 0));

        // ── Clear button (fixed 72px wide) ──
        var clrGo  = new GameObject("ClearBtn");
        clrGo.transform.SetParent(row.transform, false);
        var clrLE  = clrGo.AddComponent<LayoutElement>();
        clrLE.minWidth       = 72;
        clrLE.preferredWidth = 72;
        clrLE.flexibleWidth  = 0;

        bool canClear   = onClear != null;
        Color clrBase   = canClear ? ClearCol : new Color(0.07f, 0.07f, 0.07f, 1f);
        var clrImg      = clrGo.AddComponent<Image>();
        clrImg.color    = clrBase;

        var clrBtn      = clrGo.AddComponent<Button>();
        var ccs         = clrBtn.colors;
        ccs.normalColor      = clrBase;
        ccs.highlightedColor = canClear ? new Color(0.45f, 0.07f, 0.07f, 1f) : clrBase;
        ccs.pressedColor     = canClear ? new Color(0.6f,  0.09f, 0.09f, 1f) : clrBase;
        ccs.disabledColor    = new Color(0.07f, 0.07f, 0.07f, 0.4f);
        clrBtn.colors        = ccs;
        clrBtn.targetGraphic = clrImg;
        clrBtn.interactable  = canClear;
        if (canClear) clrBtn.onClick.AddListener(onClear);

        MakeTMPLabel(clrGo, "CLEAR", 12, FontStyles.Bold,
            canClear ? Color.white : TextDim);
    }

    private void AddSectionLabel(GameObject parent, string text)
    {
        var go  = new GameObject($"Label_{text}");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = 26;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = 12;
        tmp.color     = new Color(Accent.r + 0.1f, Accent.g + 0.08f, Accent.b + 0.08f, 0.85f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Left;
    }

    private void AddDivider(GameObject parent)
    {
        var go  = new GameObject("Divider");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = 2;
        go.AddComponent<Image>().color = AccentDim;
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
        slider.minValue     = min;
        slider.maxValue     = max;
        slider.value        = initVal;
        slider.wholeNumbers = false;

        var bg     = new GameObject("Background");
        bg.transform.SetParent(go.transform, false);
        var bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.3f);
        bgRect.anchorMax = new Vector2(1, 0.7f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = TrackCol;

        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        var faRect   = fillArea.AddComponent<RectTransform>();
        faRect.anchorMin = new Vector2(0, 0.3f);
        faRect.anchorMax = new Vector2(1, 0.7f);
        faRect.offsetMin = new Vector2(5, 0);
        faRect.offsetMax = new Vector2(-5, 0);

        var fill     = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fill.AddComponent<Image>().color = SliderFill;

        var handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(go.transform, false);
        var haRect     = handleArea.AddComponent<RectTransform>();
        haRect.anchorMin = Vector2.zero;
        haRect.anchorMax = Vector2.one;
        haRect.offsetMin = new Vector2(10, 0);
        haRect.offsetMax = new Vector2(-10, 0);

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

        if (onChange != null) slider.onValueChanged.AddListener(onChange);
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
        checkGo.AddComponent<Image>().color = Accent;

        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic       = checkGo.GetComponent<Image>();
        toggle.isOn          = initVal;
        if (onChange != null) toggle.onValueChanged.AddListener(onChange);
    }

    private void AddBarButton(GameObject parent, string text,
        UnityEngine.Events.UnityAction onClick, bool isAccent)
    {
        var go  = new GameObject($"BarBtn_{text}");
        go.transform.SetParent(parent.transform, false);

        Color baseCol = isAccent ? Accent : BtnNormal;
        var img       = go.AddComponent<Image>();
        img.color     = baseCol;

        var btn       = go.AddComponent<Button>();
        var cs        = btn.colors;
        cs.normalColor      = baseCol;
        cs.highlightedColor = isAccent
            ? new Color(Accent.r + 0.1f, Accent.g + 0.04f, Accent.b + 0.04f, 1f)
            : BtnHover;
        cs.pressedColor  = BtnPress;
        cs.selectedColor = cs.highlightedColor;
        btn.colors       = cs;
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        MakeTMPLabel(go, text, 15, FontStyles.Bold, Color.white);
    }

    private void MakeTMPLabel(GameObject parent, string text, float fontSize,
        FontStyles style, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center,
        Vector2 leftOffset = default)
    {
        var go   = new GameObject("Text");
        go.transform.SetParent(parent.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = leftOffset;
        rect.offsetMax = Vector2.zero;
        var tmp  = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = style;
        tmp.color     = color;
        tmp.alignment = alignment;
    }

    private static void ClearChildren(GameObject parent)
    {
        var toDestroy = new List<GameObject>();
        foreach (Transform child in parent.transform)
            toDestroy.Add(child.gameObject);
        foreach (var child in toDestroy)
            Destroy(child);
    }
}
