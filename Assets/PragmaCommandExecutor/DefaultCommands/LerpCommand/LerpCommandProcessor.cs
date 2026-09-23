using UnityEngine;

namespace Pragma.CommandExecutor
{
    public abstract class LerpCommandProcessor<TCommand, TValue> : ICommandProcessor<TCommand>
        where TCommand : LerpCommand<TValue>
    {
        private TCommand _command;
        private AnimationCurve _curve;
        private float _elapsed;

        public CommandStatus Start(TCommand command)
        {
            _command = command;
            _elapsed = 0f;

            // Unity deserializes a curve left unset in the inspector as an empty one.
            var curve = command.Curve;
            _curve = curve != null && curve.length > 0 ? curve : null;

            return Evaluate();
        }

        public CommandStatus Tick(float deltaTime)
        {
            _elapsed += deltaTime;
            return Evaluate();
        }

        public void Shutdown()
        {
            _command = default;
            _curve = null;
            _elapsed = 0f;
        }

        protected abstract TValue Lerp(TValue from, TValue to, float t);
        protected abstract void Apply(Transform context, TValue value);

        private CommandStatus Evaluate()
        {
            var context = _command.Context;

            if (context == null)
            {
                return CommandStatus.Completed;
            }

            var progress = _command.Duration > 0f ? Mathf.Clamp01(_elapsed / _command.Duration) : 1f;
            var t = _curve?.Evaluate(progress) ?? progress;

            Apply(context, Lerp(_command.From, _command.To, t));

            return progress < 1f ? CommandStatus.Running : CommandStatus.Completed;
        }
    }
}