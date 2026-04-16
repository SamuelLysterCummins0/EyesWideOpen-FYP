using UnityEngine;
using UnityEngine.UI;

public class DeathScreen : MonoBehaviour
{
    public GameObject deathScreen;  // UI element to show death screen
    public Button respawnButton;    // Respawn button

    void Start()
    {
        deathScreen.SetActive(false);  // Hide at start
        // GameManager.Start() already adds the respawn listener — don't add a second one here.
    }

    public void ShowDeathScreen()
    {
        deathScreen.SetActive(true);  // Show the death screen
    }
}
