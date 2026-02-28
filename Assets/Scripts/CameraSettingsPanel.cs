using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;

public class CameraSettingsPanel : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Material adjustmentMaterial;

    private GameObject settingsCanvas;
    private GameObject settingsPanel;
    private RawImage cameraPreview;
    private Slider brightnessSlider;
    private Slider contrastSlider;
    private Slider gammaSlider;
    private Image qualityIndicator;
    private Text qualityText;
    private Text brightnessValueText;
    private Text contrastValueText;
    private Text gammaValueText;

    private RenderTexture adjustedTexture;
    private float brightness = 1.0f;
    private float contrast = 1.0f;
    private float gamma = 1.0f;

    private bool isInitialized = false;

    void Start()
    {
        // Load saved settings
        brightness = PlayerPrefs.GetFloat("Cam_Brightness", 1.0f);
        contrast = PlayerPrefs.GetFloat("Cam_Contrast", 1.0f);
        gamma = PlayerPrefs.GetFloat("Cam_Gamma", 1.0f);

        // Create shader material
        Shader shader = Shader.Find("Custom/CameraAdjustment");
        if (shader != null)
        {
            adjustmentMaterial = new Material(shader);
            Debug.Log("Camera adjustment shader loaded");
        }
        else
        {
            Debug.LogError("CameraAdjustment shader NOT FOUND! Sliders won't work. Make sure shader file exists.");
        }

        CreateSettingsUI();
        settingsCanvas.SetActive(false);
        isInitialized = true;
    }

    void Update()
    {

        if (isInitialized)
        {
            UpdateAdjustedTexture();
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            TogglePanel();
        }

        if (settingsCanvas != null && settingsCanvas.activeSelf)
        {
            UpdateCameraPreview();
            UpdateQualityIndicator();

        }
    }

    private void UpdateAdjustedTexture()
    {
        var imageSource = ImageSourceProvider.ImageSource;
        if (imageSource == null || !imageSource.isPrepared)
        {
            return;
        }

        Texture sourceTex = imageSource.GetCurrentTexture();
        if (sourceTex == null)
        {
            return;
        }

        // If no shader, skip processing
        if (adjustmentMaterial == null)
        {
            return;
        }

        // Create RenderTexture if needed
        if (adjustedTexture == null ||
            adjustedTexture.width != sourceTex.width ||
            adjustedTexture.height != sourceTex.height)
        {
            if (adjustedTexture != null)
            {
                adjustedTexture.Release();
            }
            adjustedTexture = new RenderTexture(sourceTex.width, sourceTex.height, 0, RenderTextureFormat.ARGB32);
            adjustedTexture.Create();
        }

        // Apply shader adjustments
        adjustmentMaterial.SetFloat("_Brightness", brightness);
        adjustmentMaterial.SetFloat("_Contrast", contrast);
        adjustmentMaterial.SetFloat("_Gamma", gamma);

        Graphics.Blit(sourceTex, adjustedTexture, adjustmentMaterial);
    }

    // NEW: Public method to get the adjusted texture
    public Texture GetAdjustedTexture()
    {
        // Return adjusted texture if available, otherwise return original
        if (adjustedTexture != null && adjustedTexture.IsCreated())
        {
            return adjustedTexture;
        }

        var imageSource = ImageSourceProvider.ImageSource;
        return imageSource?.GetCurrentTexture();
    }

    public void TogglePanel()
    {
        bool isActive = !settingsCanvas.activeSelf;
        settingsCanvas.SetActive(isActive);

        if (isActive)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    private void UpdateCameraPreview()
    {

        if (adjustedTexture != null && adjustedTexture.IsCreated())
        {
            cameraPreview.texture = adjustedTexture;
        }

        var imageSource = ImageSourceProvider.ImageSource;
        if (imageSource == null || !imageSource.isPrepared)
        {
            return;
        }

        Texture sourceTex = imageSource.GetCurrentTexture();
        if (sourceTex == null)
        {
            return;
        }

        // If no shader, just show raw camera
        if (adjustmentMaterial == null)
        {
            cameraPreview.texture = sourceTex;
            return;
        }

        // Create RenderTexture if needed
        if (adjustedTexture == null ||
            adjustedTexture.width != sourceTex.width ||
            adjustedTexture.height != sourceTex.height)
        {
            if (adjustedTexture != null)
            {
                adjustedTexture.Release();
            }
            adjustedTexture = new RenderTexture(sourceTex.width, sourceTex.height, 0, RenderTextureFormat.ARGB32);
            adjustedTexture.Create();
        }

        // Apply shader adjustments EVERY FRAME
        adjustmentMaterial.SetFloat("_Brightness", brightness);
        adjustmentMaterial.SetFloat("_Contrast", contrast);
        adjustmentMaterial.SetFloat("_Gamma", gamma);

        // Blit with shader
        Graphics.Blit(sourceTex, adjustedTexture, adjustmentMaterial);

        // Display adjusted texture
        cameraPreview.texture = adjustedTexture;
    }

    private void UpdateQualityIndicator()
    {
        float confidence = 0.8f; // Placeholder

        if (confidence > 0.7f)
        {
            qualityIndicator.color = Color.green;
            qualityText.text = "GOOD TRACKING";
        }
        else if (confidence > 0.4f)
        {
            qualityIndicator.color = Color.yellow;
            qualityText.text = "POOR TRACKING";
        }
        else
        {
            qualityIndicator.color = Color.red;
            qualityText.text = "NO FACE DETECTED";
        }
    }

    public void SetBrightness(float value)
    {
        brightness = value;
        PlayerPrefs.SetFloat("Cam_Brightness", brightness);
        if (brightnessValueText != null)
        {
            brightnessValueText.text = value.ToString("F2");
        }
    }

    public void SetContrast(float value)
    {
        contrast = value;
        PlayerPrefs.SetFloat("Cam_Contrast", contrast);
        if (contrastValueText != null)
        {
            contrastValueText.text = value.ToString("F2");
        }
    }

    public void SetGamma(float value)
    {
        gamma = value;
        PlayerPrefs.SetFloat("Cam_Gamma", gamma);
        if (gammaValueText != null)
        {
            gammaValueText.text = value.ToString("F2");
        }
    }

    public void ResetToDefaults()
    {
        brightnessSlider.value = 1.0f;
        contrastSlider.value = 1.0f;
        gammaSlider.value = 1.0f;
    }

    private void CreateSettingsUI()
    {
        // Main Canvas
        settingsCanvas = new GameObject("CameraSettingsCanvas");
        Canvas canvas = settingsCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = settingsCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        settingsCanvas.AddComponent<GraphicRaycaster>();

        // Background Panel
        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(canvas.transform, false);
        settingsPanel = panelObj;

        Image panelImg = panelObj.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.15f, 0.1f);
        panelRect.anchorMax = new Vector2(0.85f, 0.9f);
        panelRect.sizeDelta = Vector2.zero;

        // Title
        CreateText("Title", panelObj.transform, "CAMERA SETTINGS", 40,
                   new Vector2(0, 0.92f), new Vector2(1, 1f), Color.cyan);

        // Camera Preview
        GameObject previewObj = new GameObject("CameraPreview");
        previewObj.transform.SetParent(panelObj.transform, false);

        cameraPreview = previewObj.AddComponent<RawImage>();
        cameraPreview.color = Color.white;
        cameraPreview.raycastTarget = false;

        RectTransform previewRect = previewObj.GetComponent<RectTransform>();
        previewRect.anchorMin = new Vector2(0.05f, 0.5f);
        previewRect.anchorMax = new Vector2(0.95f, 0.88f);
        previewRect.sizeDelta = Vector2.zero;

        

        // Quality Indicator (top right of preview)
        GameObject qualityObj = new GameObject("QualityIndicator");
        qualityObj.transform.SetParent(previewObj.transform, false);

        qualityIndicator = qualityObj.AddComponent<Image>();
        qualityIndicator.color = Color.green;

        RectTransform qualityRect = qualityObj.GetComponent<RectTransform>();
        qualityRect.anchorMin = new Vector2(0.85f, 0.9f);
        qualityRect.anchorMax = new Vector2(0.95f, 0.98f);
        qualityRect.sizeDelta = Vector2.zero;

        qualityText = CreateText("QualityText", previewObj.transform, "GOOD TRACKING", 18,
                                 new Vector2(0.6f, 0.9f), new Vector2(0.84f, 0.98f), Color.white);

        // Sliders
        float sliderY = 0.38f;
        float sliderSpacing = 0.12f;

        brightnessSlider = CreateSlider("Brightness", panelObj.transform, "Brightness",
                                       0.5f, 2.0f, brightness, sliderY, out brightnessValueText);
        brightnessSlider.onValueChanged.AddListener(SetBrightness);

        contrastSlider = CreateSlider("Contrast", panelObj.transform, "Contrast",
                                     0.5f, 2.0f, contrast, sliderY - sliderSpacing, out contrastValueText);
        contrastSlider.onValueChanged.AddListener(SetContrast);

        gammaSlider = CreateSlider("Gamma", panelObj.transform, "Gamma",
                                  0.5f, 2.0f, gamma, sliderY - (sliderSpacing * 2), out gammaValueText);
        gammaSlider.onValueChanged.AddListener(SetGamma);

        // Reset Button
        CreateButton("ResetButton", panelObj.transform, "Reset to Defaults",
                    new Vector2(0.3f, 0.05f), new Vector2(0.7f, 0.1f), ResetToDefaults);

        // Instructions
        CreateText("Instructions", panelObj.transform,
                   "Press TAB to close | Adjust settings for better face detection",
                   16, new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.04f), Color.gray);
    }

    private Slider CreateSlider(string name, Transform parent, string label,
                               float minValue, float maxValue, float currentValue,
                               float yPosition, out Text valueText)
    {
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);

        RectTransform sliderRect = sliderObj.AddComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.15f, yPosition);
        sliderRect.anchorMax = new Vector2(0.85f, yPosition + 0.08f);
        sliderRect.sizeDelta = Vector2.zero;

        // Label
        CreateText($"{name}_Label", sliderObj.transform, label, 24,
                  new Vector2(0, 0.6f), new Vector2(0.3f, 1f), Color.white);

        // Value display
        valueText = CreateText($"{name}_Value", sliderObj.transform, currentValue.ToString("F2"), 22,
                              new Vector2(0.85f, 0.6f), new Vector2(1f, 1f), Color.yellow);

        // Slider component
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.value = currentValue;

        // Background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(sliderObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.2f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.3f, 0.1f);
        bgRect.anchorMax = new Vector2(1f, 0.5f);
        bgRect.sizeDelta = Vector2.zero;

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillRect = fillArea.AddComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0.3f, 0.1f);
        fillRect.anchorMax = new Vector2(1f, 0.5f);
        fillRect.sizeDelta = Vector2.zero;

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0f, 0.8f, 1f);
        RectTransform fillImgRect = fill.GetComponent<RectTransform>();
        fillImgRect.sizeDelta = Vector2.zero;

        // Handle
        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0.3f, 0f);
        handleAreaRect.anchorMax = new Vector2(1f, 1f);
        handleAreaRect.sizeDelta = new Vector2(-20, 0);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20, 0);

        slider.fillRect = fillImgRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;

        return slider;
    }

    private void CreateButton(string name, Transform parent, string label,
                             Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        Button btn = btnObj.AddComponent<Button>();
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.3f, 0.3f, 0.3f);

        ColorBlock colors = btn.colors;
        colors.normalColor = new Color(0.3f, 0.3f, 0.3f);
        colors.highlightedColor = new Color(0.4f, 0.4f, 0.4f);
        colors.pressedColor = new Color(0.2f, 0.2f, 0.2f);
        btn.colors = colors;

        RectTransform btnRect = btnObj.GetComponent<RectTransform>();
        btnRect.anchorMin = anchorMin;
        btnRect.anchorMax = anchorMax;
        btnRect.sizeDelta = Vector2.zero;

        CreateText($"{name}_Label", btnObj.transform, label, 20,
                  Vector2.zero, Vector2.one, Color.white);

        btn.onClick.AddListener(onClick);
    }

    private Text CreateText(string name, Transform parent, string content, int fontSize,
                           Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        Text text = textObj.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = anchorMin;
        textRect.anchorMax = anchorMax;
        textRect.sizeDelta = Vector2.zero;

        return text;
    }

    private void OnDestroy()
    {
        if (adjustedTexture != null)
        {
            adjustedTexture.Release();
        }
    }
}