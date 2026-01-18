using UnityEngine;
using UnityEngine.UI;
using SUPERCharacter;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("References")]
    public Transform player;
    public GameObject deathScreen;
    public Button respawnButton;
    public SUPERCharacterAIO playerController;

    private bool isDeathScreenShowing = false;

    void Start()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        respawnButton.onClick.AddListener(Respawn);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (playerController == null && player != null)
            playerController = player.GetComponent<SUPERCharacterAIO>();
    }

    public void PlayerDied()
    {
        if (playerController != null)
            playerController.enabled = false;

        StartCoroutine(HandlePlayerDeath());
    }

    private System.Collections.IEnumerator HandlePlayerDeath()
    {
        yield return new WaitForSeconds(5f);

        deathScreen.SetActive(true);
        isDeathScreenShowing = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        Time.timeScale = 0f;
    }

    public void Respawn()
    {
        deathScreen.SetActive(false);
        isDeathScreenShowing = false;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        Time.timeScale = 1f;

        // Reset player position
        Rigidbody rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Re-enable player controller
        if (playerController != null)
            playerController.enabled = true;
    }

    public bool IsDeathScreenActive() => isDeathScreenShowing;
}
