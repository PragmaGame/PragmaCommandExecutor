using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>Builds a <see cref="ScaleCommand"/>.</summary>
    [Serializable]
    public class ScaleTreeNode : CommandTreeNode<ScaleCommand, Transform, LerpData<Vector3>>
    {
    }
}
