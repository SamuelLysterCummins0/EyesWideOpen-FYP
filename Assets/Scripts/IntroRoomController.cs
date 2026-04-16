using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Central state machine for the Security Entry intro room sequence.
///
/// NARRATIVE CONTEXT
/// The player arrives in a corporate security processing suite — the "Personnel Entry" room
/// required before entering the facility below. A dead guard is sprawled on the floor;
/// their access badge (the audiotape) sits beside them. The CERBERUS biometric terminal
/// (tablet) must be used first to register the player's face to the facility system,
/// which diegetically explains why head-tracking only activates at this point.
///
/// STATE FLOW
///   AwaitingFaceScan   → player must use the CERBERUS tablet (E key) to run face calibration
///   AwaitingAudiotape  → gaze cursor is now live; player must retrieve the guard's access badge
///   ReadyToExit        → door can be opened with gaze + blink; player drops to the dungeon
///
/// SETUP (prefab root)
///   • Attach this script to the IntroRoom prefab root.
///   • GazeDetector and GazeCalibration are found automatically at runtime — no prefab wiring needed.
///   • On GazeDetector in the scene, set "Start Visible = false" in the Inspector so the
///     cursor is hidden before face-scan completes (this script shows it on completion).
/// </summary>
public class IntroRoomController : MonoBehaviour
{
    public static IntroRoomController Instance { get; private set; }

    public enum IntroState
    {
        AwaitingFaceScan,
        AwaitingAudiotape,
        ReadyToExit
    }

    [Header("Events")]
    public UnityEvent OnFaceScanComplete  = new UnityEvent();
    public UnityEvent OnAudiotapeCollected = new UnityEvent();

    public IntroState CurrentState { get; private set; } = IntroState.AwaitingFaceScan;

    // ── Cached scene references ────────────────────────────────────────────────
    private GazeDetector    gazeDetector;
    private GazeCalibration gazeCalibration;

    // ── Door message lookup ────────────────────────────────────────────────────
    private static readonly string[] DoorMessages =
    {
        "COMPLETE FACE SCAN",        // AwaitingFaceScan
        "COLLECT AUDIO TAPE",        // AwaitingAudiotape
        "",                          // ReadyToExit — door opens, no text
    };

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Locate scene-level gaze systems (they live outside the intro room prefab)
        gazeDetector    = FindObjectOfType<GazeDetector>();
        gazeCalibration = FindObjectOfType<GazeCalibration>();

        // Gaze starts inactive — GazeDetector.Start() already hides the cursor.
        // SetGazeActive(false) is not needed here; the default is already false.

        if (gazeCalibration != null)
            gazeCalibration.OnCalibrationComplete.AddListener(HandleFaceScanCompleted);
        else
            Debug.LogWarning("[IntroRoomController] GazeCalibration not found in scene.");
    }

    private void OnDestroy()
    {
        if (gazeCalibration != null)
            gazeCalibration.OnCalibrationComplete.RemoveListener(HandleFaceScanCompleted);

        if (Instance == this)
            Instance = null;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Called by TabletInteraction once the GazeCalibration sequence finishes.</summary>
    public void HandleFaceScanCompleted()
    {
        if (CurrentState != IntroState.AwaitingFaceScan) return;

        CurrentState = IntroState.AwaitingAudiotape;
        // Enable full gaze — this activates IsTracking for all gameplay scripts
        // and shows the cursor dot.
        gazeDetector?.SetGazeActive(true);

        OnFaceScanComplete?.Invoke();
        Debug.Log("[IntroRoom] Face scan complete — gaze active.");
    }

    /// <summary>Called by IntroAudiotapePickup when the player picks up the access badge.</summary>
    public void OnAudiotapePickedUp()
    {
        if (CurrentState != IntroState.AwaitingAudiotape) return;

        CurrentState = IntroState.ReadyToExit;
        OnAudiotapeCollected?.Invoke();
        Debug.Log("[IntroRoom] Access badge collected — exit door unlocked.");
    }

    /// <summary>Returns the context-appropriate door message for the current state.</summary>
    public string GetDoorMessage() => DoorMessages[(int)CurrentState];

    /// <summary>True only when the player has satisfied all conditions to exit.</summary>
    public bool IsDoorUnlocked() => CurrentState == IntroState.ReadyToExit;
}
