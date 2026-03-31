using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using mptcc = Mediapipe.Tasks.Components.Containers;

public class BlinkDetector : MonoBehaviour
{
    [Header("Blink Detection Settings")]
    [SerializeField] private float blinkThreshold = 0.35f;
    [SerializeField] private int minBlinkFrames = 2;
    [SerializeField] private float blinkCooldown = 0.15f;

    [Header("UI References")]
    [SerializeField] private Text blinkStatusText;
    [SerializeField] private Text earDebugText;
    [SerializeField] private Image blinkIndicator;

    [Header("Visual Feedback")]
    [SerializeField] private Color blinkColor = Color.green;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private bool showDebugInfo = true;

    private static readonly int[] LEFT_EYE = { 33, 160, 158, 133, 153, 144 };
    private static readonly int[] RIGHT_EYE = { 362, 385, 387, 263, 373, 380 };

    private bool isBlinking = false;
    private int blinkFrameCounter = 0;
    private float lastBlinkTime = 0f;
    private int totalBlinks = 0;

    private float currentLeftEAR = 0f;
    private float currentRightEAR = 0f;
    private float currentAvgEAR = 0f;

    // Calibration system for dynamic thresholds
    [Header("Calibration")]
    [SerializeField] private bool useCalibration = false;
    
    private float calibratedClosedBaseline = -1f; // EAR when eyes fully closed
    private float calibratedOpenBaseline = -1f;   // EAR when eyes fully open
    private float dynamicBlinkThreshold = 0.21f;
    private bool isCalibrated = false;

    public bool IsBlinking => isBlinking;
    public int TotalBlinks => totalBlinks;
    public float CurrentEAR => currentAvgEAR;

    void Start()
    {
        if (blinkIndicator != null)
        {
            blinkIndicator.color = normalColor;
        }

        // Load calibration if it exists
        if (useCalibration)
        {
            LoadCalibration();
        }

        if (isCalibrated)
        {
            Debug.Log($"Calibration loaded. Closed EAR: {calibratedClosedBaseline:F3}, Open EAR: {calibratedOpenBaseline:F3}");
            Debug.Log($"Dynamic Blink Threshold: {dynamicBlinkThreshold:F3}");
        }
        else
        {
            dynamicBlinkThreshold = blinkThreshold;
            Debug.Log("Using default thresholds. Run calibration for better accuracy. Threshold: " + dynamicBlinkThreshold);
        }
    }

    void Update()
    {
        // Press 'B' to start blink calibration (for quick testing)
        if (Input.GetKeyDown(KeyCode.B))
        {
            StartCalibration();
        }
    }

    public void ProcessLandmarks(List<mptcc.NormalizedLandmark> landmarks)
    {
        if (landmarks == null || landmarks.Count < 478)
        {
            return;
        }

        currentLeftEAR = CalculateEAR(landmarks, LEFT_EYE);
        currentRightEAR = CalculateEAR(landmarks, RIGHT_EYE);
        currentAvgEAR = (currentLeftEAR + currentRightEAR) / 2.0f;

        if (showDebugInfo)
        {
            UpdateDebugUI();
        }

        DetectBlink();
    }

    float CalculateEAR(List<mptcc.NormalizedLandmark> landmarks, int[] eyeIndices)
    {
        Vector3 p1 = GetLandmarkPosition(landmarks[eyeIndices[0]]);
        Vector3 p2 = GetLandmarkPosition(landmarks[eyeIndices[1]]);
        Vector3 p3 = GetLandmarkPosition(landmarks[eyeIndices[2]]);
        Vector3 p4 = GetLandmarkPosition(landmarks[eyeIndices[3]]);
        Vector3 p5 = GetLandmarkPosition(landmarks[eyeIndices[4]]);
        Vector3 p6 = GetLandmarkPosition(landmarks[eyeIndices[5]]);

        float vertical1 = Vector3.Distance(p2, p6);
        float vertical2 = Vector3.Distance(p3, p5);
        float horizontal = Vector3.Distance(p1, p4);

        float ear = (vertical1 + vertical2) / (2.0f * horizontal);

        return ear;
    }

    Vector3 GetLandmarkPosition(mptcc.NormalizedLandmark landmark)
    {
        return new Vector3(landmark.x, landmark.y, landmark.z);
    }

    void DetectBlink()
    {
        float threshold = isCalibrated ? dynamicBlinkThreshold : blinkThreshold;
        bool earBelowThreshold = currentLeftEAR < threshold && currentRightEAR < threshold;
        float timeSinceLastBlink = Time.time - lastBlinkTime;

        if (earBelowThreshold)
        {
            if (timeSinceLastBlink > blinkCooldown)
            {
                blinkFrameCounter++;

                if (blinkFrameCounter >= minBlinkFrames && !isBlinking)
                {
                    OnBlinkStart();
                }
            }
        }
        else
        {
            if (isBlinking)
            {
                OnBlinkEnd();
            }

            blinkFrameCounter = 0;
        }
    }

    void OnBlinkStart()
    {
        if (isBlinking) return;

        isBlinking = true;
        totalBlinks++;
        lastBlinkTime = Time.time;

        if (blinkIndicator != null)
        {
            blinkIndicator.color = blinkColor;
        }

        if (blinkStatusText != null)
        {
            StartCoroutine(ShowBlinkText());
        }

        Debug.Log($"BLINK DETECTED! Total blinks: {totalBlinks}");
    }

