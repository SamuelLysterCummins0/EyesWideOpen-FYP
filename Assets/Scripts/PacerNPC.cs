using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The Pacer — wandering NPC with vision-gated detection, FSM state classes,
/// and intelligent search. No idle stops or alert animation.
///
/// States
/// ──────
///   Wander  → Randomly explores the level via NavMesh. Brief pause between each leg.
///             Transitions to Chase the moment the player enters the FOV cone with LOS.
///   Chase   → Full-speed pursuit. Requires FOV cone + clear LOS.
///             Tracks last-known position. Sustained LOS loss → Search.
///   Search  → Walk to last-known position, look around, fan out. → Chase or Wander.
///   Stunned → Frozen by flashlight. Resumes interrupted state after duration + immunity.
///
/// ─── ANIMATION SETUP ─────────────────────────────────────────────────────────────────
/// 1. Open Window → Animation → Animator.
/// 2. Click the Parameters tab (left side) → + → Float → name it "Speed".
/// 3. Right-click the graph → Create State → From New Blend Tree.
/// 4. Double-click the Blend Tree state to enter it, then click the Blend Tree node.
///    Inspector shows: Blend Type = 1D, Parameter = Speed.
///    Uncheck "Automate Thresholds" then add 3 motions:
///      Threshold 0               → Idle animation clip
///      Threshold = wanderSpeed   → Walk animation clip   (default 2.0)
///      Threshold = chaseSpeed    → Run  animation clip   (default 3.5)
///    The thresholds MUST match the wanderSpeed and chaseSpeed values below.
/// 5. Assign the Animator Controller to the Animator on the NPC's child mesh object.
///
/// ─── AUDIO SETUP ─────────────────────────────────────────────────────────────────────
/// Assign clips in the Inspector:
///   ambientLoop  — Looped breathing / hum. Separate audio source, never interrupted.
///   alertClip    — One-shot played the moment the NPC first spots the player.
///   footstepClip — One-shot triggered at speed-scaled intervals while moving.
///   stunClip     — Played when hit by the flashlight.
///   recoveryClip — Played when recovering from stun.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class PacerNPC : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Movement — per level (index = level number)")]
    [SerializeField] private float[] wanderSpeeds = { 2.0f, 2.2f, 2.4f, 2.8f };
    [SerializeField] private float[] chaseSpeeds  = { 3.5f, 3.8f, 4.2f, 5.0f };

    [Header("Vision")]
    [Tooltip("Total horizontal FOV cone in degrees (e.g. 90 = ±45° from forward).")]
    [SerializeField] private float fieldOfViewAngle  = 90f;
    [Tooltip("Maximum distance at which the NPC can spot the player inside the FOV cone.")]
    [SerializeField] private float detectionRange    = 10f;
    [Tooltip("Height of the NPC's eye position for LOS raycasts.")]
    [SerializeField] private float npcEyeHeight      = 1.6f;
    [Tooltip("Height of the player centre used as the LOS raycast target.")]
    [SerializeField] private float playerEyeHeight   = 1.0f;
    [Tooltip("Layer mask for LOS obstacle raycasts (walls, furniture, etc.).")]
    [SerializeField] private LayerMask obstacleLayerMask = ~0;

    [Header("Chase / Lose")]
    [Tooltip("Seconds of sustained LOS loss before the NPC gives up and enters Search.")]
    [SerializeField] private float losLostThreshold = 3.5f;
    [Tooltip("During active chase the NPC drops the FOV cone requirement and uses this " +
             "larger range — so running past the NPC side-on doesn't instantly break chase.")]
    [SerializeField] private float chaseTrackRange  = 16f;
    [Tooltip("Distance at which the NPC catches the player and calls PlayerDied.")]
    [SerializeField] private float catchDistance    = 1.2f;

    [Header("Wander")]
    [Tooltip("Radius around the NPC used to pick random wander destinations.")]
    [SerializeField] private float wanderRadius   = 18f;
    [Tooltip("Minimum pause between wander legs (seconds).")]
    [SerializeField] private float wanderPauseMin = 0.5f;
    [Tooltip("Maximum pause between wander legs (seconds).")]
    [SerializeField] private float wanderPauseMax = 1.5f;

    [Header("Search")]
    [Tooltip("Radius around the last-known position used to pick search points.")]
    [SerializeField] private float searchRadius     = 8f;
    [Tooltip("Number of search points (first is always the last-known position).")]
    [SerializeField] private int   searchPointCount = 4;
    [Tooltip("Total time allowed for the entire search before giving up.")]
    [SerializeField] private float maxSearchTime    = 15f;

    [Header("Siren")]
    [SerializeField] private float sirenSpeedMultiplier = 1.5f;

    [Header("Audio")]
    [Tooltip("Looping ambient clip. Plays on a separate source so one-shots never cut it.")]
    [SerializeField] private AudioClip ambientLoop;
    [Tooltip("One-shot played the moment the NPC first spots the player.")]
    [SerializeField] private AudioClip alertClip;

    [Header("Audio — Footsteps (Walk)")]
    [Tooltip("First walk footstep clip (left foot).")]
    [SerializeField] private AudioClip walkFootstepClip1;
    [Tooltip("Second walk footstep clip (right foot).")]
    [SerializeField] private AudioClip walkFootstepClip2;
    [Tooltip("Volume of walk footsteps (0–1).")]
    [Range(0f, 1f)]
    [SerializeField] private float walkFootstepVolume   = 0.4f;
    [Tooltip("Time between walk footstep sounds (seconds).")]
    [SerializeField] private float walkFootstepInterval = 0.5f;

    [Header("Audio — Footsteps (Run)")]
    [Tooltip("First run footstep clip (left foot).")]
    [SerializeField] private AudioClip runFootstepClip1;
    [Tooltip("Second run footstep clip (right foot).")]
    [SerializeField] private AudioClip runFootstepClip2;
    [Tooltip("Volume of run footsteps (0–1).")]
    [Range(0f, 1f)]
    [SerializeField] private float runFootstepVolume    = 0.6f;
    [Tooltip("Time between run footstep sounds (seconds).")]
    [SerializeField] private float runFootstepInterval  = 0.32f;

    [SerializeField] private AudioClip stunClip;
    [SerializeField] private AudioClip recoveryClip;


    [Header("Animation")]
    [Tooltip("Name of the Speed float parameter in the Animator Controller. Must match exactly.")]
    [SerializeField] private string speedParam = "Speed";

    // ── Components ─────────────────────────────────────────────────────────────

    private NavMeshAgent   _agent;
    private AudioSource    _sfxSource;
    private AudioSource    _ambientSource;
    private Animator       _animator;
    private Transform      _player;
    private GameManager    _gameManager;
    private CameraControl  _cameraControl;
    private PacerJumpscare _pacerJumpscare;

    // ── FSM ────────────────────────────────────────────────────────────────────

    private PacerStateBase _currentState;
    private PacerStateBase _previousState;

    private PacerWanderState  _wanderState;
    private PacerChaseState   _chaseState;
    private PacerSearchState  _searchState;
    private PacerStunnedState _stunnedState;

    // ── Shared runtime ─────────────────────────────────────────────────────────

    private int     _level        = 0;
    private bool    _sirenActive  = false;
    private bool    _immune       = false;
    private bool    _playerCaught = false;
    private float   _footstepTimer   = 0f;
    private bool    _footstepToggle  = false; // alternates between clip 1 and 2
    private bool    _wasRunning      = false;
    private Vector3 _lastKnownPosition;

    // Animation — target is set by states; Update applies it every frame with damping
    // so the blend tree actually receives a smoothly changing value.
    private float _targetAnimSpeed = 0f;

    // ── Speed helpers ──────────────────────────────────────────────────────────

    private float WanderSpeed => _level < wanderSpeeds.Length ? wanderSpeeds[_level] : wanderSpeeds[^1];
    private float ChaseSpeed  => _level < chaseSpeeds.Length  ? chaseSpeeds[_level]  : chaseSpeeds[^1];
    private float ApplySiren(float spd) => _sirenActive ? spd * sirenSpeedMultiplier : spd;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent    = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();

        _sfxSource              = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _sfxSource.spatialBlend = 1f;
        _sfxSource.maxDistance  = 20f;
        _sfxSource.rolloffMode  = AudioRolloffMode.Linear;
        _sfxSource.playOnAwake  = false;

        // Add a non-trigger capsule so the player can't walk through the NPC.
        // Only added if no collider already exists on this GameObject.
        if (GetComponent<Collider>() == null)
        {
            CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
            col.isTrigger = false;
            col.radius    = 0.4f;
            col.height    = 1.8f;
            col.center    = new Vector3(0f, 0.9f, 0f);
        }

        if (ambientLoop != null)
        {
            _ambientSource              = gameObject.AddComponent<AudioSource>();
            _ambientSource.clip         = ambientLoop;
            _ambientSource.loop         = true;
            _ambientSource.spatialBlend = 1f;
            _ambientSource.maxDistance  = 15f;
            _ambientSource.rolloffMode  = AudioRolloffMode.Linear;
            _ambientSource.volume       = 0.4f;
            _ambientSource.playOnAwake  = false;
        }
    }

    private void Start()
    {
        _gameManager    = GameManager.Instance;
        _player         = GameObject.FindGameObjectWithTag("Player")?.transform;
        _cameraControl  = FindObjectOfType<CameraControl>();
        _pacerJumpscare = FindObjectOfType<PacerJumpscare>();
        _level          = _gameManager != null ? _gameManager.GetCurrentLevel() : 0;
        _playerCaught   = false;

        SirenPhaseManager.Instance?.RegisterPacer(this);
        _ambientSource?.Play();

        // Exclude the player's own layer from the obstacle mask so the LOS raycast
        // in CanSeePlayer() and CanTrackPlayerInChase() doesn't hit the player's
        // CharacterController capsule and falsely report the sightline as blocked.
        if (_player != null)
            obstacleLayerMask &= ~(1 << _player.gameObject.layer);

        _wanderState  = new PacerWanderState(this);
        _chaseState   = new PacerChaseState(this);
        _searchState  = new PacerSearchState(this);
        _stunnedState = new PacerStunnedState(this);

        ChangeState(_wanderState);
    }

    private void OnDestroy()
    {
        SirenPhaseManager.Instance?.DeregisterPacer(this);
    }

    private void Update()
    {
        if (_playerCaught || _player == null) return;

        // Drive the animator every frame so damping actually interpolates smoothly.
        // States write to _targetAnimSpeed; this pushes it to the Animator each frame.
        if (_animator != null && !string.IsNullOrEmpty(speedParam))
            _animator.SetFloat(speedParam, _targetAnimSpeed, 0.12f, Time.deltaTime);

        HandleFootsteps();
        _currentState?.OnUpdate();
    }

    // ── FSM ────────────────────────────────────────────────────────────────────

    private void ChangeState(PacerStateBase newState)
    {
        _currentState?.OnExit();
        _previousState = _currentState;
        _currentState  = newState;
        _currentState?.OnEnter();
    }

    private void GoToWander()  => ChangeState(_wanderState);
    private void GoToChase()   => ChangeState(_chaseState);
    private void GoToSearch()  => ChangeState(_searchState);
    private void GoToStunned() => ChangeState(_stunnedState);

    private void ResumePrevious()
    {
        if      (_previousState is PacerChaseState)  GoToChase();
        else if (_previousState is PacerSearchState) GoToSearch();
        else                                          GoToWander();
    }

    // ── Perception ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the NPC must treat the player as completely invisible.
    /// Covers three cases:
    ///   • Player is inside any safe/spawn/computer room (open or closed door) —
    ///     the room floor is off the main NavMesh so the NPC can't reach them anyway;
    ///     breaking LOS immediately prevents the "running in place at the door" bug.
    ///   • Player is inside a closed room (doors shut) — full protection.
    ///   • Player is hiding in a locker.
    /// </summary>
    private bool IsPlayerProtected()
        => PlayerSafeZone.IsPlayerInRoom
        || PlayerSafeZone.IsPlayerProtected
        || LockerInteraction.IsHidingInLocker;

    /// <summary>
    /// Standard detection: player must be within range, inside the FOV cone, and
    /// not occluded. Used by Wander to initially spot the player.
    /// </summary>
    private bool CanSeePlayer()
    {
        if (_player == null) return false;
        if (IsPlayerProtected()) return false;

        Vector3 toPlayer = _player.position - transform.position;
        if (toPlayer.magnitude > detectionRange) return false;

        if (Vector3.Angle(transform.forward, toPlayer.normalized) > fieldOfViewAngle * 0.5f)
            return false;

        Vector3 eye    = transform.position + Vector3.up * npcEyeHeight;
        Vector3 target = _player.position   + Vector3.up * playerEyeHeight;
        Vector3 dir    = target - eye;

        return !Physics.Raycast(eye, dir.normalized, dir.magnitude, obstacleLayerMask);
    }

    /// <summary>
    /// Relaxed chase check: drops the FOV cone requirement and uses the wider
    /// chaseTrackRange. This stops the NPC instantly losing track just because
    /// the player runs past it to one side — it only gives up when the player
    /// is genuinely behind a wall or has sprinted far out of range.
    /// </summary>
    private bool CanTrackPlayerInChase()
    {
        if (_player == null) return false;
        if (IsPlayerProtected()) return false;

        Vector3 toPlayer = _player.position - transform.position;
        if (toPlayer.magnitude > chaseTrackRange) return false;

        Vector3 eye    = transform.position + Vector3.up * npcEyeHeight;
        Vector3 target = _player.position   + Vector3.up * playerEyeHeight;
        Vector3 dir    = target - eye;

        return !Physics.Raycast(eye, dir.normalized, dir.magnitude, obstacleLayerMask);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────

    private void StopAgent()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.velocity  = Vector3.zero;
        }
    }

    private void ResumeAgent()
    {
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = false;
    }

    private void SetNavDestination(Vector3 pos)
    {
        if (_agent == null || !_agent.isOnNavMesh) return;
        _agent.isStopped = false;
        _agent.SetDestination(pos);
    }

    private bool HasReachedDestination()
    {
        if (_agent == null || _agent.pathPending) return false;
        return !_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.2f;
    }

    private bool TrySampleNavMesh(Vector3 origin, float radius, out Vector3 result)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(origin, out hit, radius, NavMesh.AllAreas))
        {
            result = hit.position;
            return true;
        }
        result = origin;
        return false;
    }

    private void SetAgentSpeed(float speed)
    {
        if (_agent != null) _agent.speed = ApplySiren(speed);
    }

    /// <summary>
    /// Sets the target animation speed. The damped SetFloat call in Update()
    /// pushes this to the Animator every frame so the blend tree interpolates smoothly.
    /// </summary>
    private void SetAnimSpeed(float speed) => _targetAnimSpeed = speed;

    /// <summary>Instantly snaps the animator to 0 — used on stun so the NPC freezes immediately.</summary>
    private void SnapAnimToZero()
    {
        _targetAnimSpeed = 0f;
        if (_animator != null && !string.IsNullOrEmpty(speedParam))
            _animator.SetFloat(speedParam, 0f);
    }

    private void PlaySFX(AudioClip clip, float volume = 1f)
    {
        if (clip != null && _sfxSource != null)
            _sfxSource.PlayOneShot(clip, volume);
    }

    private Vector3[] GenerateSearchPoints(Vector3 center, int count, float radius)
    {
        var points    = new Vector3[count];
        points[0]     = center;
        int   remaining  = Mathf.Max(1, count - 1);
        float angleStep  = 360f / remaining;
        float startAngle = Random.Range(0f, 360f);

        for (int i = 1; i < count; i++)
        {
            float   angle  = startAngle + angleStep * (i - 1);
            float   rad    = angle * Mathf.Deg2Rad;
            float   dist   = Mathf.Lerp(radius * 0.4f, radius, (float)i / remaining)
                           + Random.Range(-1f, 1f);
            Vector3 dir    = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            Vector3 target = center + dir * dist;

            NavMeshHit navHit;
            points[i] = NavMesh.SamplePosition(target, out navHit, 3f, NavMesh.AllAreas)
                ? navHit.position
                : center;
        }
        return points;
    }

    private Coroutine RunCoroutine(IEnumerator r) => StartCoroutine(r);
    private void StopManagedCoroutine(ref Coroutine handle)
    {
        if (handle == null) return;
        StopCoroutine(handle);
        handle = null;
    }

    // ── Footsteps ──────────────────────────────────────────────────────────────

    private void HandleFootsteps()
    {
        if (_agent == null) return;
        if (_agent.velocity.sqrMagnitude < 0.1f) { _footstepTimer = 0f; return; }

        bool isRunning = _currentState is PacerChaseState;

        // Reset timer on gait change so the first step fires promptly
        if (isRunning != _wasRunning)
        {
            _footstepTimer  = 0f;
            _wasRunning     = isRunning;
        }

        float interval = isRunning ? runFootstepInterval : walkFootstepInterval;
        float volume   = isRunning ? runFootstepVolume   : walkFootstepVolume;

        _footstepTimer += Time.deltaTime;
        if (_footstepTimer >= interval)
        {
            _footstepTimer = 0f;

            // Alternate between clip 1 and clip 2 for a natural left/right footstep feel
            AudioClip clip;
            if (isRunning)
                clip = _footstepToggle ? runFootstepClip2  : runFootstepClip1;
            else
                clip = _footstepToggle ? walkFootstepClip2 : walkFootstepClip1;

            _footstepToggle = !_footstepToggle;

            if (clip != null)
                _sfxSource.PlayOneShot(clip, volume);
        }
    }

    // ── Stun ───────────────────────────────────────────────────────────────────

    public void TryStun(float duration, float immunityWindow)
    {
        if (_currentState is PacerStunnedState || _immune) return;
        GoToStunned();
        StartCoroutine(StunCoroutine(duration, immunityWindow));
    }

    private IEnumerator StunCoroutine(float duration, float immunityWindow)
    {
        yield return new WaitForSeconds(duration);
        _immune = true;
        PlaySFX(recoveryClip);
        ResumePrevious();
        yield return new WaitForSeconds(immunityWindow);
        _immune = false;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameManager on respawn. Clears the caught flag, restarts the
    /// agent, and returns the NPC to its wander state so it can chase again.
    /// </summary>
    public void ResetForRespawn()
    {
        _playerCaught = false;
        ResumeAgent();
        GoToWander();
    }

    public void SetLevel(int level)
    {
        _level = Mathf.Clamp(level, 0, Mathf.Max(wanderSpeeds.Length, chaseSpeeds.Length) - 1);
        if (_agent != null && !(_currentState is PacerChaseState))
            _agent.speed = ApplySiren(WanderSpeed);
    }

    public void SetSirenActive(bool active)
    {
        _sirenActive = active;
        float baseSpeed = (_currentState is PacerChaseState) ? ChaseSpeed : WanderSpeed;
        if (_agent != null) _agent.speed = ApplySiren(baseSpeed);
    }

    // ── Gizmos ─────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        float   half  = fieldOfViewAngle * 0.5f;
        Vector3 left  = Quaternion.Euler(0, -half, 0) * transform.forward * detectionRange;
        Vector3 right = Quaternion.Euler(0,  half, 0) * transform.forward * detectionRange;
        Gizmos.color  = new Color(0f, 1f, 0f, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + left);
        Gizmos.DrawLine(transform.position, transform.position + right);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, catchDistance);

        Gizmos.color = new Color(0f, 0.5f, 1f, 0.12f);
        Gizmos.DrawWireSphere(transform.position, wanderRadius);

        if (_currentState is PacerSearchState && _searchState != null)
        {
            Vector3[] pts = _searchState.DebugSearchPoints;
            if (pts != null)
            {
                Gizmos.color = Color.cyan;
                for (int i = 0; i < pts.Length; i++)
                {
                    Gizmos.DrawWireSphere(pts[i], 0.4f);
                    if (i > 0) Gizmos.DrawLine(pts[i - 1], pts[i]);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // STATE BASE
    // ═══════════════════════════════════════════════════════════════════════════

    private abstract class PacerStateBase
    {
        protected readonly PacerNPC npc;
        protected PacerStateBase(PacerNPC npc) { this.npc = npc; }
        public abstract void OnEnter();
        public abstract void OnUpdate();
        public virtual  void OnExit() { }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WANDER STATE
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class PacerWanderState : PacerStateBase
    {
        private Coroutine _loop;

        public PacerWanderState(PacerNPC npc) : base(npc) { }

        public override void OnEnter()
        {
            npc.ResumeAgent();
            npc.SetAgentSpeed(npc.WanderSpeed);
            npc.SetAnimSpeed(npc.WanderSpeed);
            _loop = npc.RunCoroutine(WanderLoop());
        }

        public override void OnUpdate()
        {
            if (npc.CanSeePlayer())
            {
                npc._lastKnownPosition = npc._player.position;
                npc.GoToChase();
            }
        }

        public override void OnExit()
        {
            npc.StopManagedCoroutine(ref _loop);
        }

        private IEnumerator WanderLoop()
        {
            while (true)
            {
                // Pick a random reachable point
                Vector3 randomDir = Random.insideUnitSphere * npc.wanderRadius + npc.transform.position;
                Vector3 dest;
                if (!npc.TrySampleNavMesh(randomDir, npc.wanderRadius, out dest))
                {
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                npc.SetNavDestination(dest);
                yield return new WaitUntil(() => npc.HasReachedDestination());

                // Brief pause then keep going
                npc.StopAgent();
                npc.SetAnimSpeed(0f);
                yield return new WaitForSeconds(Random.Range(npc.wanderPauseMin, npc.wanderPauseMax));

                npc.ResumeAgent();
                npc.SetAgentSpeed(npc.WanderSpeed);
                npc.SetAnimSpeed(npc.WanderSpeed);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CHASE STATE
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class PacerChaseState : PacerStateBase
    {
        private float _losTimer;

        // Detects when the NavMesh path to the player is invalid/partial (e.g. the
        // player is inside a room whose floor isn't fully on the NavMesh, or the door
        // is closed and the NPC can still "see" through it via LOS raycast). Without
        // this check the NPC runs in place indefinitely because SetDestination fails
        // silently while CanTrackPlayerInChase() keeps returning true.
        private float _unreachableTimer;
        private const float UnreachableThreshold = 1.5f;

        // Stuck-in-place detection. Handles the case where the NPC has LOS to the
        // player (e.g. through an open safe-room door) but is blocked by door-frame
        // geometry. The agent's velocity is non-zero (it's trying to move) so the
        // path-status check above never fires. Instead we sample the NPC's own world
        // position every StuckCheckInterval seconds; if the net displacement is below
        // StuckMoveThreshold the NPC must be pressing against geometry → Search.
        private Vector3 _stuckCheckPos;
        private float   _stuckCheckTimer;
        private const float StuckCheckInterval = 1.5f;  // seconds between samples
        private const float StuckMoveThreshold = 0.35f; // metres of net movement required

        public PacerChaseState(PacerNPC npc) : base(npc) { }

        public override void OnEnter()
        {
            npc.ResumeAgent();
            npc.SetAgentSpeed(npc.ChaseSpeed);
            npc.SetAnimSpeed(npc.ChaseSpeed);
            _losTimer         = 0f;
            _unreachableTimer = 0f;
            _stuckCheckPos    = npc.transform.position;
            _stuckCheckTimer  = 0f;
            npc.SetNavDestination(npc._lastKnownPosition);
            npc.PlaySFX(npc.alertClip);
        }

        public override void OnUpdate()
        {
            // If the player ducked into a closed safe room or locker, transition to
            // Search immediately — don't wait for any timer.
            if (npc.IsPlayerProtected())
            {
                npc.GoToSearch();
                return;
            }

            // During chase use the relaxed tracking check (no FOV cone, wider range).
            bool canTrack = npc.CanTrackPlayerInChase();

            if (canTrack && npc._player != null)
            {
                _losTimer = 0f;
                npc._lastKnownPosition = npc._player.position;
                npc._agent.SetDestination(npc._player.position);

                if (Vector3.Distance(npc.transform.position, npc._player.position) <= npc.catchDistance)
                {
                    npc._playerCaught = true;
                    npc.StopAgent();
                    npc._pacerJumpscare?.TriggerJumpscare();
                    npc._gameManager?.PlayerDied();
                    return;
                }

                // Path-status check: catches PathInvalid/PathPartial when the agent
                // has also stopped moving (e.g. the room floor isn't on the NavMesh).
                bool pathBlocked = !npc._agent.pathPending
                    && (npc._agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid
                     || npc._agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial)
                    && npc._agent.velocity.sqrMagnitude < 0.05f;

                if (pathBlocked)
                {
                    _unreachableTimer += Time.deltaTime;
                    if (_unreachableTimer >= UnreachableThreshold)
                    {
                        npc.GoToSearch();
                        return;
                    }
                }
                else
                {
                    _unreachableTimer = 0f;
                }

                // Stuck-in-place check: catches the open-door case where the agent IS
                // moving (velocity > 0) but geometry stops it from advancing. If the
                // NPC's net body displacement over StuckCheckInterval is less than
                // StuckMoveThreshold it's running against a wall/door frame → Search.
                _stuckCheckTimer += Time.deltaTime;
                if (_stuckCheckTimer >= StuckCheckInterval)
                {
                    float moved      = Vector3.Distance(npc.transform.position, _stuckCheckPos);
                    _stuckCheckPos   = npc.transform.position;
                    _stuckCheckTimer = 0f;

                    if (moved < StuckMoveThreshold)
                    {
                        npc.GoToSearch();
                        return;
                    }
                }
            }
            else
            {
                // Lost visual contact — reset both stuck and unreachable timers so
                // they don't carry over if the NPC re-acquires the player later.
                _unreachableTimer = 0f;
                _stuckCheckTimer  = 0f;
                _stuckCheckPos    = npc.transform.position;

                _losTimer += Time.deltaTime;
                if (_losTimer < npc.losLostThreshold)
                    npc._agent.SetDestination(npc._lastKnownPosition);
                else
                    npc.GoToSearch();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SEARCH STATE
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class PacerSearchState : PacerStateBase
    {
        private Vector3[] _points;
        private int       _pointIndex;
        private float     _searchTimer;

        public Vector3[] DebugSearchPoints => _points;

        public PacerSearchState(PacerNPC npc) : base(npc) { }

        public override void OnEnter()
        {
            npc.SetAgentSpeed(npc.WanderSpeed);
            npc.SetAnimSpeed(npc.WanderSpeed);
            _points      = npc.GenerateSearchPoints(npc._lastKnownPosition, npc.searchPointCount, npc.searchRadius);
            _pointIndex  = 0;
            _searchTimer = 0f;
            npc.SetNavDestination(_points[0]);
        }

        public override void OnUpdate()
        {
            _searchTimer += Time.deltaTime;

            if (npc.CanSeePlayer())
            {
                npc._lastKnownPosition = npc._player.position;
                npc.GoToChase();
                return;
            }

            if (_searchTimer >= npc.maxSearchTime)
            {
                npc.GoToWander();
                return;
            }

            // As soon as each point is reached, immediately move to the next
            if (npc.HasReachedDestination())
            {
                _pointIndex++;
                if (_pointIndex >= _points.Length)
                {
                    npc.GoToWander();
                    return;
                }
                npc.SetNavDestination(_points[_pointIndex]);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // STUNNED STATE
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class PacerStunnedState : PacerStateBase
    {
        public PacerStunnedState(PacerNPC npc) : base(npc) { }

        public override void OnEnter()
        {
            npc.StopAgent();
            npc.SnapAnimToZero();
            npc.PlaySFX(npc.stunClip);
        }

        public override void OnUpdate() { }
    }
}
