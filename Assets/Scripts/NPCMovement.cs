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

    public float raycastOffset = 1f;
    public int raycastCount = 5;

    public float cornerCheckDistance = 1f;
    public int cornerCheckRays = 8;

    private Transform flareTarget;
    private bool isTargetingFlare = false;

    private bool isInJumpscare = false;

    [Header("Blink Detection")]
    public BlinkDetector blinkDetector;

    private void Start()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
        gameManager = FindObjectOfType<GameManager>();
        agent.speed = speed;

        if (blinkDetector == null)
        {
            blinkDetector = FindObjectOfType<BlinkDetector>();
        }
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
                    }
                    else
                    {
                        // Move towards player when not visible
                        NavMeshPath path = new NavMeshPath();
                        if (agent.CalculatePath(player.position, path))
                        {
                            if (path.corners.Length > 1)
                            {
                                Vector3 nextCorner = path.corners[1];

                                if (!WouldBeVisibleAtPosition(nextCorner))
                                {
                                    agent.isStopped = false;
                                    agent.SetDestination(player.position);
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
                                            float safeDist = Mathf.Max(0f, dist - 0.5f);
                                            Vector3 safePosition = currentPos + directionToCorner * safeDist;
                                            agent.SetDestination(safePosition);
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        // Face towards player
                        Vector3 faceDir = (player.position - transform.position);
                        faceDir.y = 0f;
                        if (faceDir.sqrMagnitude > 0.01f)
                        {
                            Quaternion targetRot = Quaternion.LookRotation(faceDir);
                            transform.rotation = Quaternion.Slerp(
                                transform.rotation,
                                targetRot,
                                Time.deltaTime * 10f);
                        }
                    }
                }
            }
            else
            {
                isActivated = false;
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            if (isActivated && player != null && HeartbeatManager.Instance != null)
            {
                HeartbeatManager.Instance.UpdateHeartbeat(distanceToPlayer);
            }
        }
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
