using UnityEngine;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace EnhancedTriggerbox.Component
{
    [AddComponentMenu("")]
    public class ModifyGameobjectResponse : ResponseComponent
    {
        public GameObject obj;
        public string gameObjectName;
        public ModifyType modifyType;
        public float delay = 0f;
        public int selectedComponentIndex = 0;
        public UnityEngine.Component selectedComponent;
        public int componentCount = 0;
        public ReferenceType referenceType;

        public override bool requiresCollisionObjectData
        {
            get { return true; }
        }

        public enum ModifyType
        {
            Destroy,
            Disable,
            Enable,
            DisableComponent,
            EnableComponent,
        }

        public enum ReferenceType
        {
            Null,
            GameObjectReference,
            GameObjectName,
            CollisionGameObject,
        }

#if UNITY_EDITOR
        public override void DrawInspectorGUI()
        {
            referenceType = (ReferenceType)UnityEditor.EditorGUILayout.EnumPopup(new GUIContent("Reference Type", "How you will access a specific gameobject."), referenceType);

            if (referenceType == ReferenceType.GameObjectReference)
            {
                obj = (GameObject)UnityEditor.EditorGUILayout.ObjectField(new GUIContent("GameObject", "The gameobject that will be modified."), obj, typeof(GameObject), true);
            }
            else if (referenceType == ReferenceType.GameObjectName)
            {
                gameObjectName = UnityEditor.EditorGUILayout.TextField(new GUIContent("GameObject Name", "Enter the name here to find and modify."), gameObjectName);
            }

            modifyType = (ModifyType)UnityEditor.EditorGUILayout.EnumPopup(new GUIContent("Modification Type", "Choose how to modify the gameobject or its components."), modifyType);

            if (modifyType == ModifyType.DisableComponent || modifyType == ModifyType.EnableComponent)
            {
                if (obj)
                {
                    List<UnityEngine.Component> components = GetObjectComponents();
                    if (components.Count == 0)
                    {
                        componentCount = 0;
                        return;
                    }
                    if (componentCount != components.Count && selectedComponent != null)
                    {
                        selectedComponentIndex = components.FindIndex(c => c == selectedComponent);
                        componentCount = components.Count;
                    }
                    else if (selectedComponent == null)
                    {
                        selectedComponentIndex = 0;
                    }
                    selectedComponentIndex = UnityEditor.EditorGUILayout.Popup("Select Component", selectedComponentIndex, components.Select(n => n.GetType().ToString()).ToArray());
                    selectedComponent = components[selectedComponentIndex];
                }
            }

            delay = UnityEditor.EditorGUILayout.FloatField(new GUIContent("Delay (Seconds)", "Time to wait before modification."), delay);
        }
#endif

        public override void Validation()
        {
            base.Validation();
            if (delay < 0) delay = 0; // Ensure delay is never negative
        }

        public override bool ExecuteAction(GameObject collisionGameObject)
        {
            if (!obj && !string.IsNullOrEmpty(gameObjectName))
            {
                obj = GameObject.Find(gameObjectName);
            }

            if (!obj)
            {
                Debug.LogError("ModifyGameobjectResponse: No object found to modify.");
                return false;
            }

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(DelayedAction());
                return true;
            }
            else
            {
                Debug.LogWarning("ModifyGameobjectResponse: GameObject is not active in hierarchy.");
                return false;
            }
        }

        private IEnumerator DelayedAction()
        {
            Debug.Log($"Starting delay: {delay} seconds for object: {obj.name}");
            yield return new WaitForSeconds(delay);
            Debug.Log("Executing delayed action now.");

            switch (modifyType)
            {
                case ModifyType.Destroy:
                    Destroy(obj);
                    break;
                case ModifyType.Disable:
                    obj.SetActive(false);
                    break;
                case ModifyType.Enable:
                    obj.SetActive(true);
                    break;
                case ModifyType.DisableComponent:
                case ModifyType.EnableComponent:
                    if (selectedComponent != null)
                    {
                        var propInfo = selectedComponent.GetType().GetProperty("enabled");
                        if (propInfo != null)
                        {
                            propInfo.SetValue(selectedComponent, modifyType == ModifyType.EnableComponent, null);
                        }
                        else
                        {
                            Debug.LogError("Unable to modify component because the 'enabled' property could not be found.");
                        }
                    }
                    break;
            }
        }

        private List<UnityEngine.Component> GetObjectComponents()
        {
            List<UnityEngine.Component> returnList = new List<UnityEngine.Component>();
            foreach (UnityEngine.Component component in obj.GetComponents<UnityEngine.Component>())
            {
                if (component.GetType().GetProperty("enabled") != null && !component.GetType().ToString().Contains("EnhancedTriggerbox.Component"))
                {
                    returnList.Add(component);
                }
            }
            return returnList;
        }
    }
}
