using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Moves its <see cref="Command{TContext, TData}.Context"/> in local space. Deriving from
    /// <see cref="LerpCommand{TContext, TValue}"/> gives it a duration, an optional easing curve and shared presets
    /// (<see cref="LerpVector3PresetConfig"/>) for free.
    /// </summary>
    [Serializable]
    public class MoveCommand : LerpCommand<Transform, Vector3>
    {
    }

    public class MoveCommandProcessor : LerpCommandProcessor<MoveCommand, Transform, Vector3>
    {
        protected override Vector3 Lerp(Vector3 from, Vector3 to, float t) => Vector3.LerpUnclamped(from, to, t);

        protected override void Apply(Transform context, Vector3 value) => context.localPosition = value;
    }
}
