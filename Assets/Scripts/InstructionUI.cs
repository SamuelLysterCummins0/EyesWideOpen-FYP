using UnityEngine;
using UnityEngine.UI;

public class InstructionUI : MonoBehaviour
{
    public static InstructionUI Instance;
    
    [System.Serializable]
    public class InstructionPanel
    {
        public Item.ItemType itemType;
        public GameObject panel;
        public Button exitButton;
    }
    
    [SerializeField] private InstructionPanel[] instructionPanels;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        
        foreach (var panel in instructionPanels)
        {
            if (panel.exitButton != null)
            {
                panel.exitButton.onClick.AddListener(() => ClosePanel(panel.itemType));
            }
            panel.panel.SetActive(false);
        }
    }

    public void ShowPanel(Item.ItemType itemType)
    {
        foreach (var panel in instructionPanels)
        {
            if (panel.itemType == itemType)
            {
                panel.panel.SetActive(true);
                EnableCursor();
                Time.timeScale = 0f; 
                break;
            }
        }
    }

    public void ClosePanel(Item.ItemType itemType)
    {
        foreach (var panel in instructionPanels)
        {
            if (panel.itemType == itemType)
            {
                panel.panel.SetActive(false);
                DisableCursor();
                Time.timeScale = 1f; 
                break;
            }
        }
    }

    private void EnableCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void DisableCursor()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void OnDestroy()
    {
        foreach (var panel in instructionPanels)
        {
            if (panel.exitButton != null)
            {
                panel.exitButton.onClick.RemoveListener(() => ClosePanel(panel.itemType));
            }
        }
        Time.timeScale = 1f;
    }
}