using UnityEngine;
using UnityEngine.UI;

public class DeathScreen : MonoBehaviour
{
    public GameObject deathScreen;  // UI element to show death screen
    public Button respawnButton;    // Respawn button

    void Start()
    {
        deathScreen.SetActive(false);  // Hide at start
        respawnButton.onClick.AddListener(OnRespawnClick);  // Add listener to button
    }

    void OnRespawnClick()
    {
        // Call respawn method from GameManager
        GameManager.Instance.Respawn();
    }

    public void ShowDeathScreen()
    {
        deathScreen.SetActive(true);  // Show the death screen
    }
}
