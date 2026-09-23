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
        protected TreeRunner Runner;

        protected CommandNode(CommandExecutor executor)
        {
            Executor = executor;
        }

        public abstract CommandStatus Start();
        public abstract CommandStatus Tick(float deltaTime);

        /// <summary>
        /// Returns the node (and whatever it still holds) to the pools. <paramref name="interrupted"/> is true when the run
        /// is torn down while this node is still running. Never throws.
        /// </summary>
        public abstract void Release(bool interrupted);
    }

    internal sealed class ProcessorNode : CommandNode
    {
        private ICommand _command;
        private ProcessorPool _processorPool;
        private ICommandProcessor _processor;

        public ProcessorNode(CommandExecutor executor) : base(executor)
        {
        }

        public void Setup(TreeRunner runner, ICommand command)
        {
            Runner = runner;
            _command = command;
        }

        public override CommandStatus Start()
        {
            _processorPool = Executor.GetProcessorPool(_command);
            _processor = Executor.RentProcessor(_processorPool);
            return _processor.Start(_command);
        }

        public override CommandStatus Tick(float deltaTime)
        {
            return _processor.Tick(deltaTime);
        }

        public override void Release(bool interrupted)
        {
            if (_processor != null)
            {
                try
                {
                    _processor.Cleanup(interrupted);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }

                _processorPool.Release(_processor);
                _processor = null;
            }

            _processorPool = null;
            _command = null;
            Runner = null;
            Executor.ReleaseNode(this);
        }
    }

    internal sealed class GroupNode : CommandNode
    {
        // Parallel: every child that is still running. Sequence: at most the current child.
        private readonly List<CommandNode> _running = new();

        private IReadOnlyList<ICommand> _commands;
        private GroupMode _mode;
        private int _repeat;
        private int _completedIterations;
        private int _cursor;
        private long _iterationTick;
        private bool _isRestartPending;

        public GroupNode(CommandExecutor executor) : base(executor)
        {
        }

        public void Setup(TreeRunner runner, IReadOnlyList<ICommand> commands, GroupMode mode, int repeat)
        {
            Runner = runner;
            _commands = commands;
            _mode = mode;
            _repeat = repeat;
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

            return _mode == GroupMode.Parallel
                ? TickParallel(deltaTime)
                : TickSequential(deltaTime);
        }

        public override void Release(bool interrupted)
        {
            // Only children that are still running are left here: finished ones were released as they completed.
            for (var i = 0; i < _running.Count; i++)
            {
                _running[i].Release(interrupted);
            }

            _running.Clear();
            _commands = null;
            _isRestartPending = false;
            Runner = null;
            Executor.ReleaseNode(this);
        }

        private CommandStatus StartIteration()
        {
            _iterationTick = Executor.TickIndex;
            _cursor = 0;

            if (_mode == GroupMode.Sequential)
            {
                return AdvanceSequence();
            }

            for (var i = 0; i < _commands.Count; i++)
            {
                if (Runner.IsCancelRequested)
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
                if (Runner.IsCancelRequested)
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

        private CommandStatus TickSequential(float deltaTime)
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
            child.Release(false);

            return AdvanceSequence();
        }

        private CommandStatus TickParallel(float deltaTime)
        {
            for (var i = 0; i < _running.Count; i++)
            {
                if (Runner.IsCancelRequested)
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
                child.Release(false);
            }

            return _running.Count == 0 ? CompleteIteration() : CommandStatus.Running;
        }

        private CommandStatus CompleteIteration()
        {
            // Repeat: 0 — single pass, N — N extra passes, negative — endless.
            if (_repeat >= 0 && ++_completedIterations > _repeat)
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
            var node = Executor.CreateNode(command, Runner);
            CommandStatus status;

            try
            {
                status = node.Start();
            }
            catch
            {
                // Tracked so that the faulted run releases it as interrupted together with its siblings.
                _running.Add(node);
                throw;
            }

            if (status == CommandStatus.Running)
            {
                _running.Add(node);
            }
            else
            {
                node.Release(false);
            }

            return status;
        }
    }
}
