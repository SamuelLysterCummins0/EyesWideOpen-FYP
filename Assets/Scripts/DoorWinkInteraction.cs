using UnityEngine;
using UnityEngine.UI;

// Attach to the player (or any persistent GameObject in the scene).
// Uses the gaze cursor position to aim at safe room doors.
// When the player blinks while looking at a door, that specific door opens/closes.
public class DoorWinkInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GazeDetector gazeDetector;
    [SerializeField] private BlinkDetector blinkDetector;
    [SerializeField] private Camera playerCamera;

    [Header("Settings")]
    [Tooltip("Max distance the gaze ray travels to detect a door")]
    [SerializeField] private float rayDistance = 4f;
    [Tooltip("Radius for gaze detection so open doorway center still catches the door")]
    [SerializeField] private float gazeHitRadius = 0.2f;

    // Not serialized — keeps the prompt text consistent regardless of old serialized scene data.
    private const string doorPromptText = "Blink to open / close door";

    private SafeRoomDoor currentDoor;
    private Text uiPrompt;
    private bool blinkConsumed = false;

    private void Start()
    {
        if (gazeDetector == null) gazeDetector = FindObjectOfType<GazeDetector>();
        if (blinkDetector == null) blinkDetector = FindObjectOfType<BlinkDetector>();
        if (playerCamera == null) playerCamera = Camera.main;

        BuildPromptUI();
    }

    private void Update()
    {
        // --- Gaze ray to find which door we are looking at ---
        SafeRoomDoor targeted = null;

        if (gazeDetector != null && gazeDetector.IsTracking && playerCamera != null)
        {
            Ray ray = gazeDetector.GetGazeRay(playerCamera);
            RaycastHit hit;

            // SphereCast is more forgiving than a thin ray and keeps targeting stable
            // when the door is open and the player looks through the middle gap.
            if (Physics.SphereCast(ray, gazeHitRadius, out hit, rayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                // Check the hit object and its parents for a SafeRoomDoor component
                targeted = hit.collider.GetComponentInParent<SafeRoomDoor>();
                if (targeted == null)
                    targeted = hit.collider.GetComponent<SafeRoomDoor>();

                // Ignore doors managed by IntroDoorInteraction — that script
                // handles its own gaze, text, and open logic independently.
                if (targeted != null && targeted.GetComponent<IntroDoorInteraction>() != null)
                    targeted = null;
            }
        }

        currentDoor = targeted;

        // Show prompt only when a door is targeted
        if (uiPrompt != null)
            uiPrompt.gameObject.SetActive(currentDoor != null);

        // --- Blink triggers the targeted door ---
        if (currentDoor != null && blinkDetector != null)
        {
            // blinkConsumed prevents the door toggling multiple times per blink
            if (blinkDetector.IsBlinking && !blinkConsumed)
            {
                currentDoor.Interact();
                blinkConsumed = true;
            }

            if (!blinkDetector.IsBlinking)
                blinkConsumed = false;
        }
        else
        {
            blinkConsumed = false;
        }
    }

    // Creates the interact prompt UI at runtime so no manual Canvas setup is needed.
    private void BuildPromptUI()
    {
        GameObject canvasObj = new GameObject("DoorPromptCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject textObj = new GameObject("DoorPromptText");
        textObj.transform.SetParent(canvas.transform, false);

        uiPrompt = textObj.AddComponent<Text>();
        uiPrompt.text = doorPromptText;
        uiPrompt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiPrompt.fontSize = 26;
        uiPrompt.color = Color.white;
        uiPrompt.alignment = TextAnchor.MiddleCenter;

        RectTransform rect = uiPrompt.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.25f);
        rect.anchorMax = new Vector2(0.5f, 0.25f);
        rect.sizeDelta = new Vector2(500, 50);
        rect.anchoredPosition = Vector2.zero;

        uiPrompt.gameObject.SetActive(false);
    }
}
