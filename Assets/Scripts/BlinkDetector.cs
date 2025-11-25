using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using mptcc = Mediapipe.Tasks.Components.Containers;

public class BlinkDetector : MonoBehaviour
{
    [Header("Blink Detection Settings")]
    [SerializeField] private float blinkThreshold = 0.21f;
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

    public bool IsBlinking => isBlinking;
    public int TotalBlinks => totalBlinks;
    public float CurrentEAR => currentAvgEAR;

    void Start()
    {
        if (blinkIndicator != null)
        {
            blinkIndicator.color = normalColor;
        }

        Debug.Log("Blink Detector initialized. Threshold: " + blinkThreshold);
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
        bool earBelowThreshold = currentAvgEAR < blinkThreshold;
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
}