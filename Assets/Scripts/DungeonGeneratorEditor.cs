using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Linq;

[CustomEditor(typeof(ProceduralDungeonGenerator))]
public class DungeonGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        ProceduralDungeonGenerator generator = (ProceduralDungeonGenerator)target;
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Generate Dungeon", GUILayout.Height(30)))
        {
            generator.GenerateDungeon();
        }
        
        if (GUILayout.Button("Clear Dungeon"))
        {
            generator.ClearDungeon();
        }

        EditorGUILayout.Space();
        
        if (GUILayout.Button("Auto-Assign All Tile Prefabs"))
        {
            AutoAssignPrefabs(generator);
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This system understands edge types (Center, Left, Right, Open, Wall)\n" +
            "and only places tiles where edges properly align.\n\n" +
            "Click 'Auto-Assign All Tile Prefabs' to automatically find\n" +
            "and assign all your tile prefabs!",
            MessageType.Info
        );
    }
    
    void AutoAssignPrefabs(ProceduralDungeonGenerator generator)
    {
        // Find all prefabs in the project that start with "Tiles_"
        string[] guids = AssetDatabase.FindAssets("t:Prefab Tiles_", new[] { "Assets" });
        
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("No Prefabs Found", 
                "No tile prefabs found in the project. Make sure they are in the Assets folder.", 
                "OK");
            return;
        }
        
        // Load all tile prefabs
        var allTiles = new System.Collections.Generic.List<GameObject>();
        
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            
            if (prefab != null)
            {
                allTiles.Add(prefab);
            }
        }
        
        // Sort by name for consistency
        allTiles.Sort((a, b) => a.name.CompareTo(b.name));
        
        // Assign to the generator
        SerializedObject serializedObject = new SerializedObject(generator);
        SerializedProperty arrayProp = serializedObject.FindProperty("allTilePrefabs");
        
        if (arrayProp != null)
        {
            arrayProp.ClearArray();
            for (int i = 0; i < allTiles.Count; i++)
            {
                arrayProp.InsertArrayElementAtIndex(i);
                arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = allTiles[i];
            }
            
            serializedObject.ApplyModifiedProperties();
            
            EditorUtility.DisplayDialog("Prefabs Assigned", 
                $"Successfully assigned {allTiles.Count} tile prefabs!\n\n" +
                $"Ready to generate dungeons.", 
                "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Error", 
                "Could not find 'allTilePrefabs' property. Make sure the script is up to date.", 
                "OK");
        }
    }
}
#endif