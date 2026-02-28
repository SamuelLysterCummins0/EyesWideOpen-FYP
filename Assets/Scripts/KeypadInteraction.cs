using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class KeypadInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ZoneGazeDetector zoneGazeDetector;
    [SerializeField] private BlinkDetector blinkDetector;

    [Header("Keypad Settings")]
    [SerializeField] private string correctCode = "1234";
    [SerializeField] private float buttonPressDelay = 0.5f; // Cooldown between presses

    [Header("UI")]
    [SerializeField] private GameObject keypadCanvas;
    [SerializeField] private Text displayText;
    [SerializeField] private Text feedbackText;
    [SerializeField] private Button[] numberButtons = new Button[9];

    [Header("Audio")]
    [SerializeField] private AudioClip buttonPressSound;
    [SerializeField] private AudioClip successSound;
    [SerializeField] private AudioClip errorSound;

    // State
    private string enteredCode = "";
    private float lastButtonPressTime = 0f;
    private bool isActive = false;
    private bool isUnlocked = false;

    // Zone to number mapping (1-9, matching the grid)
    private Dictionary<GazeZone, int> zoneToNumber = new Dictionary<GazeZone, int>()
    {
        { GazeZone.TopLeft, 1 },
        { GazeZone.TopCenter, 2 },
        { GazeZone.TopRight, 3 },
        { GazeZone.MidLeft, 4 },
        { GazeZone.Center, 5 },
        { GazeZone.MidRight, 6 },
        { GazeZone.BottomLeft, 7 },
        { GazeZone.BottomCenter, 8 },
        { GazeZone.BottomRight, 9 }
    };

    private AudioSource audioSource;
    private GazeZone lastStableZone = GazeZone.None;
    private bool wasBlinking = false; // Track previous blink state

    private void Start()
    {
        if (keypadCanvas != null)
        {
            keypadCanvas.SetActive(false);
        }

        if (zoneGazeDetector == null)
        {
            zoneGazeDetector = FindObjectOfType<ZoneGazeDetector>();
        }

        if (blinkDetector == null)
        {
            blinkDetector = FindObjectOfType<BlinkDetector>();
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Subscribe to zone events
        if (zoneGazeDetector != null)
        {
            zoneGazeDetector.OnZoneStable.AddListener(OnZoneStable);
        }

        if (keypadCanvas == null)
        {
            CreateKeypadUI();
        }

        keypadCanvas.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            if (!isActive)
                ActivateKeypad();
            else
                DeactivateKeypad();
            return;
        }

        if (!isActive) return;

        // Update button highlights based on current zone
        UpdateButtonHighlights();

        // Show instructions
        if (feedbackText != null)
        {
            feedbackText.text = "Look at a number, then BLINK to enter it";
        }

        // Poll blink state - detect blink START (transition from not blinking to blinking)
        bool isBlinking = blinkDetector != null && blinkDetector.IsBlinking;

        if (isBlinking && !wasBlinking)
        {
            // Blink just started!
            OnBlinkDetected();
        }

        wasBlinking = isBlinking;

        
    }

    public void ActivateKeypad()
    {
        isActive = true;
        enteredCode = "";
        isUnlocked = false;
        keypadCanvas.SetActive(true);
        UpdateDisplay();

        Debug.Log($"Keypad activated. Correct code: {correctCode}");
    }

    public void DeactivateKeypad()
    {
        isActive = false;
        keypadCanvas.SetActive(false);
    }

    private void OnZoneStable(GazeZone zone)
    {
        if (!isActive || isUnlocked) return;

        // Store which zone is stable (ready for blink confirmation)
        lastStableZone = zone;
    }

    private void OnBlinkDetected()
    {
        if (!isActive || isUnlocked) return;

        // Check cooldown
        if (Time.time - lastButtonPressTime < buttonPressDelay)
        {
            return;
        }

        // Check if we have a stable zone
        if (lastStableZone == GazeZone.None)
        {
            return;
        }

        // Check if this zone has a number
        if (zoneToNumber.ContainsKey(lastStableZone))
        {
            int number = zoneToNumber[lastStableZone];
            PressNumber(number);
            lastButtonPressTime = Time.time;
        }
    }

    private void PressNumber(int number)
    {
        if (enteredCode.Length >= correctCode.Length)
        {
            return; // Code already full
        }

        enteredCode += number.ToString();
        UpdateDisplay();

        // Play sound
        if (audioSource != null && buttonPressSound != null)
        {
            audioSource.PlayOneShot(buttonPressSound);
        }

        Debug.Log($"Number pressed: {number}, Current code: {enteredCode}");

        // Check if code is complete
        if (enteredCode.Length == correctCode.Length)
        {
            CheckCode();
        }
    }

    private void CheckCode()
    {
        if (enteredCode == correctCode)
        {
            // SUCCESS
            isUnlocked = true;

            if (displayText != null)
            {
                displayText.text = "ACCESS GRANTED";
                displayText.color = Color.green;
            }

            if (feedbackText != null)
            {
                feedbackText.text = "Door unlocked!";
            }

            if (audioSource != null && successSound != null)
            {
                audioSource.PlayOneShot(successSound);
            }

            Debug.Log("CORRECT CODE! Door unlocked.");

            // Close keypad after delay
            Invoke("DeactivateKeypad", 2f);
        }
        else
        {
            // WRONG CODE
            if (displayText != null)
            {
                displayText.text = "ACCESS DENIED";
                displayText.color = Color.red;
            }

            if (feedbackText != null)
            {
                feedbackText.text = "Incorrect code! Try again.";
            }

            if (audioSource != null && errorSound != null)
            {
                audioSource.PlayOneShot(errorSound);
            }

            Debug.Log($"WRONG CODE! Entered: {enteredCode}, Correct: {correctCode}");

            // Reset after delay
            Invoke("ResetCode", 1.5f);
        }
    }

    private void ResetCode()
    {
        enteredCode = "";
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (displayText == null) return;

        displayText.color = Color.white;

        if (enteredCode.Length == 0)
        {
            displayText.text = "____";
        }
        else
        {
            string display = "";
            for (int i = 0; i < correctCode.Length; i++)
            {
                if (i < enteredCode.Length)
                {
                    display += "*";
                }
                else
                {
                    display += "_";
                }
            }
            displayText.text = display;
        }
    }

    private void UpdateButtonHighlights()
    {
        GazeZone currentZone = zoneGazeDetector.CurrentZone;

        foreach (var kvp in zoneToNumber)
        {
            GazeZone zone = kvp.Key;
            int number = kvp.Value;

            if (number >= 1 && number <= 9)
            {
                Button btn = numberButtons[number - 1];
                if (btn != null)
                {
                    Image btnImg = btn.GetComponent<Image>();

                    if (zone == currentZone && zoneGazeDetector.IsStable)
                    {
                        // Stable - ready to blink
                        btnImg.color = new Color(0f, 1f, 0f, 1f); // Bright green
                        btn.transform.localScale = Vector3.one * 1.1f; // Slightly bigger
                    }
                    else if (zone == currentZone)
                    {
                        // Looking but not stable
                        btnImg.color = new Color(1f, 1f, 0f, 0.8f); // Yellow
                        btn.transform.localScale = Vector3.one * 1.05f;
                    }
                    else
                    {
                        // Not looking
                        btnImg.color = new Color(0.3f, 0.3f, 0.3f); // Dark grey
                        btn.transform.localScale = Vector3.one;
                    }
                }
            }
        }
    }

    private void CreateKeypadUI()
    {
        GameObject canvasObj = new GameObject("KeypadCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasObj.AddComponent<GraphicRaycaster>();
        keypadCanvas = canvasObj;

        // Background panel
        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(canvas.transform, false);

        Image panelImg = panelObj.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.25f, 0.1f);
        panelRect.anchorMax = new Vector2(0.75f, 0.9f);
        panelRect.sizeDelta = Vector2.zero;

        // Title
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(panelObj.transform, false);

        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "SECURITY KEYPAD";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 48;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = new Color(0f, 1f, 1f);

        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 0.88f);
        titleRect.anchorMax = new Vector2(1, 0.96f);
        titleRect.sizeDelta = Vector2.zero;

        // Display background first
        GameObject displayObj = new GameObject("Display");
        displayObj.transform.SetParent(panelObj.transform, false);

        Image displayBg = displayObj.AddComponent<Image>();
        displayBg.color = new Color(0f, 0f, 0f, 0.7f);

        RectTransform displayRect = displayObj.GetComponent<RectTransform>();
        displayRect.anchorMin = new Vector2(0.1f, 0.75f);
        displayRect.anchorMax = new Vector2(0.9f, 0.85f);
        displayRect.sizeDelta = Vector2.zero;

        // Display text ON TOP of background
        GameObject displayTextObj = new GameObject("DisplayText");
        displayTextObj.transform.SetParent(displayObj.transform, false);

        displayText = displayTextObj.AddComponent<Text>();
        displayText.text = "____";
        displayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        displayText.fontSize = 64;
        displayText.alignment = TextAnchor.MiddleCenter;
        displayText.color = Color.white;

        RectTransform displayTextRect = displayTextObj.GetComponent<RectTransform>();
        displayTextRect.anchorMin = Vector2.zero;
        displayTextRect.anchorMax = Vector2.one;
        displayTextRect.sizeDelta = Vector2.zero;

        // Feedback text
        GameObject feedbackObj = new GameObject("Feedback");
        feedbackObj.transform.SetParent(panelObj.transform, false);

        feedbackText = feedbackObj.AddComponent<Text>();
        feedbackText.text = "Look at a number, then BLINK to enter it";
        feedbackText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        feedbackText.fontSize = 22;
        feedbackText.alignment = TextAnchor.MiddleCenter;
        feedbackText.color = Color.yellow;

        RectTransform feedbackRect = feedbackObj.GetComponent<RectTransform>();
        feedbackRect.anchorMin = new Vector2(0.05f, 0.02f);
        feedbackRect.anchorMax = new Vector2(0.95f, 0.08f);
        feedbackRect.sizeDelta = Vector2.zero;

        // Initialize the array
        numberButtons = new Button[9];

        // Button grid settings
        float buttonSize = 0.22f;
        float spacing = 0.05f;
        float totalGridWidth = (buttonSize * 3) + (spacing * 2);
        float totalGridHeight = (buttonSize * 3) + (spacing * 2);

        float availableHeight = 0.75f - 0.08f;
        float gridStartY = 0.08f + (availableHeight - totalGridHeight) / 2f;
        float gridStartX = (1f - totalGridWidth) / 2f;

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int number = (row * 3) + col + 1;
                int index = number - 1;

                // Button background
                GameObject btnObj = new GameObject($"Button_{number}");
                btnObj.transform.SetParent(panelObj.transform, false);

                Image btnImg = btnObj.AddComponent<Image>();
                btnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);

                Button btn = btnObj.AddComponent<Button>();
                btn.interactable = false;
                numberButtons[index] = btn;  // Assign to array

                RectTransform btnRect = btnObj.GetComponent<RectTransform>();
                float xPos = gridStartX + (col * (buttonSize + spacing));
                float yPos = gridStartY + ((2 - row) * (buttonSize + spacing));

                btnRect.anchorMin = new Vector2(xPos, yPos);
                btnRect.anchorMax = new Vector2(xPos + buttonSize, yPos + buttonSize);
                btnRect.sizeDelta = Vector2.zero;

                // Button label - CHILD of button
                GameObject labelObj = new GameObject("Label");
                labelObj.transform.SetParent(btnObj.transform, false);

                Text label = labelObj.AddComponent<Text>();
                label.text = number.ToString();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 72;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.fontStyle = FontStyle.Bold;
                label.raycastTarget = false;  // Don't block raycasts

                RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.sizeDelta = Vector2.zero;
                labelRect.anchoredPosition = Vector2.zero;

                Debug.Log($"Created button {number} at position ({xPos}, {yPos})");
            }
        }

        Debug.Log("Keypad UI fully created with all 9 buttons");
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        if (zoneGazeDetector != null)
        {
            zoneGazeDetector.OnZoneStable.RemoveListener(OnZoneStable);
        }
    }
}