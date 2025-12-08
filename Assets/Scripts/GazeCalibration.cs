using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class GazeCalibration : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GazeDetector gazeDetector;

    [Header("UI")]
    [SerializeField] private GameObject calibrationPanel;
    [SerializeField] private RectTransform targetDot;
    [SerializeField] private Text instructionText;

    [Header("Calibration Settings")]
    [SerializeField] private float dotSize = 60f;
    [SerializeField] private float edgeMargin = 150f;
    [SerializeField] private float visualEdgeMargin = 50f;
    [SerializeField] private float collectionTime = 1.5f;
    [SerializeField] private int samplesPerPoint = 50;

    [Header("Sensitivity Boost")]
    [SerializeField][Range(0.5f, 3f)] private float horizontalBoost = 1.3f;
    [SerializeField][Range(0.5f, 4f)] private float verticalBoost = 2.2f;

    [Header("Scale Limits")]
    [SerializeField] private float minScaleX = 0.5f;
    [SerializeField] private float maxScaleX = 6f;
    [SerializeField] private float minScaleY = 0.5f;
    [SerializeField] private float maxScaleY = 10f;

    [Header("Visual Feedback")]
    [SerializeField] private Color targetColor = new Color(1f, 0.8f, 0f);
    [SerializeField] private Color collectingColor = Color.green;

    // 5-point calibration (center + cardinal directions)
    private Vector2[] screenPoints = new Vector2[5];
    private Vector2[] rawGazePoints = new Vector2[5];

    private int currentPoint = 0;
    private bool isCalibrating = false;
    private bool isWaitingForConfirmation = false;

    private float pulseTime = 0f;

    private void Start()
    {
        if (calibrationPanel != null)
        {
            calibrationPanel.SetActive(false);
        }

        LoadCalibration();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.C) && !isCalibrating)
        {
            StartCalibration();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetCalibration();
        }

        if (isWaitingForConfirmation)
        {
            pulseTime += Time.deltaTime * 3f;
            float pulse = (Mathf.Sin(pulseTime) + 1f) / 2f;
            targetDot.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.2f, pulse);
        }
    }

    public void StartCalibration()
    {
        if (gazeDetector == null)
        {
            Debug.LogError("No GazeDetector assigned!");
            return;
        }

        isCalibrating = true;
        currentPoint = 0;

        gazeDetector.SetCalibration(Vector2.zero, Vector2.one, false, false);
        gazeDetector.SetDebugCursorVisible(false);

        CalculateScreenPoints();

        if (calibrationPanel == null)
        {
            CreateCalibrationUI();
        }

        calibrationPanel.SetActive(true);
        StartCoroutine(CalibrateAllPoints());

        Debug.Log("=== CALIBRATION STARTED ===");
        Debug.Log($"Collecting {samplesPerPoint} samples per point over {collectionTime}s");
    }

    private IEnumerator CalibrateAllPoints()
    {
        for (currentPoint = 0; currentPoint < screenPoints.Length; currentPoint++)
        {
            yield return CalibratePoint(currentPoint);
        }

        FinishCalibration();
    }

    private IEnumerator CalibratePoint(int pointIndex)
    {
        string pointName = GetPointName(pointIndex);
        Vector2 visualPosition = CalculateVisualPosition(pointIndex);
        targetDot.position = visualPosition;
        targetDot.GetComponent<Image>().color = targetColor;
        targetDot.localScale = Vector3.one;
        pulseTime = 0f;

        instructionText.text = $"<size=36><b>{pointName}</b></size>\n\n" +
                              $"Point {pointIndex + 1} of {screenPoints.Length}\n\n" +
                              $"Look at the dot with your eyes\n" +
                              $"Press <b>SPACEBAR</b> when ready";

        isWaitingForConfirmation = true;
        while (!Input.GetKeyDown(KeyCode.Space))
        {
            if (!gazeDetector.IsTracking)
            {
                instructionText.text = "<color=red>FACE NOT DETECTED</color>\n\n" +
                                      "Make sure your face is visible to the camera";
            }
            yield return null;
        }
        isWaitingForConfirmation = false;

        targetDot.GetComponent<Image>().color = collectingColor;
        targetDot.localScale = Vector3.one * 1.2f;

        instructionText.text = $"<size=36><b>{pointName}</b></size>\n\n" +
                              $"<color=green><b>RECORDING...</b></color>\n\n" +
                              $"Keep looking at the dot!";

        List<Vector2> samples = new List<Vector2>();
        float timer = 0f;

        while (timer < collectionTime && samples.Count < samplesPerPoint)
        {
            if (gazeDetector.IsTracking)
            {
                samples.Add(gazeDetector.GazeNormalized);
            }

            timer += Time.deltaTime;
            yield return null;
        }

        if (samples.Count == 0)
        {
            Debug.LogError($"Point {pointIndex + 1}: NO SAMPLES COLLECTED!");
            rawGazePoints[pointIndex] = Vector2.one * 0.5f;
        }
        else
        {
            // Average samples
            Vector2 sum = Vector2.zero;
            foreach (var sample in samples)
            {
                sum += sample;
            }
            Vector2 average = sum / samples.Count;

            // Calculate variance
            float variance = 0f;
            foreach (var sample in samples)
            {
                variance += Vector2.Distance(sample, average);
            }
            variance /= samples.Count;

            rawGazePoints[pointIndex] = average;

            string quality = variance < 0.02f ? "EXCELLENT" : variance < 0.05f ? "GOOD" : "ACCEPTABLE";

            Debug.Log($"Point {pointIndex + 1} ({pointName}): " +
                     $"Samples={samples.Count}, " +
                     $"RawGaze=({average.x:F3}, {average.y:F3}), " +
                     $"Variance={variance:F4} ({quality}), " +
                     $"Target=({screenPoints[pointIndex].x:F0}, {screenPoints[pointIndex].y:F0})");
        }

        targetDot.localScale = Vector3.one * 1.5f;

        yield return new WaitForSeconds(0.3f);
    }

    private void FinishCalibration()
    {
        Debug.Log("=== CALCULATING CALIBRATION ===");

        CalculateCalibrationMapping();
        SaveCalibration();

        isCalibrating = false;
        if (calibrationPanel != null)
        {
            calibrationPanel.SetActive(false);
        }

        gazeDetector.SetDebugCursorVisible(true);

        Debug.Log("=== CALIBRATION COMPLETE ===");
    }

    private void CalculateCalibrationMapping()
    {
        // Convert screen points to normalized (0-1)
        Vector2[] normalizedTargets = new Vector2[screenPoints.Length];
        for (int i = 0; i < screenPoints.Length; i++)
        {
            normalizedTargets[i] = new Vector2(
                screenPoints[i].x / Screen.width,
                1f - (screenPoints[i].y / Screen.height)
            );
        }

        // Use cardinal points for mapping
        Vector2 targetCenter = normalizedTargets[0];
        Vector2 faceCenter = rawGazePoints[0];

        Vector2 targetLeft = normalizedTargets[1];
        Vector2 targetRight = normalizedTargets[2];
        Vector2 targetTop = normalizedTargets[3];
        Vector2 targetBottom = normalizedTargets[4];

        Vector2 faceLeft = rawGazePoints[1];
        Vector2 faceRight = rawGazePoints[2];
        Vector2 faceTop = rawGazePoints[3];
        Vector2 faceBottom = rawGazePoints[4];

        // Detect inversion
        bool invertX = faceRight.x < faceLeft.x;
        bool invertY = faceBottom.y < faceTop.y;

        // Apply inversion to all points
        if (invertX)
        {
            for (int i = 0; i < rawGazePoints.Length; i++)
            {
                rawGazePoints[i].x = 1f - rawGazePoints[i].x;
            }
            faceCenter.x = 1f - faceCenter.x;
            faceLeft.x = 1f - faceLeft.x;
            faceRight.x = 1f - faceRight.x;
        }

        if (invertY)
        {
            for (int i = 0; i < rawGazePoints.Length; i++)
            {
                rawGazePoints[i].y = 1f - rawGazePoints[i].y;
            }
            faceCenter.y = 1f - faceCenter.y;
            faceTop.y = 1f - faceTop.y;
            faceBottom.y = 1f - faceBottom.y;
        }

        // Calculate ranges
        float targetXRange = targetRight.x - targetLeft.x;
        float targetYRange = targetBottom.y - targetTop.y;
        float faceXRange = Mathf.Abs(faceRight.x - faceLeft.x);
        float faceYRange = Mathf.Abs(faceBottom.y - faceTop.y);

        // Calculate scale with boost
        Vector2 scale = new Vector2(
            faceXRange > 0.01f ? (targetXRange / faceXRange) * horizontalBoost : 1f,
            faceYRange > 0.01f ? (targetYRange / faceYRange) * verticalBoost : 1f
        );

        scale.x = Mathf.Clamp(scale.x, minScaleX, maxScaleX);
        scale.y = Mathf.Clamp(scale.y, minScaleY, maxScaleY);

        // Calculate offset
        Vector2 offset = new Vector2(
            targetCenter.x - (faceCenter.x * scale.x),
            targetCenter.y - (faceCenter.y * scale.y)
        );

        Debug.Log($"Calibration: Offset={offset}, Scale={scale}, InvertX={invertX}, InvertY={invertY}");

        gazeDetector.SetCalibration(offset, scale, invertX, invertY);
    }

    private void CalculateScreenPoints()
    {
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        screenPoints[0] = new Vector2(centerX, centerY);
        screenPoints[1] = new Vector2(edgeMargin, centerY);
        screenPoints[2] = new Vector2(Screen.width - edgeMargin, centerY);
        screenPoints[3] = new Vector2(centerX, Screen.height - edgeMargin);
        screenPoints[4] = new Vector2(centerX, edgeMargin);
    }

    private Vector2 CalculateVisualPosition(int pointIndex)
    {
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        switch (pointIndex)
        {
            case 0: return new Vector2(centerX, centerY); // CENTER
            case 1: return new Vector2(visualEdgeMargin, centerY); // LEFT
            case 2: return new Vector2(Screen.width - visualEdgeMargin, centerY); // RIGHT
            case 3: return new Vector2(centerX, Screen.height - visualEdgeMargin); // TOP
            case 4: return new Vector2(centerX, visualEdgeMargin); // BOTTOM
            default: return new Vector2(centerX, centerY);
        }
    }

    private string GetPointName(int index)
    {
        string[] names = {
            "CENTER",
            "LEFT",
            "RIGHT",
            "TOP",
            "BOTTOM"
        };
        return index < names.Length ? names[index] : $"POINT {index + 1}";
    }

    private void SaveCalibration()
    {
        for (int i = 0; i < rawGazePoints.Length; i++)
        {
            PlayerPrefs.SetFloat($"GazeCal_Raw_{i}_X", rawGazePoints[i].x);
            PlayerPrefs.SetFloat($"GazeCal_Raw_{i}_Y", rawGazePoints[i].y);
        }
        PlayerPrefs.SetInt("GazeCal_PointCount", rawGazePoints.Length);
        PlayerPrefs.SetInt("GazeCal_Valid", 1);
        PlayerPrefs.Save();

        Debug.Log("Calibration saved");
    }

    private void LoadCalibration()
    {
        if (PlayerPrefs.GetInt("GazeCal_Valid", 0) != 1)
        {
            Debug.Log("No saved calibration");
            return;
        }

        int pointCount = PlayerPrefs.GetInt("GazeCal_PointCount", 5);
        if (pointCount != screenPoints.Length)
        {
            Debug.LogWarning($"Saved calibration mismatch");
            return;
        }

        for (int i = 0; i < rawGazePoints.Length; i++)
        {
            rawGazePoints[i] = new Vector2(
                PlayerPrefs.GetFloat($"GazeCal_Raw_{i}_X", 0),
                PlayerPrefs.GetFloat($"GazeCal_Raw_{i}_Y", 0)
            );
        }

        CalculateScreenPoints();
        CalculateCalibrationMapping();

        Debug.Log("Calibration loaded");
    }

    public void ResetCalibration()
    {
        PlayerPrefs.DeleteKey("GazeCal_Valid");
        PlayerPrefs.DeleteKey("GazeCal_PointCount");
        for (int i = 0; i < 5; i++)
        {
            PlayerPrefs.DeleteKey($"GazeCal_Raw_{i}_X");
            PlayerPrefs.DeleteKey($"GazeCal_Raw_{i}_Y");
        }
        PlayerPrefs.Save();

        gazeDetector.SetCalibration(Vector2.zero, Vector2.one, false, false);
        Debug.Log("Calibration reset");
    }

    private void CreateCalibrationUI()
    {
        GameObject canvasObj = new GameObject("CalibrationCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(canvas.transform, false);
        calibrationPanel = panelObj;

        Image panelImg = panelObj.AddComponent<Image>();
        panelImg.color = new Color(0, 0, 0, 0.85f);

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;

        GameObject dotObj = new GameObject("TargetDot");
        dotObj.transform.SetParent(canvas.transform, false);

        Image dotImg = dotObj.AddComponent<Image>();
        dotImg.color = targetColor;

        targetDot = dotObj.GetComponent<RectTransform>();
        targetDot.sizeDelta = new Vector2(dotSize, dotSize);
        targetDot.anchorMin = Vector2.zero;
        targetDot.anchorMax = Vector2.zero;
        targetDot.pivot = new Vector2(0.5f, 0.5f);

        GameObject textObj = new GameObject("InstructionText");
        textObj.transform.SetParent(panelObj.transform, false);

        instructionText = textObj.AddComponent<Text>();
        instructionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        instructionText.fontSize = 24;
        instructionText.alignment = TextAnchor.MiddleCenter;
        instructionText.color = Color.white;
        instructionText.supportRichText = true;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.2f, 0.65f);
        textRect.anchorMax = new Vector2(0.8f, 0.85f);
        textRect.sizeDelta = Vector2.zero;
    }
}
