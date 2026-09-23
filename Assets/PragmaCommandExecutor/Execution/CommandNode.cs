using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Runtime state of one command inside a run. Commands stay pure data, so the same command
    /// (or group) can be executed by several runs at once.
    /// </summary>
    internal abstract class CommandNode
    {
        protected readonly CommandExecutor Executor;
        protected CommandExecution Execution;

        protected CommandNode(CommandExecutor executor)
        {
            Executor = executor;
        }

        public abstract CommandStatus Start();
        public abstract CommandStatus Tick(float deltaTime);

        /// <summary>Interrupts running work. Never throws.</summary>
        public abstract void Cancel();

        /// <summary>Returns the node (and whatever it still holds) to the pools. Never throws.</summary>
        public abstract void Release();
    }

    internal sealed class ProcessorNode : CommandNode
    {
        private ICommand _command;
        private ICommandProcessor _processor;

        public ProcessorNode(CommandExecutor executor) : base(executor)
        {
        }

        public void Setup(CommandExecution execution, ICommand command)
        {
            Execution = execution;
            _command = command;
        }

        public override CommandStatus Start()
        {
            _processor = Executor.GetProcessor(_command);
            return _processor.Start(_command);
        }

        public override CommandStatus Tick(float deltaTime)
        {
            return _processor.Tick(deltaTime);
        }

        public override void Cancel()
        {
            try
            {
                _processor?.Cancel();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public override void Release()
        {
            if (_processor != null)
            {
                try
                {
                    _processor.Shutdown();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }

                Executor.ReleaseProcessor(_processor);
                _processor = null;
            }

            _command = null;
            Execution = null;
            Executor.ReturnNode(this);
        }
    }

    internal sealed class GroupNode : CommandNode
    {
        // Parallel: every child that is still running. Sequence: at most the current child.
        private readonly List<CommandNode> _running = new();

        private IReadOnlyList<ICommand> _commands;
        private CommandExecuteFormat _executeFormat;
        private int _loop;
        private int _completedIterations;
        private int _cursor;
        private long _iterationTick;
        private bool _isRestartPending;

        public GroupNode(CommandExecutor executor) : base(executor)
        {
        }

        public void Setup(CommandExecution execution, IReadOnlyList<ICommand> commands, CommandExecuteFormat executeFormat, int loop)
        {
            Execution = execution;
            _commands = commands;
            _executeFormat = executeFormat;
            _loop = loop;
        }

        public override CommandStatus Start()
        {
            _completedIterations = 0;
            return StartIteration();
        }

        public override CommandStatus Tick(float deltaTime)
        {
            if (_isRestartPending)
            {
                _isRestartPending = false;
                return StartIteration();
            }

            return _executeFormat == CommandExecuteFormat.Parallel
                ? TickParallel(deltaTime)
                : TickSequence(deltaTime);
        }

        public override void Cancel()
        {
            _isRestartPending = false;

            for (var i = 0; i < _running.Count; i++)
            {
                _running[i].Cancel();
            }
        }

        public override void Release()
        {
            for (var i = 0; i < _running.Count; i++)
            {
                _running[i].Release();
            }

            _running.Clear();
            _commands = null;
            _isRestartPending = false;
            Execution = null;
            Executor.ReturnNode(this);
        }

        private CommandStatus StartIteration()
        {
            _iterationTick = Executor.TickIndex;
            _cursor = 0;

            if (_executeFormat == CommandExecuteFormat.Sequence)
            {
                return AdvanceSequence();
            }

            for (var i = 0; i < _commands.Count; i++)
            {
                if (Execution.IsCancelRequested)
                {
                    return CommandStatus.Running;
                }

                StartChild(_commands[i]);
            }

            return _running.Count == 0 ? CompleteIteration() : CommandStatus.Running;
        }

        private CommandStatus AdvanceSequence()
        {
            while (_cursor < _commands.Count)
            {
                if (Execution.IsCancelRequested)
                {
                    return CommandStatus.Running;
                }

                // A child started here only gets its first Tick on the next frame.
                if (StartChild(_commands[_cursor++]) == CommandStatus.Running)
                {
                    return CommandStatus.Running;
                }
            }

            return CompleteIteration();
        }

        private CommandStatus TickSequence(float deltaTime)
        {
            if (_running.Count == 0)
            {
                return AdvanceSequence();
            }

            var child = _running[0];

            if (child.Tick(deltaTime) == CommandStatus.Running)
            {
                return CommandStatus.Running;
            }

            _running.Clear();
            child.Release();

            return AdvanceSequence();
        }

        private CommandStatus TickParallel(float deltaTime)
        {
            for (var i = 0; i < _running.Count; i++)
            {
                if (Execution.IsCancelRequested)
                {
                    return CommandStatus.Running;
                }

                var child = _running[i];

                if (child.Tick(deltaTime) == CommandStatus.Running)
                {
                    continue;
                }

                // Removed before release so that a throwing sibling never leaves a released node in the list.
                _running.RemoveAt(i--);
                child.Release();
            }

            return _running.Count == 0 ? CompleteIteration() : CommandStatus.Running;
        }

        private CommandStatus CompleteIteration()
        {
            // Loop: 0 — single pass, N — N extra passes, negative — endless.
            if (_loop >= 0 && ++_completedIterations > _loop)
            {
                return CommandStatus.Completed;
            }

            // A looping group whose children all complete synchronously (zero-duration lerps, callbacks, logs)
            // would spin forever inside a single tick. Deferring only iterations that consumed no ticks
            // keeps the timing of every other group untouched.
            if (_iterationTick == Executor.TickIndex)
            {
                _isRestartPending = true;
                return CommandStatus.Running;
            }

            return StartIteration();
        }

        private CommandStatus StartChild(ICommand command)
        {
            var node = Executor.CreateNode(command, Execution);
            CommandStatus status;

            try
            {
                status = node.Start();
            }
            catch
            {
                // Tracked so that the faulted run cancels and releases it together with its siblings.
                _running.Add(node);
                throw;
            }

            if (status == CommandStatus.Running)
            {
                _running.Add(node);
            }
            else
            {
                node.Release();
            }

            return status;
        }
    }
}
