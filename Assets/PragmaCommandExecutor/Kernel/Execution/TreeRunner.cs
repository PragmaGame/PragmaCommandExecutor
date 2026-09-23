using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Runs one command tree at a time: owns the runtime node tree, the cancellation flag and the completion listeners.
    /// Pooled by <see cref="CommandExecutor"/> and reused for other trees; <see cref="Version"/> is bumped on finish
    /// so stale handles are detected.
    /// </summary>
    internal sealed class TreeRunner
    {
        private readonly CommandExecutor _executor;
        private readonly List<Action<CommandResult>> _listeners = new();
        private readonly List<Action<CommandResult, Exception>> _exceptionListeners = new();
        private readonly HashSet<ICommand> _excluded = new();

        private ICommand _command;
        private bool _releaseCommand;
        private CommandNode _root;
        private bool _isProcessing;

        public int Version { get; private set; }
        public bool IsFinished { get; private set; } = true;
        public bool IsCancelRequested { get; private set; }

        public TreeRunner(CommandExecutor executor)
        {
            _executor = executor;
        }

        public void Start(
            ICommand command,
            IReadOnlyList<ICommand> commands,
            GroupMode mode,
            bool releaseCommand,
            HashSet<ICommand> excluded)
        {
            IsFinished = false;
            IsCancelRequested = false;

            _command = command;
            _releaseCommand = releaseCommand;

            if (excluded != null)
            {
                // Not UnionWith: it takes an IEnumerable<T>, and Unity's Mono boxes the enumerator on every call.
                foreach (var excludedCommand in excluded)
                {
                    _excluded.Add(excludedCommand);
                }
            }

            _isProcessing = true;
            CommandStatus status;

            try
            {
                _root = command != null
                    ? _executor.CreateNode(command, this)
                    : _executor.CreateGroupNode(commands, mode, 0, this);

                status = _root.Start();
            }
            catch (Exception exception)
            {
                _isProcessing = false;
                Fault(exception);
                return;
            }

            _isProcessing = false;
            OnStep(status);
        }

        public void Tick(float deltaTime)
        {
            if (IsFinished)
            {
                return;
            }

            _isProcessing = true;
            CommandStatus status;

            try
            {
                status = _root.Tick(deltaTime);
            }
            catch (Exception exception)
            {
                _isProcessing = false;
                Fault(exception);
                return;
            }

            _isProcessing = false;
            OnStep(status);
        }

        public void Cancel()
        {
            if (IsFinished)
            {
                return;
            }

            // Cancelled from inside its own step (a callback command, a listener of a nested run...):
            // tearing the tree down now would break the nodes that are still on the stack.
            if (_isProcessing)
            {
                IsCancelRequested = true;
                return;
            }

            Finish(CommandResult.Cancelled, isRootRunning: true);
        }

        public void AddListener(Action<CommandResult> listener)
        {
            _listeners.Add(listener);
        }

        public void AddListener(Action<CommandResult, Exception> listener)
        {
            _exceptionListeners.Add(listener);
        }

        private void OnStep(CommandStatus status)
        {
            if (IsCancelRequested)
            {
                // The root may have completed in the very step that requested the cancellation.
                Finish(CommandResult.Cancelled, status == CommandStatus.Running);
            }
            else if (status == CommandStatus.Completed)
            {
                Finish(CommandResult.Completed, isRootRunning: false);
            }
        }

        private void Fault(Exception exception)
        {
            Finish(CommandResult.Faulted, isRootRunning: true, exception);
        }

        private void Finish(CommandResult result, bool isRootRunning, Exception exception = null)
        {
            IsFinished = true;
            Version++;

            try
            {
                _root?.Release(interrupted: isRootRunning);

                if (_releaseCommand)
                {
                    // An empty set would still cost a lookup per released child.
                    _executor.ReleaseCommand(_command, _excluded.Count > 0 ? _excluded : null);
                }
            }
            catch (Exception releaseException)
            {
                Debug.LogException(releaseException);
            }
            finally
            {
                _root = null;
                _command = null;
                _releaseCommand = false;
                _excluded.Clear();
            }

            Notify(result, exception);
        }

        private void Notify(CommandResult result, Exception exception)
        {
            // A fault is reported exactly once: by a listener that takes the exception over, or here.
            if (exception != null && _exceptionListeners.Count == 0)
            {
                Debug.LogException(exception);
            }

            // The handle is already stale here, so listeners cannot append to these lists while they are iterated.
            for (var i = 0; i < _listeners.Count; i++)
            {
                try
                {
                    _listeners[i](result);
                }
                catch (Exception listenerException)
                {
                    Debug.LogException(listenerException);
                }
            }

            for (var i = 0; i < _exceptionListeners.Count; i++)
            {
                try
                {
                    _exceptionListeners[i](result, exception);
                }
                catch (Exception listenerException)
                {
                    Debug.LogException(listenerException);
                }
            }

            _listeners.Clear();
            _exceptionListeners.Clear();
        }
    }
}
