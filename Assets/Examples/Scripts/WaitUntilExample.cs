using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// A custom running command (<see cref="WaitUntilCommand"/>) holds the sequence until a condition holds,
    /// and a custom instant command (<see cref="SetActiveCommand"/>) toggles a GameObject.
    /// </summary>
    public class WaitUntilExample : Example
    {
        [SerializeField] private GameObject _gate;

        private bool _isGateOpen;

        public override string Title => "Wait until";
        public override string Description => "Waits under the lid until the toggle below is on, then removes the lid and rises through.";

        public override void DrawControls()
        {
            _isGateOpen = GUILayout.Toggle(_isGateOpen, "Open the lid");
        }

        protected override CommandHandle Run()
        {
            var bottom = Vector3.zero;
            var top = bottom + Vector3.up * 2.2f;

            Target.localPosition = bottom;

            return Executor.GetBuilder(GroupMode.Sequential)
                .JoinSetActive(_gate, true)
                .JoinWaitUntil(() => _isGateOpen)
                .JoinSetActive(_gate, false)
                .JoinMove(Target, bottom, top, 0.6f)
                .JoinSetActive(_gate, true)
                .Execute();
        }
    }
}
