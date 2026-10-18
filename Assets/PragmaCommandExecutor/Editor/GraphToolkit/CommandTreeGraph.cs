using System;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// A <see cref="CommandTreePreset"/> authored as a graph: command nodes wired into groups and into the Output
    /// node, and graph variables wired into the target ports, each variable name being a key of the preset. The
    /// <c>.commandtree</c> file imports as a <see cref="CommandTreePreset"/>.
    /// </summary>
    [Serializable]
    [Graph(EXTENSION, GraphOptions.None)]
    public class CommandTreeGraph : Graph
    {
        public const string EXTENSION = "commandtree";

        [MenuItem("Assets/Create/Pragma/Command Executor/Tree Preset Graph")]
        private static void CreateAsset()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<CommandTreeGraph>("CommandTreePresetGraph");
        }

        public override void OnGraphChanged(GraphLogger graphLogger)
        {
            var converter = CommandTreeGraphConverter.Convert(this);
            foreach (var issue in converter.Issues)
            {
                if (issue.IsError)
                {
                    graphLogger.LogError(issue.Message, issue.Node);
                }
                else
                {
                    graphLogger.LogWarning(issue.Message, issue.Node);
                }
            }
        }

        public override bool IsConnectionAllowed(IPort output, IPort input)
        {
            if (!base.IsConnectionAllowed(output, input))
            {
                return false;
            }

            var source = output.GetNode();
            var target = input.GetNode();

            // The Out port of a command node goes only into a link input, and a link input takes only an Out port.
            var isLinkOutput = source is CommandTreeGraphNode && output.Name == CommandTreeGraphNode.OUTPUT_PORT;
            var port = (target as CommandTreeGraphNode)?.Schema.FindPort(input.Name);
            var isLinkInput = target is RootGraphNode || port is { IsLink: true };
            if (isLinkOutput || isLinkInput)
            {
                return isLinkOutput && isLinkInput;
            }

            if (port == null)
            {
                return true;
            }

            // A key is the name of a variable; a value is inline or a constant.
            return port.IsKey ? source is IVariableNode : source is not IVariableNode;
        }
    }
}
