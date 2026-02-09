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

    [Header("Blink Detection")]
    public BlinkDetector blinkDetector;
    public BlinkVignetteController vignetteController;

    [Header("Blink Movement")]
    [SerializeField] private float blinkJumpDistance = 3f; // How far NPC moves per blink
    [SerializeField] private float maxBlinkJumpDistance = 5f; // Maximum jump distance — keep conservative to avoid cross-wall snaps
    [SerializeField] private AnimationCurve distanceScaleCurve = AnimationCurve.Linear(0, 0.5f, 30, 1f);
    [SerializeField] private AudioClip blinkMovementSound;
    [SerializeField] private float blinkMovementVolume = 0.3f;

    private AudioSource movementAudioSource;

    private void Start()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
        gameManager = FindObjectOfType<GameManager>();
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

        // Create audio source for movement sounds
        if (blinkMovementSound != null)
        {
            movementAudioSource = gameObject.AddComponent<AudioSource>();
            movementAudioSource.clip = blinkMovementSound;
            movementAudioSource.volume = blinkMovementVolume;
            movementAudioSource.spatialBlend = 1f;
            movementAudioSource.minDistance = 5f;
            movementAudioSource.maxDistance = 20f;
        }

    }

    private void OnDestroy()
    {
        if (vignetteController != null)
        {
            vignetteController.OnBlinkStart.RemoveListener(OnBlinkStarted);
            vignetteController.OnScreenFullyBlack.RemoveListener(OnScreenBlack);
        }
    }

    // Difficulty scaling — placeholder until collectible system is wired up
    private void UpdateDifficulty()
    {
        // Will be driven by collectible count once that system replaces CarParts
    }

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

        if (!agent.isOnNavMesh)
        {
            return;
        }

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
                    if (isCurrentlyVisible)
                    {
                        agent.isStopped = true;
                        agent.velocity = Vector3.zero;
                        agent.ResetPath();
                        // Freeze animation on the current frame — creature holds its pose while observed
                        if (animator != null) animator.speed = 0f;
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
    }

    // Rest of the code stays the same...
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
}
