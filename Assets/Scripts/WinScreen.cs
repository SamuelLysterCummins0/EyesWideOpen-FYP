using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Victory screen: slowly fades the screen to black then reveals "You Escaped" text.
///
/// Setup:
///   1. Create a Canvas GameObject, set Sort Order high (e.g. 200).
///   2. Add a child Image covering the full screen (Anchor Stretch/Stretch),
///      set colour to solid black. This is the blackPanel.
///   3. Add a child TMP_Text centred on screen with whatever font/size you want.
///      Set the initial text to "You Escaped".
///   4. Attach this script to the Canvas root.
///   5. Wire up blackPanel and escapedText in the Inspector.
///   6. The Canvas starts INACTIVE — WinScreen activates it when ShowVictory() is called.
///   7. Assign this component to GameManager.winScreen in the Inspector.
/// </summary>
public class WinScreen : MonoBehaviour
{
    public static WinScreen Instance { get; private set; }

    [Header("Black Fade Panel")]
    [Tooltip("Full-screen black Image child. Should start with alpha = 0 in the Inspector.")]
    [SerializeField] private Image blackPanel;
    [Tooltip("How many real-time seconds the screen takes to fade fully to black.")]
    [SerializeField] private float fadeDuration = 2.5f;

    [Header("Escaped Text")]
    [Tooltip("TMP_Text that reads 'You Escaped'. Should start with alpha = 0 in the Inspector.")]
    [SerializeField] private TMP_Text escapedText;
    [Tooltip("Real-time seconds to wait (after fade completes) before the text starts appearing.")]
    [SerializeField] private float textDelay = 0.8f;
    [Tooltip("How many real-time seconds the text takes to fade in.")]
    [SerializeField] private float textFadeDuration = 2f;

    [Header("Return to Menu")]
    [Tooltip("Name of the main menu scene to load after the victory screen.")]
    [SerializeField] private string mainMenuScene = "MainMenu";
    [Tooltip("Real-time seconds to display 'You Escaped' before returning to the main menu.")]
    [SerializeField] private float returnToMenuDelay = 5f;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the canvas and plays the fade-in sequence.
    /// Uses real (unscaled) time so it works even when timeScale is paused.
    /// </summary>
    public void ShowVictory()
    {
        gameObject.SetActive(true);
        StartCoroutine(PlaySequence());
    }

    // ── Sequence ──────────────────────────────────────────────────────────────

    private IEnumerator PlaySequence()
    {
        // ── Reset starting state ───────────────────────────────────────────────
        if (blackPanel != null)
        {
            Color c = blackPanel.color;
            c.a = 0f;
            blackPanel.color = c;
            blackPanel.gameObject.SetActive(true);
        }

        if (escapedText != null)
        {
            Color c = escapedText.color;
            c.a = 0f;
            escapedText.color = c;
            escapedText.gameObject.SetActive(false);
        }

        // ── Fade to black ─────────────────────────────────────────────────────
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            if (blackPanel != null)
            {
                Color c = blackPanel.color;
                c.a = t;
                blackPanel.color = c;
            }
            yield return null;
        }

        if (blackPanel != null)
        {
            Color c = blackPanel.color;
            c.a = 1f;
            blackPanel.color = c;
        }

        // ── Pause before text ─────────────────────────────────────────────────
        yield return new WaitForSecondsRealtime(textDelay);

        // ── Fade in "You Escaped" ─────────────────────────────────────────────
        if (escapedText != null)
        {
            escapedText.gameObject.SetActive(true);
            elapsed = 0f;
            while (elapsed < textFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / textFadeDuration);
                Color c = escapedText.color;
                c.a = t;
                escapedText.color = c;
                yield return null;
            }

            Color final = escapedText.color;
            final.a = 1f;
            escapedText.color = final;
        }

        // Freeze time and unlock cursor once the animation finishes.
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Wait then return to the main menu
        yield return new WaitForSecondsRealtime(returnToMenuDelay);
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuScene);
    }
}
