// EnableMeshReadWrite.cs  — Editor-only utility
// Run once from the menu: Tools → Enable Read/Write on Tile Meshes
// This is REQUIRED for runtime NavMesh baking to work in a build.
// Unity's NavMeshSurface needs CPU-readable mesh data at runtime; the FBX
// import setting "Read/Write Enabled" is off by default and must be turned on.

using UnityEngine;
using UnityEditor;
using System.IO;

public static class EnableMeshReadWrite
{
    [MenuItem("Tools/Enable Read-Write on Tile Meshes (Run Before Build)")]
    public static void EnableAll()
    {
        // Folders to scan — add more paths here if you have tiles elsewhere
        string[] searchFolders = new[]
        {
            "Assets/Asset/BackroomsLikeAsset",
        };

        string[] guids = AssetDatabase.FindAssets("t:Model", searchFolders);

        int total   = 0;
        int changed = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            total++;

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();   // reimports the asset with the new setting
                changed++;
                Debug.Log($"[EnableMeshReadWrite] Enabled Read/Write: {path}");
            }
        }

        Debug.Log($"[EnableMeshReadWrite] Done. {changed} of {total} models updated.");
        EditorUtility.DisplayDialog(
            "Mesh Read/Write",
            $"Finished!\n\n{changed} model(s) updated (Read/Write enabled).\n{total - changed} already had it enabled.\n\nYou can now rebuild the game.",
            "OK");
    }
}
