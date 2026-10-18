using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// How a <see cref="LerpCommand{TContext, TValue}"/> animates its context: stored in the command or shared through a
    /// preset such as <see cref="LerpVector3PresetConfig"/>.
    /// </summary>
    [Serializable]
    public class LerpData<TValue>
    {
        public const float DEFAULT_DURATION = 0.2f;

        [field: SerializeField] public TValue From { get; set; }
        [field: SerializeField] public TValue To { get; set; }
        [field: SerializeField] public float Duration { get; set; } = DEFAULT_DURATION;

        /// <summary>
        /// Maps normalized time to the lerp factor. <c>null</c> or a curve without keys means linear.
        /// </summary>
        [field: SerializeField] public AnimationCurve Curve { get; set; }
    }
}
