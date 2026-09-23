using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// An endless loop (<see cref="CommandGroup.REPEAT_FOREVER"/>) that only stops when cancelled. Cancel interrupts
    /// the running command where it is, skips the commands that have not started and reports
    /// <see cref="CommandResult.Cancelled"/> to the OnFinished listeners.
    /// </summary>
    public class RepeatAndCancelExample : Example
    {
        [SerializeField] private AnimationCurve _bounceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public override string Title => "Repeat forever & cancel";
        public override string Description => "Bounces until cancelled; Cancel freezes the cube mid-move.";

        protected override CommandHandle Run()
        {
            var bottom = Vector3.zero;
            var top = bottom + Vector3.up * 1.2f;

            Target.localPosition = bottom;

            return Executor.GetBuilder(GroupMode.Sequential)
                .JoinGroup(GroupMode.Sequential, loop => loop
                    .JoinMove(Target, bottom, top, 0.35f, _bounceCurve)
                    .JoinMove(Target, top, bottom, 0.35f, _bounceCurve)
                    .JoinDelay(0.15f), CommandGroup.REPEAT_FOREVER)
                .JoinLog("Never logged: the loop above never completes", LogType.Warning)
                .Execute();
        }
    }
}
