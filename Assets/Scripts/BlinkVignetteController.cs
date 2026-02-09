using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class BlinkVignetteController : MonoBehaviour
{

    [System.Serializable]
    public class ScreenBlackEvent : UnityEvent { }

    [Header("Events")]
    public ScreenBlackEvent OnScreenFullyBlack;
    public ScreenBlackEvent OnBlinkStart; // Fires the instant a blink is detected, before close animation

    [Header("References")]
    [SerializeField] private Image vignetteImage;
    [SerializeField] private BlinkDetector blinkDetector;

    [Header("Animation Settings")]
    [SerializeField] private float blinkCloseTime = 0.1f;  // Time to close eyes
    [SerializeField] private float blinkOpenTime = 0.12f;  // Time to open eyes
    [SerializeField] private float minBlinkDuration = 0.2f; // Minimum time eyes stay closed

    [Header("Visual Settings")]
    [SerializeField] private AnimationCurve closeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    [SerializeField] private Color vignetteColor = Color.black;

    [Header("Scale Effect (Optional)")]
    [SerializeField] private bool useScaleEffect = true;
    [SerializeField] private float maxScale = 50f; // How much the vignette scales

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

        if (useScaleEffect)
        {
            vignetteImage.transform.localScale = Vector3.one * maxScale;
        }
    }

    void Update()
    {
        if (blinkDetector == null) return;

        bool isBlinking = blinkDetector.IsBlinking;

        // Detect blink start
        if (isBlinking && !wasBlinking && !isAnimating)
        {
            blinkStartTime = Time.time;

            if (currentBlinkRoutine != null)
            {
                StopCoroutine(currentBlinkRoutine);
            }

            OnBlinkStart?.Invoke(); // Fire immediately before any animation — NPC can freeze here
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

            if (useScaleEffect)
            {
                float scale = Mathf.Lerp(maxScale, 1f, curveValue);
                vignetteImage.transform.localScale = Vector3.one * scale;
            }

            yield return null;
        }

        SetVignetteAlpha(1f);
        if (useScaleEffect)
        {
            vignetteImage.transform.localScale = Vector3.one;
        }

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

            if (useScaleEffect)
            {
                float scale = Mathf.Lerp(1f, maxScale, progress);
                vignetteImage.transform.localScale = Vector3.one * scale;
            }

            yield return null;
        }

        SetVignetteAlpha(0f);
        if (useScaleEffect)
        {
            vignetteImage.transform.localScale = Vector3.one * maxScale;
        }

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

    // Returns true any time the blink animation is running (closing, closed, or opening)
    // Use this to prevent NPC movement being visible during any part of a blink
    public bool IsBlinkAnimating()
    {
        return isAnimating;
    }
}