using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Self-contained elevator door controller.
/// Drag the two door GameObjects into the Inspector.
/// Each door slides along its own local X axis (positive = slides right,
/// negative = slides left).  Set SlideDistance to how far each door travels
/// to fully open.  The script figures out the rest.
///
/// Place this component on any GameObject near the doors (e.g. an empty
/// "ElevatorDoors" object between them).
/// </summary>
public class ElevatorDoors : MonoBehaviour
{
    [Header("Door Objects")]
    [Tooltip("The left elevator door GameObject.")]
    [SerializeField] private GameObject leftDoor;
    [Tooltip("The right elevator door GameObject.")]
    [SerializeField] private GameObject rightDoor;

    [Header("Slide Settings")]
    [Tooltip("How many units each door slides to open fully.")]
    [SerializeField] private float slideDistance = 1.2f;
    [Tooltip("How fast the doors slide (units per second).")]
    [SerializeField] private float slideSpeed    = 2.0f;

    [Header("Interaction")]
    [Tooltip("How close the player must be before the prompt appears.")]
    [SerializeField] private float interactionRadius = 2f;
    [SerializeField] private KeyCode interactKey     = KeyCode.E;

    // ── Runtime ──────────────────────────────────────────────────────────────────
    private Transform player;
    private bool      doorsOpen = false;
    private bool      moving    = false;

    // Recorded closed positions (set on Start so they're correct regardless of
    // where the prefab is placed in the world).
    private Vector3 leftClosed;
    private Vector3 rightClosed;

    // UI
    private GameObject promptCanvas;
    private Text       promptText;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────────

    private void Start()
    {
        player = GameObject.FindWithTag("Player")?.transform;

        if (leftDoor  != null) leftClosed  = leftDoor.transform.localPosition;
        if (rightDoor != null) rightClosed = rightDoor.transform.localPosition;

        BuildPromptUI();
    }

    private void Update()
    {
        if (doorsOpen || moving || player == null) return;

        bool inRange = Vector3.Distance(player.position, transform.position) <= interactionRadius;
        SetPromptVisible(inRange);

        if (inRange && Input.GetKeyDown(interactKey))
        {
            SetPromptVisible(false);
            StartCoroutine(SlideDoors());
        }
    }

    // ── Door animation ────────────────────────────────────────────────────────────

    /// <summary>Called by IntroCinematicController via Timeline signal.</summary>
    public void OpenForCinematic()
    {
        if (!moving && !doorsOpen)
            StartCoroutine(SlideDoors());
    }

    private IEnumerator SlideDoors()
    {
        moving = true;

        // Left door slides left (negative local X), right door slides right (positive local X)
        Vector3 leftTarget  = leftClosed  + leftDoor.transform.parent.InverseTransformDirection( leftDoor.transform.right * -slideDistance);
        Vector3 rightTarget = rightClosed + rightDoor.transform.parent.InverseTransformDirection(rightDoor.transform.right *  slideDistance);

        // If the doors have no parent, fall back to world-space offsets
        if (leftDoor.transform.parent  == null) leftTarget  = leftClosed  + leftDoor.transform.right  * -slideDistance;
        if (rightDoor.transform.parent == null) rightTarget = rightClosed + rightDoor.transform.right *  slideDistance;

        float elapsed  = 0f;
        float duration = slideDistance / slideSpeed;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / duration);
            float s  = Mathf.SmoothStep(0f, 1f, t);   // ease in/out

            if (leftDoor  != null) leftDoor.transform.localPosition  = Vector3.Lerp(leftClosed,  leftTarget,  s);
            if (rightDoor != null) rightDoor.transform.localPosition = Vector3.Lerp(rightClosed, rightTarget, s);

            yield return null;
        }

        // Snap to final positions
        if (leftDoor  != null) leftDoor.transform.localPosition  = leftTarget;
        if (rightDoor != null) rightDoor.transform.localPosition = rightTarget;

        moving    = false;
        doorsOpen = true;
    }

    // ── Prompt UI ─────────────────────────────────────────────────────────────────

    private void BuildPromptUI()
    {
        promptCanvas = new GameObject("ElevatorDoorsPromptCanvas");
        Canvas canvas = promptCanvas.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        promptCanvas.AddComponent<CanvasScaler>();
        promptCanvas.AddComponent<GraphicRaycaster>();

        GameObject textObj = new GameObject("ElevatorDoorsPromptText");
        textObj.transform.SetParent(promptCanvas.transform, false);

        promptText           = textObj.AddComponent<Text>();
        promptText.text      = "[E]  Open doors";
        promptText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        promptText.fontSize  = 28;
        promptText.color     = Color.white;
        promptText.alignment = TextAnchor.MiddleCenter;

        RectTransform rect    = promptText.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.5f, 0.25f);
        rect.anchorMax        = new Vector2(0.5f, 0.25f);
        rect.sizeDelta        = new Vector2(400f, 50f);
        rect.anchoredPosition = Vector2.zero;

        SetPromptVisible(false);
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptCanvas != null) promptCanvas.SetActive(visible);
    }

    private void OnDestroy()
    {
        if (promptCanvas != null) Destroy(promptCanvas);
    }

    // Shows the interaction zone in the Scene view
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, interactionRadius);
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}
