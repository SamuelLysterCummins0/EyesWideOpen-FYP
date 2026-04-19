using UnityEngine;

/// <summary>
/// Debug Spectator / Free-Cam mode.
///
/// Press [F8] (configurable) to detach the camera from the player and fly freely
/// through the level — no-clip through all geometry. Press again to snap back.
///
/// Controls while active:
///   W / S            — fly forward / back
///   A / D            — strafe left / right
///   E / Q            — fly up / down
///   Scroll Wheel     — fly up / down (alternative)
///   Left Shift       — hold for 3× speed boost
///   Mouse            — look around
///   F8               — exit spectator mode and return to player
///   F9               — teleport player to spectator position, then exit
///
/// All movement uses Time.unscaledDeltaTime so it works even while the game
/// is frozen (Time.timeScale == 0).
///
/// Setup:
///   Attach to any persistent scene GameObject (e.g. the GameManager or Player).
///   All references are found automatically — no Inspector wiring needed.
///
/// Note: To strip this from release builds, wrap the class body in:
///   #if UNITY_EDITOR || DEVELOPMENT_BUILD  ...  #endif
/// </summary>
public class DebugSpectatorCamera : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Toggle")]
    [Tooltip("Key that enters / exits spectator mode.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F8;

    [Tooltip("Key that teleports the player to the spectator camera's current position and exits spectator mode.")]
    [SerializeField] private KeyCode teleportPlayerKey = KeyCode.F9;

    [Tooltip("Freeze game time while spectating? Recommended — stops NPCs, physics, etc.")]
    [SerializeField] private bool freezeTime = true;

    [Header("Movement")]
    [SerializeField] private float moveSpeed      = 12f;
    [SerializeField] private float fastMoveSpeed  = 36f;   // held while Shift
    [SerializeField] private float mouseSensitivity = 2.5f;

    // ── Private State ──────────────────────────────────────────────────────────

    private bool isSpectating = false;

    // Camera restore data
    private Camera        _cam;
    private Transform     _camOriginalParent;
    private Vector3       _camOriginalLocalPos;
    private Quaternion    _camOriginalLocalRot;

    // Free-look euler angles (maintained separately to avoid gimbal lock)
    private float _yaw;
    private float _pitch;

    // Cached references (found once in Start)
    private CameraControl _cameraControl;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Start()
    {
        _cam          = Camera.main;
        _cameraControl = FindObjectOfType<CameraControl>();
    }

    private void Update()
    {
        // Toggle on key press — guard against pressing while death screen is up,
        // since we don't want to break the game-over flow.
        if (Input.GetKeyDown(toggleKey))
        {
            bool deathActive = GameManager.Instance != null && GameManager.Instance.IsDeathScreenActive();
            if (!deathActive)
            {
                if (isSpectating) ExitSpectator();
                else              EnterSpectator();
            }
        }

        if (isSpectating)
        {
            HandleFreeCam();

            // Teleport the player to wherever the spectator camera currently is,
            // then drop out of spectator mode so control returns immediately.
            if (Input.GetKeyDown(teleportPlayerKey))
                TeleportPlayerToCamera();
        }
    }

    private void OnDestroy()
    {
        // Safety net: if this component is destroyed while spectating,
        // restore time so the game doesn't stay frozen.
        if (isSpectating && freezeTime)
            Time.timeScale = 1f;
    }

    // ── Spectator Enter / Exit ─────────────────────────────────────────────────

    private void EnterSpectator()
    {
        if (_cam == null)
        {
            Debug.LogWarning("[DebugSpectator] Camera.main not found — cannot enter spectator mode.");
            return;
        }

        isSpectating = true;

        // ── 1. Capture camera's current world transform so we can restore it ──
        _camOriginalParent   = _cam.transform.parent;
        _camOriginalLocalPos = _cam.transform.localPosition;
        _camOriginalLocalRot = _cam.transform.localRotation;

        // Detach from player — camera is now a free-floating world object
        _cam.transform.SetParent(null, worldPositionStays: true);

        // Seed free-look angles from the camera's current world rotation so there
        // is no snap when we first start controlling it
        Vector3 euler = _cam.transform.eulerAngles;
        _yaw   = euler.y;
        _pitch = euler.x > 180f ? euler.x - 360f : euler.x; // normalise to -180…180

        // ── 2. Disable systems that would fight the free cam ──────────────────
        if (_cameraControl != null)
            _cameraControl.enabled = false;

        // Disable player movement so WASD isn't consumed by both systems
        if (GameManager.Instance?.playerController != null)
            GameManager.Instance.playerController.enabled = false;

        // ── 3. Freeze game time (optional) ────────────────────────────────────
        if (freezeTime)
            Time.timeScale = 0f;

        Debug.Log("[DebugSpectator] Spectator mode ON. Press F8 to return.");
    }

    private void ExitSpectator()
    {
        isSpectating = false;

        // ── 1. Restore time first so physics/controllers re-activate cleanly ──
        if (freezeTime)
            Time.timeScale = 1f;

        // ── 2. Return camera to its original parent and local transform ────────
        if (_cam != null)
        {
            _cam.transform.SetParent(_camOriginalParent, worldPositionStays: false);
            _cam.transform.localPosition = _camOriginalLocalPos;
            _cam.transform.localRotation = _camOriginalLocalRot;
        }

        // ── 3. Re-enable systems ───────────────────────────────────────────────
        if (_cameraControl != null)
        {
            _cameraControl.enabled = true;
            // Tell CameraControl where the camera is now so its internal
            // "original transform" reference matches the restored position.
            _cameraControl.UpdateOriginalTransform();
        }

        if (GameManager.Instance?.playerController != null)
            GameManager.Instance.playerController.enabled = true;

        Debug.Log("[DebugSpectator] Spectator mode OFF. Back to player.");
    }

    // ── Teleport Player To Camera ──────────────────────────────────────────────

    private void TeleportPlayerToCamera()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[DebugSpectator] GameManager.Instance is null — cannot teleport player.");
            return;
        }

        // The camera's current world position is exactly where we want the player.
        // We drop them slightly below the camera so the feet land at that point
        // rather than the eye-level position (the camera sits at roughly eye height).
        Vector3 destination = _cam != null ? _cam.transform.position : Vector3.zero;
        destination.y -= 1.6f; // approximate eye-height offset so feet land at cam position

        Debug.Log($"[DebugSpectator] Teleporting player to spectator position {destination}.");

        // Exit spectator first — this re-parents the camera and re-enables systems —
        // then immediately teleport the player to override the restored spawn position.
        ExitSpectator();
        GameManager.Instance.TeleportPlayer(destination);

        // Sync CameraControl's reference point to the new position
        _cameraControl?.UpdateOriginalTransform();
    }

    // ── Free-Cam Input ─────────────────────────────────────────────────────────

    private void HandleFreeCam()
    {
        if (_cam == null) return;

        // ── Mouse Look ───────────────────────────────────────────────────────
        // GetAxisRaw is used so smoothing doesn't interfere when timeScale == 0.
        // Cursor stays locked — WASD feel is preserved.
        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        _yaw   += mouseX;
        _pitch -= mouseY;
        _pitch  = Mathf.Clamp(_pitch, -89f, 89f); // prevent flipping at poles

        _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        // ── Translation ──────────────────────────────────────────────────────
        float speed = Input.GetKey(KeyCode.LeftShift) ? fastMoveSpeed : moveSpeed;
        float dt    = Time.unscaledDeltaTime; // works while timeScale == 0

        Vector3 dir = Vector3.zero;

        // Forward / back along camera's look direction (ignores Y so W/S stay horizontal-ish)
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            dir += _cam.transform.forward;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            dir -= _cam.transform.forward;

        // Strafe
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            dir += _cam.transform.right;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            dir -= _cam.transform.right;

        // Vertical — E up, Q down, scroll wheel as alternative
        if (Input.GetKey(KeyCode.E))
            dir += Vector3.up;
        if (Input.GetKey(KeyCode.Q))
            dir -= Vector3.up;

        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
            dir += Vector3.up * Mathf.Sign(scroll);

        // Normalise only the directional part so diagonals aren't faster,
        // but let the scroll impulse keep its raw magnitude for responsive feel.
        _cam.transform.position += dir.normalized * speed * dt;
    }

    // ── On-Screen Label ────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!isSpectating) return;

        // Simple overlay — no Canvas needed
        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize  = 16,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = new Color(1f, 0.85f, 0.2f);

        GUI.Box(new Rect(Screen.width * 0.5f - 200f, 12f, 400f, 32f),
                $"  SPECTATOR MODE  |  {toggleKey} = return  |  {teleportPlayerKey} = teleport here  ",
                style);

        // Mini help text
        GUIStyle small = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            alignment = TextAnchor.MiddleCenter
        };
        small.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 0.85f);
        GUI.Label(new Rect(Screen.width * 0.5f - 240f, 46f, 480f, 20f),
                  "WASD = move   E/Q = up/down   Shift = fast   Mouse = look",
                  small);
    }
}
