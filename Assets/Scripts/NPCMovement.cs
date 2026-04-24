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

    // Set by SirenPhaseManager during a Siren Phase — disables gaze-freeze/blink rules.
    [HideInInspector] public bool sirenOverride = false;

    public float raycastOffset = 1f;
    public int raycastCount = 5;

    public float cornerCheckDistance = 1f;
    public int cornerCheckRays = 8;

    [Tooltip("How far short of the path end the NPC stops so it doesn't clip into walls")]
    [SerializeField] private float wallStopBuffer = 1.2f;
    [Tooltip("After a blink warp, nudge NPC away from any NavMesh edge closer than this")]
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
    [Tooltip("Rotation offset to fix model forward. Try 0, 90, -90, or 180.")]
    [SerializeField] private float modelForwardOffset = 0f;
    [Tooltip("Frames to skip forward on each blink warp for a visible pose change")]
    [SerializeField] private int blinkAnimFrameSkip = 8;

    [Header("Insanity")]
    [Tooltip("Insanity per second while the player looks at this NPC (stacks across NPCs)")]
    [SerializeField] private float insanityBuildRate = 2f;

    [Header("Siren Phase — Patrol")]
    [Tooltip("Angel only chases during siren if player is this close and visible")]
    [SerializeField] private float sirenDetectionRadius = 10f;
    [Tooltip("Angel gives up once player goes beyond this distance")]
    [SerializeField] private float sirenLoseRadius = 16f;
    [Tooltip("Radius for random wander destinations during siren phase")]
    [SerializeField] private float sirenWanderRadius = 20f;

    [Header("Blink Detection")]
    public BlinkDetector blinkDetector;
    public BlinkVignetteController vignetteController;

    [Header("Blink Movement")]
    [SerializeField] private float blinkJumpDistance = 3f;
    [SerializeField] private float maxBlinkJumpDistance = 5f; // keep conservative to avoid cross-wall snaps
    [SerializeField] private AnimationCurve distanceScaleCurve = AnimationCurve.Linear(0, 0.5f, 30, 1f);
    [SerializeField] private AudioClip blinkMovementSound;
    [SerializeField] private float blinkMovementVolume = 0.3f;

    [Header("Movement Audio")]
    [Tooltip("Looping 3D sound played while the NPC is moving")]
    [SerializeField] private AudioClip movementLoopClip;
    [SerializeField] [Range(0f, 1f)] private float movementLoopVolume = 0.6f;

    [Header("Flashlight Exposure (Angel Respawn)")]
    [Tooltip("Seconds the flashlight must hold on this angel before it respawns")]
    [SerializeField] private float flashlightHoldTimeToRespawn = 5f;
    [Tooltip("Exposure meter decay per second when the flashlight is off the angel")]
    [SerializeField] private float flashlightExposureDecayRate = 1f;
    [Tooltip("Max shake displacement (world units) at full exposure")]
    [SerializeField] private float flashlightMaxShakeIntensity = 0.3f;

    private AudioSource movementAudioSource;  // blink jump one-shots
    private AudioSource movementLoopSource;   // walk/creep loop

    // Flashlight exposure state
    private float           _flashlightExposure     = 0f;
    private bool            _flashlightHitThisFrame = false;
    private NPCSpawnManager _spawnManager;

    // Siren phase wander state
    private bool      sirenChasingPlayer  = false;
    private Coroutine sirenWanderCoroutine = null;

    // Siren stuck detection (mirrors PacerNPC)
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

        if (blinkDetector == null)
        {
            blinkDetector = FindObjectOfType<BlinkDetector>();
        }

        if (vignetteController == null)
        {
            vignetteController = FindObjectOfType<BlinkVignetteController>();
        }

        if (vignetteController != null)
        {
            vignetteController.OnBlinkStart.AddListener(OnBlinkStarted);
            vignetteController.OnScreenFullyBlack.AddListener(OnScreenBlack);
        }

        SirenPhaseManager.Instance?.RegisterAngel(this);

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

        if (movementLoopSource != null && movementLoopSource.isPlaying)
            movementLoopSource.Stop();

        SirenPhaseManager.Instance?.DeregisterAngel(this);
    }

    private void UpdateDifficulty()
    {
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

        NavMeshPath path = new NavMeshPath();
        Vector3 agentNavPos = agent.nextPosition;
        if (!NavMesh.CalculatePath(agentNavPos, player.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete)
            return;

        Vector3 jumpDestination = agentNavPos;
        float remaining = actualJumpDistance;
        Vector3[] corners = path.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            float segmentLen = Vector3.Distance(corners[i - 1], corners[i]);
            if (remaining <= segmentLen)
            {
                jumpDestination = Vector3.Lerp(corners[i - 1], corners[i], remaining / segmentLen);
                break;
            }
            remaining -= segmentLen;
            jumpDestination = corners[i];
        }

        NavMeshHit hit;
        if (!NavMesh.SamplePosition(jumpDestination, out hit, 1.5f, NavMesh.AllAreas))
            return;

        Vector3 faceDir = (player.position - hit.position);
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(faceDir) *
                                 Quaternion.Euler(0f, modelForwardOffset, 0f);

        agent.Warp(hit.position);

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

    public void OnBlinkStarted()
    {
        if (!isActivated || isInJumpscare) return;
        if (sirenOverride) return; // siren ignores blink rules
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        if (animator != null) animator.speed = 0f;
    }

    public void OnScreenBlack()
    {
        if (!isActivated || isInJumpscare) return;

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

        // Player is hidden in a locker — freeze. Siren phase ignores this.
        if (LockerInteraction.IsHidingInLocker && !sirenOverride)
        {
            agent.isStopped = true;
            agent.velocity  = Vector3.zero;
            if (animator != null) animator.speed = 0f;
            wasVisibleLastFrame = false;
            return;
        }

        // Siren phase: wander until player is close + visible, chase, drop chase when out of range.
        if (sirenOverride)
        {
            isActivated = true;
            if (animator != null) animator.speed = 1f;

            if (sirenWanderCoroutine == null && !sirenChasingPlayer)
            {
                sirenWanderCoroutine = StartCoroutine(SirenWanderLoop());
                _sirenStuckCheckPos   = transform.position;
                _sirenStuckCheckTimer = 0f;
            }

            float distToPlayer = Vector3.Distance(transform.position, player.position);

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
            }

            if (sirenChasingPlayer)
            {
                if (distToPlayer > sirenLoseRadius || IsPlayerProtected())
                {
                    sirenChasingPlayer    = false;
                    _sirenStuckCheckTimer = 0f;
                }
                else
                {
                    // If the angel hasn't moved much in a while it's stuck at a door — drop chase.
                    _sirenStuckCheckTimer += Time.deltaTime;
                    if (_sirenStuckCheckTimer >= SirenStuckCheckInterval)
                    {
                        float moved           = Vector3.Distance(transform.position, _sirenStuckCheckPos);
                        _sirenStuckCheckPos   = transform.position;
                        _sirenStuckCheckTimer = 0f;
                        if (moved < SirenStuckMoveThreshold)
                        {
                            sirenChasingPlayer = false;
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
                    isActivated = true;

                if (isActivated)
                {
                    if (isCurrentlyVisible && !sirenOverride)
                    {
                        agent.isStopped = true;
                        agent.velocity = Vector3.zero;
                        agent.ResetPath();
                        if (animator != null) animator.speed = 0f;

                        InsanityManager.Instance?.AddInsanity(insanityBuildRate * Time.deltaTime);
                    }
                    else
                    {
                        // Snap pose the frame the player looks away, but not during a blink
                        // (PerformBlinkJump handles that case) — otherwise it fires twice.
                        if (wasVisibleLastFrame && !IsPlayerBlinking())
                            SnapAnimationForward();

                        // Only move when screen is clear, so the player never sees the lurch
                        // into motion during a blink fade.
                        bool screenClear = vignetteController == null || !vignetteController.IsBlinkAnimating();

                        if (screenClear)
                        {
                            NavMeshPath path = new NavMeshPath();
                            if (agent.CalculatePath(player.position, path))
                            {
                                if (path.corners.Length > 1)
                                {
                                    Vector3 nextCorner = path.corners[1];

                                    if (!WouldBeVisibleAtPosition(nextCorner))
                                    {
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
                                                float safeDistBack = Mathf.Max(0f, dist - wallStopBuffer);
                                                Vector3 safePosition = currentPos + directionToCorner * safeDistBack;
                                                agent.SetDestination(safePosition);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

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

            wasVisibleLastFrame = isCurrentlyVisible;

            if (isActivated && player != null && HeartbeatManager.Instance != null)
            {
                HeartbeatManager.Instance.UpdateHeartbeat(distanceToPlayer);
            }
        }

        // Looping walk clip — plays whenever the agent has real velocity.
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

    private IEnumerator SirenWanderLoop()
    {
        while (sirenOverride && !sirenChasingPlayer)
        {
            Vector3 randomDir = Random.insideUnitSphere * sirenWanderRadius;
            randomDir += transform.position;

            NavMeshHit navHit;
            if (NavMesh.SamplePosition(randomDir, out navHit, sirenWanderRadius, NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.SetDestination(navHit.position);
            }

            yield return new WaitUntil(() =>
                !sirenOverride ||
                sirenChasingPlayer ||
                (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f));

            if (!sirenOverride || sirenChasingPlayer) break;

            yield return new WaitForSeconds(Random.Range(0.3f, 1.0f));
        }

        sirenWanderCoroutine = null;
    }

    public void NotifyFlashlightHit()
    {
        _flashlightHitThisFrame = true;
    }

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

    private void LateUpdate()
    {
        if (_flashlightExposure <= 0f) return;

        float shakeT     = _flashlightExposure / flashlightHoldTimeToRespawn;
        float shakePower = shakeT * shakeT; // quadratic ramp
        Vector3 shake    = Random.insideUnitSphere * (shakePower * flashlightMaxShakeIntensity);
        shake.y          = 0f;
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

    public void ResetActivation()
    {
        isActivated         = false;
        isCurrentlyVisible  = false;
        wasVisibleLastFrame = false;
        _flashlightExposure = 0f;

        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity  = Vector3.zero;
        }
        if (animator != null) animator.speed = 0f;
    }
}
