using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Edits a <see cref="CommandTreePreset"/> stored as a <c>.asset</c> in Graph Toolkit. Open in Graph Toolkit (the
    /// Inspector header, the context menu of the preset, the Project window) builds a graph from the preset next to it;
    /// saving the graph writes its tree into the preset, and the graph is deleted once no window shows it. References to
    /// the preset do not change.
    /// </summary>
    [InitializeOnLoad]
    public static class CommandTreeGraphEditing
    {
        public const string MENU_NAME = "Open in Graph Toolkit";
        private const string SUFFIX = " (editing)";
        private const string HASH_KEY = "Pragma.CommandExecutor.CommandTreeGraphEditing:";
        private const double CHECK_INTERVAL = 1d;
        private const int CHECKS_BEFORE_DELETE = 2;

        private static readonly Regex ReferenceIds = new("\"rid\":\\s*-?\\d+");
        // Editing graphs and how many checks in a row found no window showing them.
        private static readonly Dictionary<string, int> Tracked = new();
        private static double _nextCheck;

        static CommandTreeGraphEditing()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;

            // Without windows every graph counts as closed: batch mode keeps the editing graphs.
            if (!Application.isBatchMode)
            {
                EditorApplication.update += Update;
                EditorApplication.delayCall += TrackExisting;
            }
        }

        /// <summary>
        /// Opens the graph of <paramref name="preset"/>: its own graph for a <c>.commandtree</c> preset, otherwise an
        /// editing graph built from the <c>.asset</c>. Shows a dialog when the tree cannot be shown in a graph.
        /// </summary>
        public static void Open(CommandTreePreset preset)
        {
            if (preset == null || !EditorUtility.IsPersistent(preset))
            {
                return;
            }

            if (!AssetDatabase.IsNativeAsset(preset))
            {
                AssetDatabase.OpenAsset(preset);
                return;
            }

            var presetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(preset));
            string error = null;
            var graphPath = FindEditingGraph(presetGuid) ?? CreateEditingGraph(preset, out error);
            if (graphPath == null)
            {
                EditorUtility.DisplayDialog(MENU_NAME, $"{preset.name} cannot be edited as a graph:\n\n{error}", "OK");
                return;
            }

            Track(graphPath);
            AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(graphPath));
        }

        /// <summary>The editing graph of the preset with <paramref name="presetGuid"/>, or <c>null</c>.</summary>
        internal static string FindEditingGraph(string presetGuid)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(CommandTreeEditingGraph)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadMainAssetAtPath(path) is CommandTreeEditingGraph editingGraph
                    && editingGraph.PresetGuid == presetGuid)
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates the editing graph of <paramref name="preset"/> next to it, or returns <c>null</c> and the reason
        /// when the graph cannot show the tree as it is.
        /// </summary>
        internal static string CreateEditingGraph(CommandTreePreset preset, out string error)
        {
            var errors = CommandTreeGraphBuilder.Validate(preset.Root);
            if (errors.Count > 0)
            {
                error = string.Join("\n", errors.Distinct());
                return null;
            }

            var presetPath = AssetDatabase.GetAssetPath(preset);
            var directory = Path.GetDirectoryName(presetPath)?.Replace('\\', '/');
            var graphPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{preset.name}{SUFFIX}.{CommandTreeGraph.EXTENSION}");
            var graph = GraphDatabase.CreateGraph<CommandTreeGraph>(graphPath);
            CommandTreeGraphBuilder.Build(graph, preset.Root);

            // Saving the graph writes its tree into the preset: a tree the graph would change is not opened.
            var converter = CommandTreeGraphConverter.Convert(graph);
            var difference = FindDifference(SerializeTree(preset.Root), SerializeTree(converter.Root));
            if (difference != null)
            {
                AssetDatabase.DeleteAsset(graphPath);
                error = $"the graph would change the tree: {difference}";
                return null;
            }

            GraphDatabase.SaveGraph(graph);
            // The settings live in the .meta file: the graph file, and so its hash, stay the same.
            SessionState.SetString(HASH_KEY + graphPath, ComputeFileHash(graphPath));
            var importer = (CommandTreeGraphImporter)AssetImporter.GetAtPath(graphPath);
            importer.EditedPresetGuid = AssetDatabase.AssetPathToGUID(presetPath);
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            Track(graphPath);
            error = null;
            return graphPath;
        }

        /// <summary>
        /// Writes the tree of the editing graph at <paramref name="graphPath"/> into its preset. A graph with errors
        /// writes nothing.
        /// </summary>
        internal static bool Save(string graphPath)
        {
            if (AssetImporter.GetAtPath(graphPath) is not CommandTreeGraphImporter { IsEditingGraph: true } importer)
            {
                return false;
            }

            var presetPath = AssetDatabase.GUIDToAssetPath(importer.EditedPresetGuid);
            var preset = AssetDatabase.LoadAssetAtPath<CommandTreePreset>(presetPath);
            if (preset == null)
            {
                Debug.LogWarning($"{graphPath} edits a tree preset that no longer exists: nothing was saved");
                return false;
            }

            var graph = GraphDatabase.LoadGraphForImporter<CommandTreeGraph>(graphPath);
            var converter = graph != null ? CommandTreeGraphConverter.Convert(graph) : null;
            if (converter == null || converter.HasErrors)
            {
                Debug.LogWarning($"{presetPath} was not saved: the graph {graphPath} has errors", preset);
                return false;
            }

            Write(preset, converter.Root);
            return true;
        }

        /// <summary>
        /// Writes <paramref name="root"/> into <paramref name="preset"/>, keeping the node objects where the graph left
        /// their type in place, so that their serialized reference ids and the diff of the asset stay small.
        /// </summary>
        internal static void Write(CommandTreePreset preset, ICommandTreePresetNode root)
        {
            if (SerializeTree(preset.Root) == SerializeTree(root))
            {
                return;
            }

            Undo.RecordObject(preset, "Save Command Tree Graph");
            preset.Root = (ICommandTreePresetNode)Reuse(preset.Root, root, new HashSet<object>(ReferenceComparer.Instance));
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssetIfDirty(preset);
        }

        /// <summary>
        /// Deletes the editing graphs that no window showed for <see cref="CHECKS_BEFORE_DELETE"/> checks in a row.
        /// </summary>
        internal static void CheckWindows(ICollection<string> openGraphPaths)
        {
            foreach (var path in Tracked.Keys.ToList())
            {
                if (openGraphPaths.Contains(path))
                {
                    Tracked[path] = 0;
                }
                else if (++Tracked[path] >= CHECKS_BEFORE_DELETE)
                {
                    Delete(path);
                }
            }
        }

        internal static void OnEditingGraphImported(string path)
        {
            var hash = ComputeFileHash(path);
            var savedHash = SessionState.GetString(HASH_KEY + path, null);
            SessionState.SetString(HASH_KEY + path, hash);
            Track(path);

            // A graph also imports again unchanged: when the editor starts, or when the node types change.
            if (savedHash != null && savedHash != hash)
            {
                Save(path);
            }
        }

        internal static void OnDeleted(string path)
        {
            Tracked.Remove(path);
            SessionState.EraseString(HASH_KEY + path);
        }

        internal static void OnMoved(string from, string to)
        {
            if (Tracked.Remove(from))
            {
                Track(to);
            }

            var hash = SessionState.GetString(HASH_KEY + from, null);
            if (hash != null)
            {
                SessionState.EraseString(HASH_KEY + from);
                SessionState.SetString(HASH_KEY + to, hash);
            }
        }

        private static void Track(string path)
        {
            Tracked.TryAdd(path, 0);
        }

        private static void TrackExisting()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(CommandTreeEditingGraph)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                // After a restart: the next save is a change from the graph as it is now.
                if (SessionState.GetString(HASH_KEY + path, null) == null)
                {
                    SessionState.SetString(HASH_KEY + path, ComputeFileHash(path));
                }

                Track(path);
            }
        }

        private static void Update()
        {
            if (Tracked.Count == 0 || EditorApplication.timeSinceStartup < _nextCheck)
            {
                return;
            }

            _nextCheck = EditorApplication.timeSinceStartup + CHECK_INTERVAL;
            var openGraphPaths = new HashSet<string>();
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window is IGraphWindow { Graph: { } graph })
                {
                    openGraphPaths.Add(GraphDatabase.GetGraphAssetPath(graph));
                }
            }

            CheckWindows(openGraphPaths);
        }

        private static void Delete(string path)
        {
            OnDeleted(path);
            // Only ever an editing graph: a graph that is a preset is the user's asset.
            if (AssetImporter.GetAtPath(path) is CommandTreeGraphImporter { IsEditingGraph: true })
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static object Reuse(object existing, object built, HashSet<object> reused)
        {
            if (existing == null || built == null || existing.GetType() != built.GetType() || !reused.Add(existing))
            {
                return built;
            }

            foreach (var field in TreeNodeSchema.GetSerializedFields(built.GetType()))
            {
                var value = field.GetValue(built);
                if (typeof(ICommandTreePresetNode).IsAssignableFrom(field.FieldType))
                {
                    value = Reuse(field.GetValue(existing), value, reused);
                }
                else if (value is IList children && TreeNodeSchema.TryGetListElementType(field.FieldType, out var elementType)
                                                 && typeof(ICommandTreePresetNode).IsAssignableFrom(elementType))
                {
                    var previous = field.GetValue(existing) as IList;
                    for (var i = 0; i < children.Count; i++)
                    {
                        children[i] = Reuse(previous != null && i < previous.Count ? previous[i] : null, children[i], reused);
                    }
                }

                field.SetValue(existing, value);
            }

            return existing;
        }

        /// <summary>
        /// The tree as Unity serializes it, outside the preset so that its reference ids are numbered in tree order
        /// rather than kept from the asset.
        /// </summary>
        internal static string SerializeTree(ICommandTreePresetNode root)
        {
            var json = EditorJsonUtility.ToJson(new SerializedTree { Root = root }, true);
            var ids = new Dictionary<string, int>();
            return ReferenceIds.Replace(json, match =>
            {
                if (!ids.TryGetValue(match.Value, out var index))
                {
                    index = ids.Count;
                    ids.Add(match.Value, index);
                }

                return $"\"rid\": {index}";
            });
        }

        private static string FindDifference(string expected, string actual)
        {
            if (expected == actual)
            {
                return null;
            }

            var expectedLines = expected.Split('\n');
            var actualLines = actual.Split('\n');
            for (var i = 0; i < Mathf.Min(expectedLines.Length, actualLines.Length); i++)
            {
                if (expectedLines[i] != actualLines[i])
                {
                    return $"'{expectedLines[i].Trim()}' would become '{actualLines[i].Trim()}'";
                }
            }

            return "the trees have different sizes";
        }

        private static string ComputeFileHash(string path)
        {
            return File.Exists(path) ? Hash128.Compute(File.ReadAllBytes(path)).ToString() : string.Empty;
        }

        [Serializable]
        private sealed class SerializedTree
        {
            [SerializeReference] public ICommandTreePresetNode Root;
        }

        [MenuItem("CONTEXT/" + nameof(CommandTreePreset) + "/" + MENU_NAME)]
        private static void OpenFromInspector(MenuCommand command)
        {
            Open(command.context as CommandTreePreset);
        }

        [MenuItem("Assets/" + MENU_NAME, true)]
        private static bool CanOpenSelection()
        {
            return Selection.activeObject is CommandTreePreset or CommandTreeEditingGraph;
        }

        [MenuItem("Assets/" + MENU_NAME, false, 20)]
        private static void OpenSelection()
        {
            switch (Selection.activeObject)
            {
                case CommandTreePreset preset:
                    Open(preset);
                    break;
                case CommandTreeEditingGraph editingGraph:
                    AssetDatabase.OpenAsset(editingGraph);
                    break;
            }
        }

        private static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1)
            {
                return;
            }

            switch (editor.target)
            {
                // A .commandtree preset shows its importer, which gets the button below.
                case CommandTreePreset preset when EditorUtility.IsPersistent(preset) && AssetDatabase.IsNativeAsset(preset):
                    if (GUILayout.Button(MENU_NAME))
                    {
                        Open(preset);
                    }

                    break;
                case CommandTreeGraphImporter importer:
                    if (importer.IsEditingGraph)
                    {
                        EditorGUILayout.HelpBox(
                            $"Edits {AssetDatabase.GUIDToAssetPath(importer.EditedPresetGuid)}: saving the graph writes the " +
                            "tree there. The graph is deleted when its window closes.", MessageType.Info);
                    }

                    if (GUILayout.Button(MENU_NAME))
                    {
                        AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(importer.assetPath));
                    }

                    break;
            }
        }
    }

    internal sealed class CommandTreeGraphEditingPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (var path in deletedAssets)
            {
                CommandTreeGraphEditing.OnDeleted(path);
            }

            for (var i = 0; i < movedAssets.Length; i++)
            {
                CommandTreeGraphEditing.OnMoved(movedFromAssetPaths[i], movedAssets[i]);
            }

            foreach (var path in importedAssets)
            {
                if (path.EndsWith("." + CommandTreeGraph.EXTENSION, StringComparison.OrdinalIgnoreCase)
                    && AssetImporter.GetAtPath(path) is CommandTreeGraphImporter { IsEditingGraph: true })
                {
                    CommandTreeGraphEditing.OnEditingGraphImported(path);
                }
            }
        }
    }
}
