using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Root of a single run: owns the runtime node tree, the cancellation flag and the completion listeners.
    /// Pooled by <see cref="CommandExecutor"/>; <see cref="Version"/> is bumped on finish so stale handles are detected.
    /// </summary>
    internal sealed class CommandExecution
    {
        private readonly CommandExecutor _executor;
        private readonly List<Action<CommandResult>> _listeners = new();
        private readonly HashSet<ICommand> _excluded = new();

        private ICommand _command;
        private bool _releaseCommand;
        private CommandNode _root;
        private bool _isProcessing;

        public int Version { get; private set; }
        public bool IsFinished { get; private set; } = true;
        public bool IsCancelRequested { get; private set; }

        public CommandExecution(CommandExecutor executor)
        {
            _executor = executor;
        }

        public void Start(
            ICommand command,
            IReadOnlyList<ICommand> commands,
            CommandExecuteFormat executeFormat,
            bool releaseCommand,
            HashSet<ICommand> excluded)
        {
            IsFinished = false;
            IsCancelRequested = false;

            _command = command;
            _releaseCommand = releaseCommand;

            if (excluded != null)
            {
                _excluded.UnionWith(excluded);
            }

            _isProcessing = true;
            CommandStatus status;

            try
            {
                _root = command != null
                    ? _executor.CreateNode(command, this)
                    : _executor.CreateGroupNode(commands, executeFormat, 0, this);

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

            Interrupt();
            Finish(CommandResult.Cancelled);
        }

        public void AddListener(Action<CommandResult> listener)
        {
            _listeners.Add(listener);
        }

        private void OnStep(CommandStatus status)
        {
            if (IsCancelRequested)
            {
                Interrupt();
                Finish(CommandResult.Cancelled);
            }
            else if (status == CommandStatus.Completed)
            {
                Finish(CommandResult.Completed);
            }
        }

        private void Fault(Exception exception)
        {
            Interrupt();
            Finish(CommandResult.Faulted(exception));
        }

        private void Interrupt()
        {
            _root?.Cancel();
        }

        private void Finish(CommandResult result)
        {
            IsFinished = true;
            Version++;

            try
            {
                _root?.Release();

                if (_releaseCommand)
                {
                    // An empty set would still cost a lookup per released child.
                    _executor.ReleaseCommand(_command, _excluded.Count > 0 ? _excluded : null);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _root = null;
                _command = null;
                _releaseCommand = false;
                _excluded.Clear();
            }

            Notify(result);
        }

        private void Notify(CommandResult result)
        {
            if (_listeners.Count == 0)
            {
                if (result.IsFaulted)
                {
                    Debug.LogException(result.Exception);
                }

                return;
            }

            // The handle is already stale here, so listeners cannot append to this list while it is iterated.
            for (var i = 0; i < _listeners.Count; i++)
            {
                try
                {
                    _listeners[i](result);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _listeners.Clear();
        }
    }
}
