using UnityEngine;
using TMPro;
using SUPERCharacter;

/// <summary>
/// The CERBERUS Biometric Entry Terminal — a tablet mounted in the intro room.
///
/// NARRATIVE CONTEXT
/// "CERBERUS Systems requires facial verification before granting facility access.
///  Look into the scanner and hold still."
/// This is the diegetic wrapper around GazeCalibration.StartCalibration().
/// The player presses E to initiate; the calibration runs its 9-point sequence;
/// once complete IntroRoomController advances the state and the gaze cursor appears.
///
/// SETUP (prefab child)
///   • Attach to the tablet GameObject inside the IntroRoom prefab.
///   • Optionally assign a TextMeshProUGUI on the tablet's screen mesh to TabletScreenText.
///   • GazeCalibration is found automatically — no wiring needed.
/// </summary>
public class TabletInteraction : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float interactionRadius = 2.5f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Optional — tablet screen TMP text")]
    [Tooltip("Assign a TextMeshProUGUI on the physical tablet mesh to show status text.")]
    [SerializeField] private TextMeshProUGUI tabletScreenText;

    // ── Screen copy ────────────────────────────────────────────────────────────
    private const string IDLE_TEXT =
        "<size=60%>CERBERUS SYSTEMS  v4.2\n" +
        "BIOMETRIC ENTRY TERMINAL</size>\n\n" +
        "Facility access requires facial\n" +
        "verification. Unauthorised entry\n" +
        "triggers immediate lockdown.\n\n" +
        "<color=#00FF88>[ PRESS  E  TO  SCAN ]</color>";

    private const string SCANNING_TEXT =
        "<size=60%>CERBERUS SYSTEMS  v4.2\n" +
        "BIOMETRIC ENTRY TERMINAL</size>\n\n" +
        "<color=#FFD700>SCANNING...</color>\n\n" +
        "Look at each target dot\n" +
        "and press SPACEBAR when ready.";

    private const string COMPLETE_TEXT =
        "<size=60%>CERBERUS SYSTEMS  v4.2\n" +
        "BIOMETRIC ENTRY TERMINAL</size>\n\n" +
        "<color=#00FF88>SCAN COMPLETE</color>\n\n" +
        "Facial profile registered.\n" +
        "Head-tracking active.";

    // ── Runtime ────────────────────────────────────────────────────────────────
    private Transform       player;
    private GazeCalibration gazeCalibration;
    private bool            scanDone = false;
    private SUPERCharacterAIO playerController;

    // World-space "Press E" prompt floating above the tablet
    private GameObject        promptRoot;
    private TextMeshProUGUI   promptText;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Start()
    {
        player           = GameObject.FindWithTag("Player")?.transform;
        gazeCalibration  = FindObjectOfType<GazeCalibration>();
        playerController = player?.GetComponent<SUPERCharacterAIO>();

        if (tabletScreenText != null)
            tabletScreenText.text = IDLE_TEXT;

        BuildPromptUI();
        SetPromptVisible(false);
    }

    private void Update()
    {
        if (scanDone || player == null) return;

        bool inRange = Vector3.Distance(player.position, transform.position) <= interactionRadius;
        SetPromptVisible(inRange);

        if (inRange && Input.GetKeyDown(interactKey))
            BeginScan();
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private void BeginScan()
    {
        if (gazeCalibration == null)
        {
            Debug.LogError("[TabletInteraction] GazeCalibration not found in scene!");
            return;
        }

        SetPromptVisible(false);

        if (tabletScreenText != null)
            tabletScreenText.text = SCANNING_TEXT;

        // Freeze movement so the player holds still during the face scan
        if (playerController != null)
            playerController.enabled = false;

        gazeCalibration.OnCalibrationComplete.AddListener(OnScanFinished);
        gazeCalibration.StartCalibration();
    }

    private void OnScanFinished()
    {
        scanDone = true;
        gazeCalibration.OnCalibrationComplete.RemoveListener(OnScanFinished);

        // Unfreeze movement now that the scan is done
        if (playerController != null)
            playerController.enabled = true;

        if (tabletScreenText != null)
            tabletScreenText.text = COMPLETE_TEXT;
    }

    // ── World-space prompt UI ──────────────────────────────────────────────────

    private void BuildPromptUI()
    {
        promptRoot = new GameObject("TabletPrompt");
        promptRoot.transform.SetParent(transform, false);
        // Float 1.4 m above the tablet pivot so it clears most mesh geometry.
        // Rotate 180° on Y so the canvas faces toward the player (world-space canvases
        // render text toward their local +Z; flipping makes it face the opposite
        // direction from the tablet's forward, which is where the player stands).
        promptRoot.transform.localPosition    = new Vector3(0f, 1.4f, 0f);
        promptRoot.transform.localEulerAngles = new Vector3(0f, 180f, 0f);

        Canvas canvas = promptRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        RectTransform cr = promptRoot.GetComponent<RectTransform>();
        cr.sizeDelta   = new Vector2(400f, 60f);
        cr.localScale  = Vector3.one * 0.004f;

        promptRoot.AddComponent<UnityEngine.UI.CanvasScaler>();

        GameObject textGO = new GameObject("Label");
        textGO.transform.SetParent(promptRoot.transform, false);

        promptText = textGO.AddComponent<TextMeshProUGUI>();
        promptText.text      = "[E]  Access Biometric Terminal";
        promptText.fontSize   = 36f;
        promptText.alignment  = TextAlignmentOptions.Center;
        promptText.color      = new Color(0.15f, 1f, 0.5f);

        RectTransform tr = textGO.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.sizeDelta = Vector2.zero;
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptRoot != null)
            promptRoot.SetActive(visible);
    }
}
