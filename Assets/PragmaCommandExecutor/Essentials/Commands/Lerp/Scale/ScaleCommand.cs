using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    [Serializable]
    public class ScaleCommand : LerpCommand<Transform, Vector3>
    {
    }
}
