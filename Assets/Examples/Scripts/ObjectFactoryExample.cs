using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Processors with dependencies: the executor creates processors through an <see cref="IObjectFactory"/>, so a
    /// processor can take services in its constructor. The command is registered on this executor only.
    /// </summary>
    public class ObjectFactoryExample : Example
    {
        private readonly ScoreBoard _scoreBoard = new();

        public override string Title => "Object factory";
        public override string Description => "Adds score three times; AddScoreCommandProcessor receives the ScoreBoard in its constructor.";

        protected override void Awake()
        {
            base.Awake();
            Executor.AddRegistration<AddScoreCommand, AddScoreCommandProcessor>();
        }

        public override void DrawControls()
        {
            GUILayout.Label($"Score: {_scoreBoard.Score}");
        }

        protected override IObjectFactory CreateFactory()
        {
            return new ScoreObjectFactory(_scoreBoard);
        }

        protected override CommandHandle Run()
        {
            return Executor.GetBuilder(GroupMode.Sequential)
                .JoinGroup(GroupMode.Sequential, step => step
                    .JoinAddScore(10)
                    .JoinScale(Target, Vector3.one * 1.3f, Vector3.one, 0.3f)
                    .JoinDelay(0.2f), repeat: 2)
                .Execute();
        }
    }
}
