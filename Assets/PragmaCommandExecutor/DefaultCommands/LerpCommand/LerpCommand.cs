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
        [field: SerializeField] public AnimationCurve Curve { get; set; } = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        
        public void Reset()
        {
            Context = null;
            From = default;
            To = default;
            Duration = 0.2f;
            Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }
}