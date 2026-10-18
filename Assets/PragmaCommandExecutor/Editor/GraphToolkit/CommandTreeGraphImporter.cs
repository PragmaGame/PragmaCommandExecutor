using System.IO;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Imports a <c>.commandtree</c> graph as a <see cref="CommandTreePreset"/>. A graph with errors imports as a
    /// preset without a root, and the errors are logged on the asset. A graph that edits a <c>.asset</c> preset imports
    /// as a <see cref="CommandTreeEditingGraph"/> instead, so that it cannot be referenced in place of the preset.
    /// </summary>
    [ScriptedImporter(1, CommandTreeGraph.EXTENSION)]
    internal sealed class CommandTreeGraphImporter : ScriptedImporter
    {
        [SerializeField, HideInInspector] private string _editedPresetGuid;

        /// <summary>The GUID of the <c>.asset</c> preset this graph edits, or empty for a graph that is a preset.</summary>
        public string EditedPresetGuid
        {
            get => _editedPresetGuid;
            set => _editedPresetGuid = value;
        }

        public bool IsEditingGraph => !string.IsNullOrEmpty(_editedPresetGuid);

        public override void OnImportAsset(AssetImportContext ctx)
        {
            ctx.DependsOnCustomDependency(CommandTreeSchemaDependency.NAME);

            var name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            var root = ImportTree(ctx);

            if (IsEditingGraph)
            {
                // Saving the graph writes its tree into the edited preset: see CommandTreeGraphEditing.
                var editingGraph = ScriptableObject.CreateInstance<CommandTreeEditingGraph>();
                editingGraph.name = name;
                editingGraph.PresetGuid = _editedPresetGuid;
                ctx.AddObjectToAsset(nameof(CommandTreeEditingGraph), editingGraph);
                ctx.SetMainObject(editingGraph);
                return;
            }

            var preset = ScriptableObject.CreateInstance<CommandTreePreset>();
            preset.name = name;
            preset.Root = root;
            ctx.AddObjectToAsset(nameof(CommandTreePreset), preset);
            ctx.SetMainObject(preset);
        }

        private static ICommandTreePresetNode ImportTree(AssetImportContext ctx)
        {
            var graph = GraphDatabase.LoadGraphForImporter<CommandTreeGraph>(ctx.assetPath);
            if (graph == null)
            {
                ctx.LogImportError($"{ctx.assetPath} is not a command tree graph");
                return null;
            }

            // A new graph is empty: it imports as a preset without a root and logs nothing until it has nodes.
            if (graph.NodeCount == 0)
            {
                return null;
            }

            var converter = CommandTreeGraphConverter.Convert(graph);
            foreach (var issue in converter.Issues)
            {
                var message = $"{ctx.assetPath}: {issue.Message}";
                if (issue.IsError)
                {
                    ctx.LogImportError(message);
                }
                else
                {
                    ctx.LogImportWarning(message);
                }
            }

            return converter.HasErrors ? null : converter.Root;
        }
    }

    /// <summary>
    /// Hashes the ports of every graph node into a custom dependency of the graphs, so that they import again when a
    /// tree node type changes its fields.
    /// </summary>
    [InitializeOnLoad]
    internal static class CommandTreeSchemaDependency
    {
        public const string NAME = "Pragma.CommandExecutor/CommandTreeGraphSchema";

        static CommandTreeSchemaDependency()
        {
            var hash = new Hash128();
            foreach (var type in TypeCache.GetTypesDerivedFrom<CommandTreeGraphNode>()
                         .Where(t => !t.IsAbstract)
                         .OrderBy(t => t.FullName, System.StringComparer.Ordinal))
            {
                hash.Append(type.AssemblyQualifiedName);
                var treeNodeType = CommandTreeGraphNode.GetTreeNodeType(type);
                if (treeNodeType != null)
                {
                    hash.Append(TreeNodeSchema.Get(treeNodeType).Describe());
                }
            }

            AssetDatabase.RegisterCustomDependency(NAME, hash);
        }
    }
}
