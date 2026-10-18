using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// A tree authored as a graph: <c>Presets/Pulse.commandtree</c> opens in Graph Toolkit (Unity 6000.6+) and imports as
    /// a <see cref="CommandTreePreset"/>. Its targets are keys — graph variables — that <see cref="CommandTreePreset.Build"/>
    /// looks up in the arguments, so one preset serves any number of objects.
    /// </summary>
    public class TreePresetExample : Example
    {
        [SerializeField] private CommandTreePreset _preset;

        private readonly Dictionary<string, object> _args = new();
        private int _pulses;

        public override string Title => "Tree preset";
        public override string Description => "Builds a tree from the Pulse graph for this target. Open Presets/Pulse.commandtree to edit it.";

        protected override void Awake()
        {
            base.Awake();

            // The keys the graph declares as variables, each with its type: see GetRequiredKeys().
            _args["target"] = Target;
            _args["done"] = (Action)(() => _pulses++);
        }

        public override void DrawControls()
        {
            GUILayout.Label($"Pulses: {_pulses}");
        }

        protected override CommandHandle Run()
        {
            // Build creates a new tree on every call; ExecuteAndRelease returns its commands to the pools afterwards.
            return Executor.ExecuteAndRelease(_preset.Build(_args));
        }
    }
}
