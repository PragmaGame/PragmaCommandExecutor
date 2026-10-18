using UnityEditor;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// What a <c>.commandtree</c> graph imports as while it edits a <see cref="CommandTreePreset"/> stored as a
    /// <c>.asset</c>: saving the graph writes its tree into that preset, and the graph is deleted when its window
    /// closes. Open in Graph Toolkit on the preset creates it.
    /// </summary>
    public sealed class CommandTreeEditingGraph : ScriptableObject
    {
        [SerializeField] private string _presetGuid;

        /// <summary>The GUID of the edited preset.</summary>
        public string PresetGuid
        {
            get => _presetGuid;
            internal set => _presetGuid = value;
        }

        /// <summary>The edited preset, or <c>null</c> when it was deleted.</summary>
        public CommandTreePreset Preset =>
            AssetDatabase.LoadAssetAtPath<CommandTreePreset>(AssetDatabase.GUIDToAssetPath(_presetGuid));
    }
}
