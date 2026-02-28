using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Vector2 originalPosition;
    private Transform originalParent;
    private GameObject dragIcon;
    private Canvas canvas;
    private ItemController itemController;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        canvas = GetComponentInParent<Canvas>();
        itemController = GetComponent<ItemController>();

        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        Debug.Log("Begin drag");
        originalPosition = rectTransform.anchoredPosition;
        originalParent = transform.parent;
        
        CreateDragIcon(eventData);
        
        canvasGroup.alpha = 0.6f;
        canvasGroup.blocksRaycasts = false;
    }

    private void CreateDragIcon(PointerEventData eventData)
    {
        
        if (dragIcon != null)
        {
            Destroy(dragIcon);
        }

        dragIcon = new GameObject("DragIcon");
        dragIcon.transform.SetParent(canvas.transform);
        
        Image dragImage = dragIcon.AddComponent<Image>();
        RectTransform dragRect = dragIcon.GetComponent<RectTransform>();
        
        // Add CanvasGroup to drag icon
        CanvasGroup dragIconGroup = dragIcon.AddComponent<CanvasGroup>();
        dragIconGroup.blocksRaycasts = false;
        
        Transform iconTransform = transform.Find("ItemIcon");
        if (iconTransform != null)
        {
            Image originalIcon = iconTransform.GetComponent<Image>();
            if (originalIcon != null && originalIcon.sprite != null)
            {
                dragImage.sprite = originalIcon.sprite;
                dragImage.preserveAspect = true;
                dragImage.color = new Color(1, 1, 1, 0.6f);
            }
        }
        
        dragRect.sizeDelta = new Vector2(50, 50);
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvas.GetComponent<RectTransform>(), 
            eventData.position, eventData.pressEventCamera, out Vector3 worldPoint))
        {
            dragIcon.transform.position = worldPoint;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragIcon != null && canvas != null)
        {
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvas.GetComponent<RectTransform>(), 
                eventData.position, eventData.pressEventCamera, out Vector3 worldPoint))
            {
                dragIcon.transform.position = worldPoint;
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        Debug.Log("End drag");
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;

        
        CleanupDragIcon();

        
        GameObject dropTarget = eventData.pointerEnter;
        DropSlot dropSlot = null;
        
        if (dropTarget != null)
        {
            dropSlot = dropTarget.GetComponent<DropSlot>();
            if (dropSlot == null)
            {
                dropSlot = dropTarget.GetComponentInParent<DropSlot>();
            }
            if (dropSlot == null)
            {
                dropSlot = dropTarget.GetComponentInChildren<DropSlot>();
            }
        }

        
        InventoryDropArea dropArea = null;
        if (dropTarget != null)
        {
            dropArea = dropTarget.GetComponent<InventoryDropArea>();
            if (dropArea == null)
            {
                dropArea = dropTarget.GetComponentInParent<InventoryDropArea>();
            }
        }

        if (dropSlot == null && dropArea == null)
        {
            Debug.Log("Invalid drop target - resetting position");
            rectTransform.SetParent(originalParent);
            rectTransform.anchoredPosition = originalPosition;
        }
    }

    public void CleanupDragIcon()
    {
        if (dragIcon != null)
        {
            Destroy(dragIcon);
            dragIcon = null;
        }
    }

    private void OnDestroy()
    {
        
        CleanupDragIcon();
    }
}