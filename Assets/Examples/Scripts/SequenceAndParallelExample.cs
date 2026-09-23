using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Builder basics: a sequential group runs its children one after another, a parallel group starts them together
    /// and completes with the longest one, <c>repeat</c> replays a group, and a curve eases a lerp.
    /// </summary>
    public class SequenceAndParallelExample : Example
    {
        [SerializeField] private AnimationCurve _jumpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public override string Title => "Sequence & parallel";
        public override string Description => "Jumps up, spins while pulsing twice, lands and logs to the console.";

        protected override CommandHandle Run()
        {
            var ground = Vector3.zero;
            var air = ground + Vector3.up * 1.5f;

            return Executor.GetBuilder(GroupMode.Sequential)
                .JoinMove(Target, ground, air, 0.4f, _jumpCurve)
                .JoinGroup(GroupMode.Parallel, parallel => parallel
                    .JoinRotate(Target, Vector3.zero, new Vector3(0f, 360f, 0f), 1f)
                    .JoinGroup(GroupMode.Sequential, pulse => pulse
                        .JoinScale(Target, Vector3.one, Vector3.one * 1.4f, 0.25f)
                        .JoinScale(Target, Vector3.one * 1.4f, Vector3.one, 0.25f), repeat: 1))
                .JoinMove(Target, air, ground, 0.4f, _jumpCurve)
                .JoinLog("Sequence & parallel: landed", LogType.Log)
                .Execute();
        }
    }
}
