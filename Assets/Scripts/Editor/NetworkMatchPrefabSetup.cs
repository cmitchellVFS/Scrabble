#if UNITY_EDITOR
using Coherence.Toolkit;
using UnityEditor;
using UnityEngine;

namespace Shaderpark.Scrabble.EditorTools
{
    /// <summary>
    /// One-time setup: creates the ScrabbleNetworkMatch networked prefab if it
    /// doesn't exist yet, using the same AddComponent/PrefabUtility APIs the
    /// Editor GUI uses, so CoherenceSync gets built and registered normally
    /// (hand-authoring its serialized YAML would be fragile). Runs automatically
    /// after scripts (re)compile; also available as a menu item.
    /// </summary>
    [InitializeOnLoad]
    internal static class NetworkMatchPrefabSetup
    {
        // Must live under a "Resources" folder so NetworkSession can load it at
        // runtime with Resources.Load -- this project has no scenes to hold a
        // serialized prefab reference in (everything self-bootstraps via code).
        private const string PrefabPath = "Assets/Resources/ScrabbleNetworkMatch.prefab";

        static NetworkMatchPrefabSetup()
        {
            EditorApplication.delayCall += CreateIfMissing;
        }

        // Public entry point so a headless batch run can generate the prefab via
        // -executeMethod (the delayCall above does not fire under -batchmode -quit).
        public static void CreateIfMissingBatch() => CreateIfMissing();

        [MenuItem("Tools/Scrabble/Create Network Match Prefab")]
        private static void CreateIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;

            string dir = System.IO.Path.GetDirectoryName(PrefabPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            GameObject go = new GameObject("ScrabbleNetworkMatch");
            go.AddComponent<CoherenceSync>();
            go.AddComponent<ScrabbleNetworkMatch>();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Scrabble] Created networked match prefab at " + PrefabPath +
                ". Assign it to a NetworkSession component's Match Prefab field, and open the " +
                "CoherenceSync's Configure window once to confirm its [Sync]/[Command] bindings baked correctly.");
        }
    }
}
#endif
