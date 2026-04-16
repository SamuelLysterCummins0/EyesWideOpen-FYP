using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the Player.
/// Detects nearby elevator doors (tag "door") via OverlapSphere — far more
/// reliable than a thin forward raycast against mesh colliders.
/// Shows a "[E] Open door" prompt when in range; pressing E triggers the door.
/// Elevator floor buttons are still handled by a short forward raycast since
/// they are simple collision geometry with known names.
/// </summary>
public class DoorAction : MonoBehaviour
{
    [SerializeField] private float interactionRadius = 2.5f;
    [SerializeField] private float buttonRayDistance = 3f;

    // ── Runtime ─────────────────────────────────────────────────────────────────
    private Door   nearestDoor;
    private Text   uiPrompt;

    // ── Unity Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        BuildPromptUI();
    }

    private void Update()
    {
        FindNearestDoor();
        UpdatePrompt();

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (nearestDoor != null)
            {
                nearestDoor.ActionDoor();
            }
            else
            {
                // Fallback: short forward raycast for elevator floor buttons
                // (these are simple box geometry, not mesh colliders)
                HandleButtonRaycast();
            }
        }
    }

    // ── Door proximity detection ─────────────────────────────────────────────────

    private void FindNearestDoor()
    {
        nearestDoor = null;
        float closestDist = interactionRadius;

        Collider[] hits = Physics.OverlapSphere(transform.position, interactionRadius);
        foreach (Collider col in hits)
        {
            if (!col.CompareTag("door")) continue;

            // Door component may be on the collider's object or a parent
            Door door = col.GetComponent<Door>() ?? col.GetComponentInParent<Door>();
            if (door == null) continue;

            float dist = Vector3.Distance(transform.position, col.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                nearestDoor = door;
            }
        }
    }

    private void UpdatePrompt()
    {
        if (uiPrompt == null) return;
        uiPrompt.gameObject.SetActive(nearestDoor != null);
    }

    // ── Elevator button fallback (unchanged logic) ───────────────────────────────

    private void HandleButtonRaycast()
    {
        RaycastHit hit;
        if (!Physics.Raycast(transform.position, transform.TransformDirection(Vector3.forward),
                             out hit, buttonRayDistance))
            return;

        if (hit.collider == null) return;
        string n = hit.collider.gameObject.name;

        if (n == "Button floor 1") AddElevatorTask(hit, n);
        else if (n == "Button floor 2") AddElevatorTask(hit, n);
        else if (n == "Button floor 3") AddElevatorTask(hit, n);
        else if (n == "Button floor 4") AddElevatorTask(hit, n);
        else if (n == "Button floor 5") AddElevatorTask(hit, n);
        else if (n == "Button floor 6") AddElevatorTask(hit, n);
    }

    private static void AddElevatorTask(RaycastHit hit, string buttonName)
    {
        var passOn = hit.transform.gameObject.GetComponent<pass_on_parent>();
        if (passOn != null)
            passOn.MyParent.GetComponent<evelator_controll>().AddTaskEve(buttonName);
    }

    // ── Prompt UI ────────────────────────────────────────────────────────────────

    private void BuildPromptUI()
    {
        GameObject canvasObj = new GameObject("DoorActionPromptCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject textObj = new GameObject("DoorActionPromptText");
        textObj.transform.SetParent(canvas.transform, false);

        uiPrompt = textObj.AddComponent<Text>();
        uiPrompt.text      = "[E]  Open door";
        uiPrompt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiPrompt.fontSize  = 28;
        uiPrompt.color     = Color.white;
        uiPrompt.alignment = TextAnchor.MiddleCenter;

        RectTransform rect = uiPrompt.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.5f, 0.25f);
        rect.anchorMax        = new Vector2(0.5f, 0.25f);
        rect.sizeDelta        = new Vector2(400f, 50f);
        rect.anchoredPosition = Vector2.zero;

        uiPrompt.gameObject.SetActive(false);
    }
}
