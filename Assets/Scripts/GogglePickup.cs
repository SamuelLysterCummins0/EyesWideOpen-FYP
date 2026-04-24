using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Collider))]
public class GogglePickup : MonoBehaviour
{
    [Header("Gaze / Blink Settings")]
    [SerializeField] private float  rayDistance   = 5f;
    [SerializeField] private float  gazeHitRadius = 0.5f;
    [SerializeField] private string promptMessage = "Blink to pick up goggles";

    [Header("Glow")]
    [SerializeField] private Color glowColor     = new Color(0.5f, 0.2f, 1f); // purple
    [SerializeField] private float glowIntensity = 2.5f;
    [SerializeField] private float pulseSpeed    = 3f;

    private GazeDetector  gazeDetector;
    private BlinkDetector blinkDetector;
    private Camera        playerCamera;

    private Renderer[] itemRenderers;
    private Color[]    originalEmission;
    private Color[]    originalBaseColor;
    private bool[]     supportsEmission;
    private bool[]     usesBaseColor;

    private bool collected   = false;
    private bool wasBlinking = false;

    private Text uiPrompt;

    private void Start()
    {
        gazeDetector  = FindObjectOfType<GazeDetector>();
        blinkDetector = FindObjectOfType<BlinkDetector>();
        playerCamera  = Camera.main;

        itemRenderers    = GetComponentsInChildren<Renderer>();
        originalEmission = new Color[itemRenderers.Length];
        originalBaseColor= new Color[itemRenderers.Length];
        supportsEmission = new bool [itemRenderers.Length];
        usesBaseColor    = new bool [itemRenderers.Length];

        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material mat = itemRenderers[i].material;

            supportsEmission[i] = mat.HasProperty("_EmissionColor");
            if (supportsEmission[i])
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                originalEmission[i] = mat.GetColor("_EmissionColor");
            }

            if (mat.HasProperty("_BaseColor"))
            {
                usesBaseColor[i]     = true;
                originalBaseColor[i] = mat.GetColor("_BaseColor");
            }
            else if (mat.HasProperty("_Color"))
            {
                usesBaseColor[i]     = false;
                originalBaseColor[i] = mat.GetColor("_Color");
            }
            else
            {
                originalBaseColor[i] = Color.white;
            }
        }

        BuildPromptUI();
    }

    private void Update()
    {
        if (collected) return;

        bool gazed = IsGazedAt();
        ShowPrompt(gazed);

        if (gazed)
        {
            PulseGlow();

            bool blinkingNow = blinkDetector != null && blinkDetector.IsBlinking;
            if (blinkingNow && !wasBlinking)
                Collect();
            wasBlinking = blinkingNow;
        }
        else
        {
            ResetGlow();
            wasBlinking = blinkDetector != null && blinkDetector.IsBlinking;
        }
    }

    private void OnDestroy()
    {
        if (uiPrompt != null && uiPrompt.transform.parent != null)
            Destroy(uiPrompt.transform.parent.gameObject);
    }

    private bool IsGazedAt()
    {
        if (gazeDetector == null || !gazeDetector.IsTracking || playerCamera == null)
            return false;

        Ray ray = gazeDetector.GetGazeRay(playerCamera);

        if (Physics.Raycast(ray, out RaycastHit directHit, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (directHit.collider.transform == transform ||
                directHit.collider.transform.IsChildOf(transform))
                return true;
        }

        RaycastHit[] hits = Physics.SphereCastAll(ray, gazeHitRadius, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform))
                return true;
        }

        Vector3 toItem = transform.position - ray.origin;
        float   along  = Vector3.Dot(ray.direction, toItem);
        if (along > 0f && along <= rayDistance)
        {
            if (Vector3.Distance(ray.origin + ray.direction * along, transform.position) <= gazeHitRadius * 2f)
                return true;
        }

        return false;
    }

    private void PulseGlow()
    {
        float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1f);
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;
            if (supportsEmission[i])
                m.SetColor("_EmissionColor", glowColor * glowIntensity * pulse);
            Color tinted = Color.Lerp(originalBaseColor[i], glowColor, pulse * 0.5f);
            if (usesBaseColor[i])             m.SetColor("_BaseColor", tinted);
            else if (m.HasProperty("_Color")) m.SetColor("_Color",     tinted);
        }
    }

    private void ResetGlow()
    {
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            Material m = itemRenderers[i].material;
            if (supportsEmission[i])          m.SetColor("_EmissionColor", originalEmission[i]);
            if (usesBaseColor[i])             m.SetColor("_BaseColor",     originalBaseColor[i]);
            else if (m.HasProperty("_Color")) m.SetColor("_Color",         originalBaseColor[i]);
        }
    }

    private void Collect()
    {
        if (collected) return;
        collected = true;

        ResetGlow();
        ShowPrompt(false);

        if (GoggleController.Instance != null)
            GoggleController.Instance.UnlockGoggles();
        else
            Debug.LogWarning("[GogglePickup] GoggleController.Instance is null.");

        FlashlightHUD.Instance?.ShowNotification("Blink twice fast to put on / off goggles", 6f);

        Destroy(gameObject);
    }

    private void BuildPromptUI()
    {
        GameObject canvasObj = new GameObject($"GogglePickupCanvas_{GetInstanceID()}");
        Canvas canvas        = canvasObj.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder  = 100;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject textObj = new GameObject("PromptText");
        textObj.transform.SetParent(canvas.transform, false);

        uiPrompt           = textObj.AddComponent<Text>();
        uiPrompt.text      = promptMessage;
        uiPrompt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiPrompt.fontSize  = 26;
        uiPrompt.color     = new Color(0.7f, 0.4f, 1f);
        uiPrompt.alignment = TextAnchor.MiddleCenter;

        RectTransform rect    = uiPrompt.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.5f, 0.22f);
        rect.anchorMax        = new Vector2(0.5f, 0.22f);
        rect.sizeDelta        = new Vector2(400, 50);
        rect.anchoredPosition = Vector2.zero;

        uiPrompt.gameObject.SetActive(false);
    }

    private void ShowPrompt(bool show)
    {
        if (uiPrompt != null) uiPrompt.gameObject.SetActive(show);
    }
}
