using UnityEngine;

namespace Pragma.CommandExecutor
{
    public abstract class LerpCommandProcessor<TCommand, TContext, TValue> : IStatefulCommandProcessor<TCommand>
        where TCommand : LerpCommand<TContext, TValue>
        where TContext : class
    {
        private TContext _context;
        private LerpData<TValue> _data;
        private AnimationCurve _curve;
        private float _elapsed;

        public void Start(TCommand command)
        {
            _context = command.Context;
            _data = command.Data;
            _elapsed = 0f;

            // Unity deserializes a curve left unset in the inspector as an empty one.
            var curve = _data.Curve;
            _curve = curve != null && curve.length > 0 ? curve : null;
        }

        public CommandStatus Tick(float deltaTime)
        {
            _elapsed += deltaTime;

            // A destroyed Unity object equals null only through the operator of UnityEngine.Object.
            if (_context is Object unityObject ? unityObject == null : _context == null)
            {
                return CommandStatus.Completed;
            }

            var duration = _data.Duration;
            var progress = duration > 0f ? Mathf.Clamp01(_elapsed / duration) : 1f;
            var t = _curve?.Evaluate(progress) ?? progress;

            Apply(_context, Lerp(_data.From, _data.To, t));

            return progress < 1f ? CommandStatus.Running : CommandStatus.Completed;
        }

        public void Cleanup(bool interrupted)
        {
            // All of them belong to the command, which goes back to its own pool.
            _context = null;
            _data = null;
            _curve = null;
        }

        protected abstract TValue Lerp(TValue from, TValue to, float t);
        protected abstract void Apply(TContext context, TValue value);
    }
}
