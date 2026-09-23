using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Rotates <see cref="LerpCommand{TValue}.Context"/> between two sets of local Euler angles, so a full turn
    /// is simply <c>(0, 0, 0) → (0, 360, 0)</c>.
    /// </summary>
    [Serializable]
    public class RotateCommand : LerpCommand<Vector3>
    {
    }

    public class RotateCommandProcessor : LerpCommandProcessor<RotateCommand, Vector3>
    {
        protected override Vector3 Lerp(Vector3 from, Vector3 to, float t) => Vector3.LerpUnclamped(from, to, t);

        protected override void Apply(Transform context, Vector3 value) => context.localRotation = Quaternion.Euler(value);
    }
}
