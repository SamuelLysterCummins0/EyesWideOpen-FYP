using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;

public class PauseMenu : MonoBehaviour
{
   [Header("Menu UI Panels")]
    public GameObject menuUI;
    public GameObject settingsMenuUI;
    public GameObject audioSettingsUI;
    public GameObject saveMenuUI;
    public GameObject loadMenuUI;
    public GameObject instructionsUI;

    [Header("UI Buttons")]
    public Button exitButton;
    public Button settingsButton;
    public Button instructionsButton;
    public Button audioSettingsButton;
    public Button saveSettingsButton;
    public Button saveGameButton;
    public Button loadGameButton;

    [Header("Back Buttons")]
    public Button backFromSettingsButton;
    public Button backFromAudioButton;
    public Button backFromSaveButton;
    public Button backFromLoadButton;
    public Button backFromInstructionsButton;

    [Header("Audio Settings")]
    public AudioMixer audioMixer;
    public Toggle muteToggle;
    public Slider masterVolumeSlider;

    private bool isPaused = false;
    

    private void Start()
    {
        menuUI.SetActive(false);
        settingsMenuUI.SetActive(false);
        audioSettingsUI.SetActive(false);
        saveMenuUI.SetActive(false);
        loadMenuUI.SetActive(false);
        instructionsUI.SetActive(false);

        if (exitButton != null)
            exitButton.onClick.AddListener(ExitGame);
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OpenSettings);
        if (instructionsButton != null)
            instructionsButton.onClick.AddListener(OpenInstructions);

        if (audioSettingsButton != null)
            audioSettingsButton.onClick.AddListener(OpenAudioSettings);
        if (saveSettingsButton != null)
            saveSettingsButton.onClick.AddListener(OpenSaveSettings);
        if (loadGameButton != null)
            loadGameButton.onClick.AddListener(OpenLoadGame);

        if (backFromSettingsButton != null)
            backFromSettingsButton.onClick.AddListener(BackToPauseMenu);
        if (backFromAudioButton != null)
            backFromAudioButton.onClick.AddListener(BackToSettings);
        if (backFromSaveButton != null)
            backFromSaveButton.onClick.AddListener(BackToSettings);
        if (backFromLoadButton != null)
            backFromLoadButton.onClick.AddListener(BackToSettings);
        if (backFromInstructionsButton != null)
            backFromInstructionsButton.onClick.AddListener(BackToPauseMenu);

        if (muteToggle != null)
        {
            muteToggle.onValueChanged.AddListener(ToggleAudio);
        }
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
            masterVolumeSlider.value = 1f;
        }

       
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            ToggleMenu();
        }
    }

    void ToggleMenu()
    {
        Debug.Log("Menu toggled");
        isPaused = !isPaused;
        menuUI.SetActive(isPaused);

        if (settingsMenuUI != null) settingsMenuUI.SetActive(false);
        if (audioSettingsUI != null) audioSettingsUI.SetActive(false);
        if (saveMenuUI != null) saveMenuUI.SetActive(false);
        if (loadMenuUI != null) loadMenuUI.SetActive(false);
        if (instructionsUI != null) instructionsUI.SetActive(false);

        Time.timeScale = isPaused ? 0 : 1;

        if (isPaused)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void OpenSettings()
    {
        Debug.Log("Opening Settings");
        menuUI.SetActive(false);
        settingsMenuUI.SetActive(true);
    }

    void OpenAudioSettings()
    {
        Debug.Log("Opening Audio Settings");
        settingsMenuUI.SetActive(false);
        audioSettingsUI.SetActive(true);
    }

    void OpenSaveSettings()
    {
        Debug.Log("Opening Save Settings");
        settingsMenuUI.SetActive(false);
        saveMenuUI.SetActive(true);
    }

    void OpenLoadGame()
    {
        Debug.Log("Opening Load Game Menu");
        settingsMenuUI.SetActive(false);
        loadMenuUI.SetActive(true);
        
    }

    
    void OpenInstructions()
    {
        Debug.Log("Opening Instructions");
        menuUI.SetActive(false);
        instructionsUI.SetActive(true);
    }

    void BackToPauseMenu()
    {
        Debug.Log("Back to Main Menu");
        if (settingsMenuUI != null) settingsMenuUI.SetActive(false);
        if (audioSettingsUI != null) audioSettingsUI.SetActive(false);
        if (saveMenuUI != null) saveMenuUI.SetActive(false);
        if (loadMenuUI != null) loadMenuUI.SetActive(false);
        if (instructionsUI != null) instructionsUI.SetActive(false);
        menuUI.SetActive(true);
    }

    void BackToSettings()
    {
        Debug.Log("Back to Settings");
        if (audioSettingsUI != null) audioSettingsUI.SetActive(false);
        if (saveMenuUI != null) saveMenuUI.SetActive(false);
        if (loadMenuUI != null) loadMenuUI.SetActive(false);
        settingsMenuUI.SetActive(true);
    }

    

    void ToggleAudio(bool isMuted)
    {
        Debug.Log($"Audio Muted: {isMuted}");
        if (audioMixer != null)
        {
            float volume = isMuted ? -80f : 0f;
            audioMixer.SetFloat("MasterVolume", volume);
        }
    }

    void SetMasterVolume(float volume)
    {
        if (audioMixer != null)
        {
            float dB = volume > 0.0001f ? 20f * Mathf.Log10(volume) : -80f;
            audioMixer.SetFloat("MasterVolume", dB);
        }
    }

    void ExitGame()
    {
        Debug.Log("Exiting Game");
        Application.Quit();
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
}