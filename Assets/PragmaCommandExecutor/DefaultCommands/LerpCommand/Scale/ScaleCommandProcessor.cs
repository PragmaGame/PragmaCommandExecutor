using UnityEngine;

namespace Pragma.CommandExecutor
{
    public class ScaleCommandProcessor : LerpCommandProcessor<ScaleCommand, Vector3>
    {
        [UnityEngine.Scripting.RequiredMember]
        public ScaleCommandProcessor()
        {
        }

        protected override Vector3 Lerp(Vector3 from, Vector3 to, float t) => Vector3.LerpUnclamped(from, to, t);

        protected override void Apply(Transform context, Vector3 value) => context.localScale = value;
    }
}