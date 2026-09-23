using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    [Serializable]
    public class LerpCommand<TValue> : ICommand
    {
        [field: SerializeField] public Transform Context { get; set; }
        [field: SerializeField] public TValue From { get; set; }
        [field: SerializeField] public TValue To { get; set; }
        [field: SerializeField] public float Duration { get; set; } = 0.2f;

        /// <summary>
        /// Maps normalized time to the lerp factor. <c>null</c> or a curve without keys means linear.
        /// </summary>
        [field: SerializeField] public AnimationCurve Curve { get; set; }

        public void Reset()
        {
            Context = null;
            From = default;
            To = default;
            Duration = 0.2f;
            Curve = null;
        }
    }
}