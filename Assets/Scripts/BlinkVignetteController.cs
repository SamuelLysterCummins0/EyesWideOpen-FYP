using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class BlinkVignetteController : MonoBehaviour
{

    [System.Serializable]
    public class ScreenBlackEvent : UnityEvent { }

    [Header("Events")]
    public ScreenBlackEvent OnBlinkStart;
    public ScreenBlackEvent OnScreenFullyBlack;

    [Header("References")]
    [SerializeField] private Image vignetteImage;
    [SerializeField] private BlinkDetector blinkDetector;

    [Header("Animation Settings")]
    [SerializeField] private float blinkCloseTime = 0.1f;
    [SerializeField] private float blinkOpenTime = 0.12f;
    [SerializeField] private float minBlinkDuration = 0.2f;

    [Header("Visual Settings")]
    [SerializeField] private AnimationCurve closeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    [SerializeField] private Color vignetteColor = Color.black;

    private bool isAnimating = false;
    private bool wasBlinking = false;
    private float blinkStartTime = 0f;
    private Coroutine currentBlinkRoutine = null;

    void Start()
    {
        if (vignetteImage == null)
        {
            vignetteImage = GetComponent<Image>();
        }

        if (blinkDetector == null)
        {
            blinkDetector = FindObjectOfType<BlinkDetector>();
        }

        // Initialize fully transparent
        SetVignetteAlpha(0f);
    }

    void Update()
    {
        if (blinkDetector == null) return;

        bool isBlinking = blinkDetector.IsBlinking;

        // Detect blink start
        if (isBlinking && !wasBlinking && !isAnimating)
        {
            blinkStartTime = Time.time;

            // Fire blink start event immediately
            OnBlinkStart?.Invoke();

            if (currentBlinkRoutine != null)
            {
                StopCoroutine(currentBlinkRoutine);
            }

            currentBlinkRoutine = StartCoroutine(BlinkAnimation());
        }

        wasBlinking = isBlinking;
    }

    IEnumerator BlinkAnimation()
    {
        isAnimating = true;

        // CLOSE animation (eyes closing)
        float elapsed = 0f;
        while (elapsed < blinkCloseTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / blinkCloseTime;
            float curveValue = closeCurve.Evaluate(progress);

            SetVignetteAlpha(curveValue);

            yield return null;
        }

        SetVignetteAlpha(1f);

        // TRIGGER EVENT: Screen is now fully black
        OnScreenFullyBlack?.Invoke();

        // HOLD at closed (while eyes are closed)
        float holdStartTime = Time.time;
        while (blinkDetector.IsBlinking || (Time.time - holdStartTime) < minBlinkDuration)
        {
            yield return null;
        }

        // Small delay to ensure eyes are actually open
        yield return new WaitForSeconds(0.05f);

        // OPEN animation (eyes opening)
        elapsed = 0f;
        while (elapsed < blinkOpenTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / blinkOpenTime;
            float curveValue = openCurve.Evaluate(progress);

            SetVignetteAlpha(curveValue);

            yield return null;
        }

        SetVignetteAlpha(0f);

        isAnimating = false;
        currentBlinkRoutine = null;
    }

    void SetVignetteAlpha(float alpha)
    {
        Color color = vignetteColor;
        color.a = alpha;
        vignetteImage.color = color;
    }

    // Public method to get total blink animation duration
    public float GetTotalBlinkDuration()
    {
        return blinkCloseTime + minBlinkDuration + blinkOpenTime;
    }

    // Check if blink animation is in "fully closed" state
    public bool IsFullyClosed()
    {
        return vignetteImage.color.a >= 0.99f;
    }

    // Check if any blink animation is currently playing
    public bool IsBlinkAnimating()
    {
        return isAnimating;
    }
}
