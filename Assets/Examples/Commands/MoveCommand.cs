using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Moves <see cref="LerpCommand{TValue}.Context"/> in local space. Deriving from <see cref="LerpCommand{TValue}"/>
    /// gives it a duration and an optional easing curve for free.
    /// </summary>
    [Serializable]
    public class MoveCommand : LerpCommand<Vector3>
    {
    }

    public class MoveCommandProcessor : LerpCommandProcessor<MoveCommand, Vector3>
    {
        protected override Vector3 Lerp(Vector3 from, Vector3 to, float t) => Vector3.LerpUnclamped(from, to, t);

        protected override void Apply(Transform context, Vector3 value) => context.localPosition = value;
    }
}
