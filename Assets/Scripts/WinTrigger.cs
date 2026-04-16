using UnityEngine;

/// <summary>
/// Placed at the level 0 spawn room by SpawnRoomSetup.
/// When the player enters this trigger while the detonation sequence is active,
/// TriggerVictory() fires — the player escaped in time.
/// </summary>
public class WinTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (DetonationManager.Instance != null && DetonationManager.Instance.IsDetonationActive)
        {
            Debug.Log("[WinTrigger] Player reached level 0 spawn room during detonation — triggering victory!");
            GameManager.Instance?.TriggerVictory();
        }
    }
}