    void OnBlinkEnd()
    {
        isBlinking = false;

        if (blinkIndicator != null)
        {
            blinkIndicator.color = normalColor;
        }

        Debug.Log($"Blink ended");
    }

    IEnumerator ShowBlinkText()
    {
        if (blinkStatusText != null)
        {
            blinkStatusText.text = "BLINKED!";
            blinkStatusText.color = blinkColor;
            blinkStatusText.fontSize = 48;

            yield return new WaitForSeconds(0.5f);

            blinkStatusText.text = "";
        }
    }

    void UpdateDebugUI()
    {
        if (earDebugText != null)
        {
            earDebugText.text = $"Left EAR: {currentLeftEAR:F3}\n" +
                               $"Right EAR: {currentRightEAR:F3}\n" +
                               $"Avg EAR: {currentAvgEAR:F3}\n" +
                               $"Threshold: {blinkThreshold:F3}\n" +
                               $"Total Blinks: {totalBlinks}\n" +
                               $"Status: {(isBlinking ? "BLINKING" : "OPEN")}";
        }
    }

    public void SetBlinkThreshold(float threshold)
    {
        blinkThreshold = threshold;
        Debug.Log($"Blink threshold updated to: {threshold:F3}");
    }

    // ===== CALIBRATION SYSTEM =====

    public void StartCalibration()
    {
        Debug.Log("Starting Blink Calibration...");
        StartCoroutine(CalibrationSequence());
    }

    private IEnumerator CalibrationSequence()
    {
        // Step 1: Fully open eyes
        Debug.Log("CALIBRATION STEP 1: Open both eyes FULLY and keep them open for 2 seconds...");
        yield return new WaitForSeconds(2f);

        if (currentAvgEAR <= 0)
        {
            Debug.LogError("No valid EAR data during calibration step 1. Aborting.");
            yield break;
        }

        calibratedOpenBaseline = currentAvgEAR;
        Debug.Log($"Open baseline captured: {calibratedOpenBaseline:F3}");

        // Step 2: Fully close eyes
        yield return new WaitForSeconds(1f);
        Debug.Log("CALIBRATION STEP 2: Close both eyes FULLY and keep them closed for 2 seconds...");
        yield return new WaitForSeconds(2f);

        if (currentAvgEAR <= 0)
        {
            Debug.LogError("No valid EAR data during calibration step 2. Aborting.");
            yield break;
        }

        calibratedClosedBaseline = currentAvgEAR;
        Debug.Log($"Closed baseline captured: {calibratedClosedBaseline:F3}");

        // Calculate dynamic thresholds
        CalculateDynamicThresholds();

        // Save to PlayerPrefs
        SaveCalibration();

        Debug.Log("CALIBRATION COMPLETE!");
        Debug.Log($"Dynamic Blink Threshold: {dynamicBlinkThreshold:F3}");
    }

    private void CalculateDynamicThresholds()
    {
        if (calibratedClosedBaseline < 0 || calibratedOpenBaseline < 0)
        {
            Debug.LogError("Calibration baselines not set!");
            return;
        }

        // Blink threshold = midpoint between closed and open
        dynamicBlinkThreshold = (calibratedClosedBaseline + calibratedOpenBaseline) / 2f;

        isCalibrated = true;
    }

    private void SaveCalibration()
    {
        PlayerPrefs.SetFloat("BlinkDetector_ClosedBaseline", calibratedClosedBaseline);
        PlayerPrefs.SetFloat("BlinkDetector_OpenBaseline", calibratedOpenBaseline);
        PlayerPrefs.SetFloat("BlinkDetector_BlinkThreshold", dynamicBlinkThreshold);
        PlayerPrefs.SetInt("BlinkDetector_IsCalibrated", 1);
        PlayerPrefs.Save();

        Debug.Log("Calibration saved to PlayerPrefs");
    }

    private void LoadCalibration()
    {
        if (!PlayerPrefs.HasKey("BlinkDetector_IsCalibrated"))
        {
            Debug.Log("No previous calibration found.");
            isCalibrated = false;
            return;
        }

        calibratedClosedBaseline = PlayerPrefs.GetFloat("BlinkDetector_ClosedBaseline");
        calibratedOpenBaseline = PlayerPrefs.GetFloat("BlinkDetector_OpenBaseline");
        dynamicBlinkThreshold = PlayerPrefs.GetFloat("BlinkDetector_BlinkThreshold");
        isCalibrated = PlayerPrefs.GetInt("BlinkDetector_IsCalibrated") == 1;

        if (isCalibrated)
        {
            Debug.Log("Calibration loaded successfully!");
        }
    }

    public void ResetCalibration()
    {
        PlayerPrefs.DeleteKey("BlinkDetector_ClosedBaseline");
        PlayerPrefs.DeleteKey("BlinkDetector_OpenBaseline");
        PlayerPrefs.DeleteKey("BlinkDetector_BlinkThreshold");
        PlayerPrefs.DeleteKey("BlinkDetector_IsCalibrated");
        PlayerPrefs.Save();

        calibratedClosedBaseline = -1f;
        calibratedOpenBaseline = -1f;
        dynamicBlinkThreshold = blinkThreshold;
        isCalibrated = false;

        Debug.Log("Calibration reset. Using default thresholds.");
    }
}