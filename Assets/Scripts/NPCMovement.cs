using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class NPCMovement : MonoBehaviour
{
    public Transform player;
    public NavMeshAgent agent;
    public GameManager gameManager;
    public AudioSource jumpscareAudio;
    public CameraControl cameraControl;

    public float speed = 3.5f;
    public float detectionRange = 30f;
    public LayerMask obstaclesLayer;
    public float fieldOfViewAngle = 60f;
    private bool isActivated = false;
    private bool isCurrentlyVisible = false;
    private bool wasVisibleLastFrame = false;

    /// <summary>
    /// Set true by SirenPhaseManager during a Siren Phase.
    /// Disables the gaze-freeze and blink-stop mechanics so this NPC runs freely.
    /// </summary>
    [HideInInspector] public bool sirenOverride = false;

    public float raycastOffset = 1f;
    public int raycastCount = 5;

    public float cornerCheckDistance = 1f;
    public int cornerCheckRays = 8;

    [Tooltip("How far short of the path end the NPC stops — prevents mesh clipping into walls")]
    [SerializeField] private float wallStopBuffer = 1.2f;
    [Tooltip("After a blink warp, nudge the NPC away from any NavMesh edge closer than this distance")]
    [SerializeField] private float wallRetreatDistance = 0.8f;

    private Transform flareTarget;
    private bool isTargetingFlare = false;

    private bool isInJumpscare = false;

    [Header("Difficulty Scaling")]
    public float speedIncreasePerPart = 1f;
    public float detectionRangeIncreasePerPart = 2f;

    private float baseSpeed;
    private float baseDetectionRange;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [Tooltip("Rotation offset to correct model forward direction. Try 0, 90, -90, or 180 until NPC faces the player correctly.")]
    [SerializeField] private float modelForwardOffset = 0f;
    [Tooltip("How many animation frames to skip forward on each blink warp — gives visible pose change between blinks")]
    [SerializeField] private int blinkAnimFrameSkip = 8;

    [Header("Insanity")]
    [Tooltip("Insanity added per second while the player is directly looking at this NPC. Stacks — two visible NPCs add twice this rate, three add three times, etc.")]
    [SerializeField] private float insanityBuildRate = 2f;

    [Header("Siren Phase — Patrol")]
    [Tooltip("During siren, angel only chases if the player is within this distance AND has line of sight.")]
    [SerializeField] private float sirenDetectionRadius = 10f;
    [Tooltip("Angel gives up the chase and returns to wander once the player is beyond this distance.")]
    [SerializeField] private float sirenLoseRadius = 16f;
    [Tooltip("How far the angel picks its random wander destinations during the siren phase.")]
    [SerializeField] private float sirenWanderRadius = 20f;

    [Header("Blink Detection")]
    public BlinkDetector blinkDetector;
    public BlinkVignetteController vignetteController;

    [Header("Blink Movement")]
    [SerializeField] private float blinkJumpDistance = 3f; // How far NPC moves per blink
    [SerializeField] private float maxBlinkJumpDistance = 5f; // Maximum jump distance — keep conservative to avoid cross-wall snaps
    [SerializeField] private AnimationCurve distanceScaleCurve = AnimationCurve.Linear(0, 0.5f, 30, 1f);
    [SerializeField] private AudioClip blinkMovementSound;
    [SerializeField] private float blinkMovementVolume = 0.3f;

    [Header("Movement Audio")]
    [Tooltip("Looping sound played while the NPC is actively moving (siren phase walking, normal chase creep). 3D spatial.")]
    [SerializeField] private AudioClip movementLoopClip;
    [SerializeField] [Range(0f, 1f)] private float movementLoopVolume = 0.6f;

    [Header("Flashlight Exposure (Angel Respawn)")]
    [Tooltip("Seconds the player must hold the flashlight on this angel to trigger a respawn.")]
    [SerializeField] private float flashlightHoldTimeToRespawn = 5f;
    [Tooltip("How quickly the exposure meter decays per second when the flashlight is removed.")]
    [SerializeField] private float flashlightExposureDecayRate = 1f;
    [Tooltip("Maximum shake displacement (world units) at full exposure. The offset is applied to the root transform in LateUpdate so it is visible but never permanent — the NavMeshAgent corrects the position next frame.")]
    [SerializeField] private float flashlightMaxShakeIntensity = 0.3f;

    private AudioSource movementAudioSource;  // one-shot blink jump sounds
    private AudioSource movementLoopSource;   // looping walk/creep sound

    // Flashlight exposure state
    private float           _flashlightExposure     = 0f;
    private bool            _flashlightHitThisFrame = false;
    private NPCSpawnManager _spawnManager;

    // Siren phase wander state
    private bool      sirenChasingPlayer  = false;
    private Coroutine sirenWanderCoroutine = null;

    // Siren phase stuck detection — mirrors PacerNPC's StuckCheck logic
    private Vector3 _sirenStuckCheckPos;
    private float   _sirenStuckCheckTimer;
    private const float SirenStuckCheckInterval  = 1.5f;
    private const float SirenStuckMoveThreshold  = 0.3f;

    private void Start()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
        gameManager   = FindObjectOfType<GameManager>();
        _spawnManager = FindObjectOfType<NPCSpawnManager>();
        agent.speed = speed;
        baseSpeed = speed;
        baseDetectionRange = detectionRange;

        // Auto-find components
        if (blinkDetector == null)
        {
            blinkDetector = FindObjectOfType<BlinkDetector>();
        }

        if (vignetteController == null)
        {
            vignetteController = FindObjectOfType<BlinkVignetteController>();
        }

        // CRITICAL: Subscribe to blink events
        if (vignetteController != null)
        {
            vignetteController.OnBlinkStart.AddListener(OnBlinkStarted);
            vignetteController.OnScreenFullyBlack.AddListener(OnScreenBlack);
        }

        // Register with SirenPhaseManager so it can set sirenOverride during a Siren Phase
        SirenPhaseManager.Instance?.RegisterAngel(this);

        // One-shot AudioSource for blink-jump sounds
        if (blinkMovementSound != null)
        {
            movementAudioSource = gameObject.AddComponent<AudioSource>();
            movementAudioSource.clip         = blinkMovementSound;
            movementAudioSource.volume       = blinkMovementVolume;
            movementAudioSource.spatialBlend = 1f;
            movementAudioSource.minDistance  = 5f;
            movementAudioSource.maxDistance  = 20f;
            movementAudioSource.playOnAwake  = false;
        }

        // Looping AudioSource for continuous movement (walking / creeping)
        if (movementLoopClip != null)
        {
            movementLoopSource = gameObject.AddComponent<AudioSource>();
            movementLoopSource.clip         = movementLoopClip;
            movementLoopSource.loop         = true;
            movementLoopSource.volume       = movementLoopVolume;
            movementLoopSource.spatialBlend = 1f;
            movementLoopSource.minDistance  = 3f;
            movementLoopSource.maxDistance  = 15f;
            movementLoopSource.playOnAwake  = false;
        }

    }

    private void OnDestroy()
    {
        if (vignetteController != null)
        {
            vignetteController.OnBlinkStart.RemoveListener(OnBlinkStarted);
            vignetteController.OnScreenFullyBlack.RemoveListener(OnScreenBlack);
        }

        // Stop looping audio so it doesn't carry over after the NPC is destroyed
        if (movementLoopSource != null && movementLoopSource.isPlaying)
            movementLoopSource.Stop();

        // Deregister from SirenPhaseManager to prevent stale reference after respawn
        SirenPhaseManager.Instance?.DeregisterAngel(this);
    }

    // Difficulty scaling — placeholder until collectible system is wired up
    private void UpdateDifficulty()
    {
        // Will be driven by collectible count once that system replaces CarParts
    }

    private bool IsPlayerProtected()
        => PlayerSafeZone.IsPlayerInRoom
        || PlayerSafeZone.IsPlayerProtected
        || LockerInteraction.IsHidingInLocker;

    private bool CheckLineOfSight()
    {
        if (player == null) return false;

        Vector3 directionToPlayer = (player.position - transform.position).normalized;
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        Collider npcCollider = GetComponent<Collider>();
        if (npcCollider == null) return false;

        float height = npcCollider.bounds.size.y;
        Vector3 basePosition = transform.position;

        for (int i = 0; i < raycastCount; i++)
        {
            float heightOffset = (height * i) / (raycastCount - 1);
            Vector3 rayStart = basePosition + Vector3.up * heightOffset;

            if (!Physics.Raycast(rayStart, directionToPlayer, distanceToPlayer, obstaclesLayer))
            {
                return true;
            }
        }

        return false;
    }

    private bool WouldBeVisibleAtPosition(Vector3 position)
    {
        Vector3 directionToPlayer = (player.position - position).normalized;
        float distanceToPlayer = Vector3.Distance(position, player.position);
        float angle = Vector3.Angle(-directionToPlayer, player.forward);

        if (angle > fieldOfViewAngle / 2)
            return false;

        for (int i = 0; i < cornerCheckRays; i++)
        {
            float angleOffset = (360f / cornerCheckRays) * i;
            Vector3 checkPosition = position + (Quaternion.Euler(0, angleOffset, 0) * Vector3.forward * cornerCheckDistance);

            if (!Physics.Raycast(checkPosition, directionToPlayer, distanceToPlayer, obstaclesLayer))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPlayerBlinking()
    {
        if (blinkDetector == null) return false;
        return blinkDetector.IsBlinking;
    }


    // Returns a point along path corners that is 'buffer' units short of the final corner.
    // Prevents the NPC from walking all the way to the path end (wall edge) and clipping through.
    private Vector3 GetBufferedDestination(Vector3[] corners, float buffer)
    {
        float remaining = buffer;
        for (int i = corners.Length - 1; i > 0; i--)
        {
            float segLen = Vector3.Distance(corners[i], corners[i - 1]);
            if (remaining <= segLen)
                return Vector3.Lerp(corners[i], corners[i - 1], remaining / segLen);
            remaining -= segLen;
        }
        return corners[0]; // path shorter than buffer — stay near origin
    }

    private void PerformBlinkJump()
    {
        if (player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        if (distanceToPlayer < 2f) return;

        float distanceScale = distanceScaleCurve.Evaluate(distanceToPlayer);
        float actualJumpDistance = Mathf.Min(blinkJumpDistance * distanceScale, maxBlinkJumpDistance);

        // Calculate the full NavMesh path to the player first.
        // Using agent.nextPosition as the origin (where the agent actually sits on the NavMesh)
        // rather than transform.position, which can be slightly off-mesh when pressed against a wall.
        NavMeshPath path = new NavMeshPath();
        Vector3 agentNavPos = agent.nextPosition;
        if (!NavMesh.CalculatePath(agentNavPos, player.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete)
            return; // No valid route to player — don't jump

        // Walk along the path corners, consuming actualJumpDistance units.
        // This ensures the jump follows the NavMesh corridor (around walls) rather than cutting
        // through them in a straight line. The destination is wherever we run out of distance budget.
        Vector3 jumpDestination = agentNavPos;
        float remaining = actualJumpDistance;
        Vector3[] corners = path.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            float segmentLen = Vector3.Distance(corners[i - 1], corners[i]);
            if (remaining <= segmentLen)
            {
                // Land partway along this segment
                jumpDestination = Vector3.Lerp(corners[i - 1], corners[i], remaining / segmentLen);
                break;
            }
            remaining -= segmentLen;
            jumpDestination = corners[i];
        }

        // Small surface snap to account for float imprecision in the corner lerp
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(jumpDestination, out hit, 1.5f, NavMesh.AllAreas))
            return;

        // Snap rotation to face player instantly
        Vector3 faceDir = (player.position - hit.position);
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(faceDir) *
                                 Quaternion.Euler(0f, modelForwardOffset, 0f);

        agent.Warp(hit.position);

        // If the warp landed close to a wall edge, nudge away so the mesh doesn't clip through.
        // FindClosestEdge returns the nearest NavMesh boundary — if we're too close, push back.
        NavMeshHit edgeHit;
        if (NavMesh.FindClosestEdge(hit.position, out edgeHit, NavMesh.AllAreas))
        {
            if (edgeHit.distance < wallRetreatDistance)
            {
                Vector3 awayFromWall = (hit.position - edgeHit.position).normalized;
                Vector3 retreatedPos = hit.position + awayFromWall * (wallRetreatDistance - edgeHit.distance);
                NavMeshHit retreatHit;
                if (NavMesh.SamplePosition(retreatedPos, out retreatHit, 1f, NavMesh.AllAreas))
                    agent.Warp(retreatHit.position);
            }
        }

        SnapAnimationForward();

        if (movementAudioSource != null && blinkMovementSound != null)
            movementAudioSource.PlayOneShot(blinkMovementSound);
    }

    // Scrubs the current animation forward by blinkAnimFrameSkip frames then re-freezes.
    // Gives the creature a different pose each time the player blinks or looks away without
    // any visible continuous playback — the pose change only appears when the screen opens up.
    private void SnapAnimationForward()
    {
        if (animator == null) return;

        animator.speed = 1f;
        float fps = 30f;
        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
        if (clips.Length > 0 && clips[0].clip != null)
            fps = clips[0].clip.frameRate;

        animator.Update(blinkAnimFrameSkip / fps);
        animator.speed = 0f;
    }

    // Called the instant a blink is detected — before the screen starts going black.
    // Freeze immediately so no animation is visible during the close fade.
    public void OnBlinkStarted()
    {
        if (!isActivated || isInJumpscare) return;
        if (sirenOverride) return; // During siren phase, blinks don't freeze the angel
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        if (animator != null) animator.speed = 0f;
    }

    public void OnScreenBlack()
    {
        if (!isActivated || isInJumpscare) return;

        // Only jump if in player's FOV
        Vector3 directionToPlayer = (player.position - transform.position).normalized;
        float angle = Vector3.Angle(-directionToPlayer, player.forward);
        bool inPlayerFOV = angle <= fieldOfViewAngle / 2;

        if (inPlayerFOV)
        {
            PerformBlinkJump();
        }
    }

    private void Update()
    {
        if (isInJumpscare || player == null || agent == null) return;

        if (!agent.isOnNavMesh) return;

        UpdateFlashlightExposure();

        // Player is hiding in a locker — freeze in place so they can't be tracked.
        // Siren phase overrides this: during a siren angels run freely regardless.
        if (LockerInteraction.IsHidingInLocker && !sirenOverride)
        {
            agent.isStopped = true;
            agent.velocity  = Vector3.zero;
            if (animator != null) animator.speed = 0f;
            wasVisibleLastFrame = false;
            return;
        }

        // ── SIREN PHASE ──────────────────────────────────────────────────────────
        // Angels wander randomly around the level. If the player comes within
        // sirenDetectionRadius AND has line of sight, the angel chases. It gives up
        // once the player moves beyond sirenLoseRadius. No gaze/blink rules apply.
        if (sirenOverride)
        {
            isActivated = true;
            if (animator != null) animator.speed = 1f;

            // Start the wander coroutine if it isn't running yet
            if (sirenWanderCoroutine == null && !sirenChasingPlayer)
            {
                sirenWanderCoroutine = StartCoroutine(SirenWanderLoop());
                _sirenStuckCheckPos   = transform.position;
                _sirenStuckCheckTimer = 0f;
            }

            float distToPlayer = Vector3.Distance(transform.position, player.position);

            // Detect player: close enough AND line of sight, but not if player is protected
            if (!sirenChasingPlayer && distToPlayer <= sirenDetectionRadius && CheckLineOfSight() && !IsPlayerProtected())
            {
                sirenChasingPlayer    = true;
                _sirenStuckCheckPos   = transform.position;
                _sirenStuckCheckTimer = 0f;
                if (sirenWanderCoroutine != null)
                {
                    StopCoroutine(sirenWanderCoroutine);
                    sirenWanderCoroutine = null;
                }
                Debug.Log("[NPCMovement] Siren: player detected — chasing.");
            }

            // Lose the player: out of range, protected (safe room / locker), or stuck at a door
            if (sirenChasingPlayer)
            {
                if (distToPlayer > sirenLoseRadius || IsPlayerProtected())
                {
                    sirenChasingPlayer    = false;
                    _sirenStuckCheckTimer = 0f;
                    Debug.Log("[NPCMovement] Siren: lost player — resuming wander.");
                }
                else
                {
                    // Stuck-in-place detection: if the angel hasn't moved enough over the
                    // check interval it's pressing against a door frame — stop chasing.
                    _sirenStuckCheckTimer += Time.deltaTime;
                    if (_sirenStuckCheckTimer >= SirenStuckCheckInterval)
                    {
                        float moved           = Vector3.Distance(transform.position, _sirenStuckCheckPos);
                        _sirenStuckCheckPos   = transform.position;
                        _sirenStuckCheckTimer = 0f;
                        if (moved < SirenStuckMoveThreshold)
                        {
                            sirenChasingPlayer = false;
                            Debug.Log("[NPCMovement] Siren: stuck at door — resuming wander.");
                        }
                    }
                }
            }

            if (sirenChasingPlayer)
            {
                agent.isStopped = false;
                agent.SetDestination(player.position);
            }
            // else: SirenWanderLoop handles destinations

            // Rotate to face movement direction
            if (agent.velocity.sqrMagnitude > 0.1f)
            {
                Vector3 moveDir = agent.velocity;
                moveDir.y = 0f;
                if (moveDir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(moveDir) * Quaternion.Euler(0f, modelForwardOffset, 0f),
                        Time.deltaTime * 10f);
            }

            wasVisibleLastFrame = false;
            return;
        }

        // Siren just ended — clean up wander state
        if (sirenWanderCoroutine != null)
        {
            StopCoroutine(sirenWanderCoroutine);
            sirenWanderCoroutine = null;
        }
        sirenChasingPlayer = false;
        // ── END SIREN PHASE ─────────────────────────────────────────────────────

        if (isTargetingFlare && flareTarget != null)
        {
            if (enabled)
            {
                Vector3 directionToPlayer = (player.position - transform.position).normalized;
                float angle = Vector3.Angle(-directionToPlayer, player.forward);
                bool isInPlayerFOV = angle <= fieldOfViewAngle / 2;

                bool playerBlinking = IsPlayerBlinking();

                if (!isInPlayerFOV || playerBlinking)
                {
                    agent.isStopped = false;
                    agent.SetDestination(flareTarget.position);
                }
                else
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
            }
        }
        else
        {
            float distanceToPlayer = Vector3.Distance(transform.position, player.position);

            if (distanceToPlayer <= detectionRange)
            {
                Vector3 directionToPlayer = (player.position - transform.position).normalized;
                float angle = Vector3.Angle(-directionToPlayer, player.forward);
                bool inPlayerFOV = angle <= fieldOfViewAngle / 2;

                bool hasLineOfSight = CheckLineOfSight();

                bool playerBlinking = IsPlayerBlinking();
                isCurrentlyVisible = inPlayerFOV && hasLineOfSight && !playerBlinking;

                if (isCurrentlyVisible && !isActivated)
                {
                    Debug.Log("NPC Activated - Player has direct line of sight!");
                    isActivated = true;
                }

                if (isActivated)
                {
                    // sirenOverride: during a Siren Phase the angel ignores gaze entirely and runs freely
                    if (isCurrentlyVisible && !sirenOverride)
                    {
                        agent.isStopped = true;
                        agent.velocity = Vector3.zero;
                        agent.ResetPath();
                        // Freeze animation on the current frame — creature holds its pose while observed
                        if (animator != null) animator.speed = 0f;

                        // Build insanity while the player stares at the angel
                        InsanityManager.Instance?.AddInsanity(insanityBuildRate * Time.deltaTime);
                    }
                    else
                    {
                        // One-shot pose snap the exact frame the player looks away —
                        // but NOT during a blink (PerformBlinkJump handles that snap separately).
                        // Without this guard the snap fires twice: once here when playerBlinking
                        // makes isCurrentlyVisible go false, and again inside PerformBlinkJump.
                        if (wasVisibleLastFrame && !IsPlayerBlinking())
                            SnapAnimationForward();

                        // Only unfreeze movement and animation when the screen is fully transparent —
                        // this prevents the player seeing the NPC lurch into motion during the
                        // eye-open fade or at the edge of a blink
                        bool screenClear = vignetteController == null || !vignetteController.IsBlinkAnimating();

                        if (screenClear)
                        {
                            // Animation stays frozen (speed = 0) — pose only advances via
                            // SnapAnimationForward() on look-away or blink transitions.
                            // The agent still moves normally; this only controls the visual pose.

                            NavMeshPath path = new NavMeshPath();
                            if (agent.CalculatePath(player.position, path))
                            {
                                if (path.corners.Length > 1)
                                {
                                    Vector3 nextCorner = path.corners[1];

                                    if (!WouldBeVisibleAtPosition(nextCorner))
                                    {
                                        // Pull the destination wallStopBuffer units back from the path end
                                        // so the NPC never presses its mesh flush against a wall surface
                                        agent.isStopped = false;
                                        agent.SetDestination(GetBufferedDestination(path.corners, wallStopBuffer));
                                    }
                                    else
                                    {
                                        Vector3 currentPos = transform.position;
                                        Vector3 directionToCorner = (nextCorner - currentPos).normalized;
                                        float distanceToCorner = Vector3.Distance(currentPos, nextCorner);

                                        for (float dist = 0; dist < distanceToCorner; dist += 0.5f)
                                        {
                                            Vector3 checkPos = currentPos + directionToCorner * dist;
                                            if (WouldBeVisibleAtPosition(checkPos))
                                            {
                                                // Stop wallStopBuffer short rather than a fixed 0.5f
                                                float safeDistBack = Mathf.Max(0f, dist - wallStopBuffer);
                                                Vector3 safePosition = currentPos + directionToCorner * safeDistBack;
                                                agent.SetDestination(safePosition);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            // Rotation slerp gated behind screenClear — same as movement so it
                            // never runs during the blink close/open fade where the player can see it
                            Vector3 faceDir = (player.position - transform.position);
                            faceDir.y = 0f;
                            if (faceDir.sqrMagnitude > 0.01f)
                            {
                                Quaternion targetRot = Quaternion.LookRotation(faceDir) *
                                                       Quaternion.Euler(0f, modelForwardOffset, 0f);
                                transform.rotation = Quaternion.Slerp(
                                    transform.rotation,
                                    targetRot,
                                    Time.deltaTime * 10f);
                            }
                        }
                    }
                }
            }
            else
            {
                isActivated = false;
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                if (animator != null) animator.speed = 0f;
            }

            // Track last frame visibility for the look-away snap
            wasVisibleLastFrame = isCurrentlyVisible;

            if (isActivated && player != null && HeartbeatManager.Instance != null)
            {
                HeartbeatManager.Instance.UpdateHeartbeat(distanceToPlayer);
            }
        }

        // ── Movement loop audio ──────────────────────────────────────────────────
        // Play the looping clip whenever the NavMesh agent has actual velocity;
        // stop it the moment the NPC halts. Works across normal and siren phases.
        if (movementLoopSource != null)
        {
            bool isMoving = agent != null && agent.isOnNavMesh
                            && agent.velocity.sqrMagnitude > 0.05f;

            if (isMoving && !movementLoopSource.isPlaying)
                movementLoopSource.Play();
            else if (!isMoving && movementLoopSource.isPlaying)
                movementLoopSource.Stop();
        }
    }

    // ── Siren Wander Loop ─────────────────────────────────────────────────────

    /// <summary>
    /// While the siren is active and the angel hasn't spotted the player,
    /// picks random NavMesh destinations across the level — covering ground
    /// and searching rather than standing still.
    /// </summary>
    private IEnumerator SirenWanderLoop()
    {
        while (sirenOverride && !sirenChasingPlayer)
        {
            // Pick a wide random point to properly cover the level
            Vector3 randomDir = Random.insideUnitSphere * sirenWanderRadius;
            randomDir += transform.position;

            NavMeshHit navHit;
            if (NavMesh.SamplePosition(randomDir, out navHit, sirenWanderRadius, NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.SetDestination(navHit.position);
            }

            // Wait until we arrive at the destination (or siren ends / player detected)
            yield return new WaitUntil(() =>
                !sirenOverride ||
                sirenChasingPlayer ||
                (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f));

            if (!sirenOverride || sirenChasingPlayer) break;

            // Brief pause before picking the next destination
            yield return new WaitForSeconds(Random.Range(0.3f, 1.0f));
        }

        sirenWanderCoroutine = null;
    }

    // ── Flashlight Exposure (Angel Respawn) ──────────────────────────────────

    /// <summary>
    /// Called by FlashlightController each frame the flashlight cone is aimed at this angel.
    /// </summary>
    public void NotifyFlashlightHit()
    {
        _flashlightHitThisFrame = true;
    }

    /// <summary>
    /// Builds or decays flashlight exposure and fires the respawn at full charge.
    /// Shake position is NOT applied here — it runs in LateUpdate() so the Animator
    /// cannot overwrite it between Update and the render frame.
    /// </summary>
    private void UpdateFlashlightExposure()
    {
        if (_flashlightHitThisFrame)
        {
            _flashlightExposure = Mathf.Min(
                _flashlightExposure + Time.deltaTime, flashlightHoldTimeToRespawn);
            _flashlightHitThisFrame = false;
        }
        else
        {
            _flashlightExposure = Mathf.Max(
                _flashlightExposure - flashlightExposureDecayRate * Time.deltaTime, 0f);
        }

        if (_flashlightExposure >= flashlightHoldTimeToRespawn)
            TriggerFlashlightRespawn();
    }

    /// <summary>
    /// Adds a positional jitter to the root transform each LateUpdate.
    /// Runs after the Animator so the offset survives to the render frame.
    /// The NavMeshAgent restores the true NavMesh position next FixedUpdate,
    /// so the NPC never permanently drifts — the shake is purely visual.
    /// No child references needed — works with any model hierarchy.
    /// </summary>
    private void LateUpdate()
    {
        if (_flashlightExposure <= 0f) return;

        float shakeT     = _flashlightExposure / flashlightHoldTimeToRespawn;
        float shakePower = shakeT * shakeT; // quadratic — subtle at first, violent near threshold
        Vector3 shake    = Random.insideUnitSphere * (shakePower * flashlightMaxShakeIntensity);
        shake.y          = 0f; // horizontal shake only
        transform.position += shake;
    }

    private void TriggerFlashlightRespawn()
    {
        _flashlightExposure = 0f;

        if (_spawnManager == null)
            _spawnManager = FindObjectOfType<NPCSpawnManager>();

        if (_spawnManager != null)
            _spawnManager.RespawnSingleNPC(gameObject);
        else
            Debug.LogWarning("[NPCMovement] TriggerFlashlightRespawn: NPCSpawnManager not found.");
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        if (isInJumpscare) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Vector3 fovLine1 = Quaternion.AngleAxis(fieldOfViewAngle / 2, transform.up) * transform.forward * detectionRange;
        Vector3 fovLine2 = Quaternion.AngleAxis(-fieldOfViewAngle / 2, transform.up) * transform.forward * detectionRange;

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, fovLine1);
        Gizmos.DrawRay(transform.position, fovLine2);

        if (!isInJumpscare && player != null)
        {
            Vector3 directionToPlayer = (player.position - transform.position).normalized;
            float distanceToPlayer = Vector3.Distance(transform.position, player.position);

            if (distanceToPlayer <= detectionRange)
            {
                float angle = Vector3.Angle(-directionToPlayer, transform.forward);
                if (angle <= fieldOfViewAngle / 2)
                {
                    Gizmos.color = isActivated ? Color.red : Color.green;
                    Gizmos.DrawRay(transform.position, directionToPlayer * distanceToPlayer);
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Player is safely hidden in a locker — don't trigger a jumpscare
            if (LockerInteraction.IsHidingInLocker) return;

            if (agent != null)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            TriggerJumpscare();

            if (gameManager != null)
            {
                gameManager.PlayerDied();
            }
            else
            {
                Debug.LogError("GameManager instance not found.");
            }
        }
    }

    private void TriggerJumpscare()
    {
        isInJumpscare = true;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        if (jumpscareAudio != null)
        {
            jumpscareAudio.Play();
        }

        if (cameraControl != null)
        {
            cameraControl.TriggerJumpscare();
            StartCoroutine(ResetAfterJumpscare());
        }
    }

    private System.Collections.IEnumerator ResetAfterJumpscare()
    {
        yield return new WaitForSeconds(5f);
        isInJumpscare = false;
        agent.isStopped = false;
    }

    public void SetFlareTarget(Transform target)
    {
        flareTarget = target;
        isTargetingFlare = true;

        if (agent != null)
        {
            agent.isStopped = false;
            agent.SetDestination(target.position);
        }
    }

    public void ClearFlareTarget()
    {
        flareTarget = null;
        isTargetingFlare = false;
    }

    /// <summary>
    /// Resets the NPC to its idle, deactivated state after being repositioned.
    /// Called by NPCSpawnManager after a warp so the NPC doesn't immediately
    /// resume chasing if it spawns within detection range of the player.
    /// The NPC will only re-activate once the player looks at it again.
    /// </summary>
    public void ResetActivation()
    {
        isActivated         = false;
        isCurrentlyVisible  = false;
        wasVisibleLastFrame = false;
        _flashlightExposure = 0f; // also clears any residual shake

        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity  = Vector3.zero;
        }
        if (animator != null) animator.speed = 0f;
    }
}
