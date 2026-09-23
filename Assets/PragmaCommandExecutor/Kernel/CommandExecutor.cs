using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    public partial class CommandExecutor : ICommandExecutor, IDisposable
    {
        // Keyed by command type: one lookup per started command finds both the processor type and its pool.
        private readonly Dictionary<Type, ProcessorPool> _processorPools = new();
        private readonly IObjectFactory _defaultFactory = new ActivatorFactory();
        private IObjectFactory _factory;

        private readonly Dictionary<Type, Stack<ICommand>> _commandPools = new();

        private readonly List<TreeRunner> _runners = new();
        private readonly Stack<TreeRunner> _runnersPool = new();
        private readonly Stack<ProcessorNode> _processorNodesPool = new();
        private readonly Stack<GroupNode> _groupNodesPool = new();

        private readonly bool _autoTick;
        private bool _isTicking;
        private bool _isDisposed;

        internal long TickIndex { get; private set; }

        /// <summary>
        /// Creates an executor that is ticked automatically from the player loop until <see cref="Dispose"/> is called.
        /// </summary>
        public CommandExecutor(IObjectFactory factory, IEnumerable<ICommandRegistrationContext> registrationContexts)
            : this(factory, registrationContexts, autoTick: true)
        {
        }

        internal CommandExecutor(IObjectFactory factory, IEnumerable<ICommandRegistrationContext> registrationContexts, bool autoTick)
        {
            _factory = factory ?? _defaultFactory;

            if (registrationContexts != null)
            {
                foreach (var context in registrationContexts)
                {
                    foreach (var pair in context.Registrations)
                    {
                        AddRegistration(pair.Key, pair.Value);
                    }
                }
            }

            _autoTick = autoTick;

            if (autoTick)
            {
                CommandExecutorPlayerLoop.Register(this);
            }
        }

        public void SetFactory(IObjectFactory factory)
        {
            _factory = factory ?? _defaultFactory;
        }

        public void AddRegistration(Type commandType, Type processorType)
        {
            if (_processorPools.TryGetValue(commandType, out var pool) && pool.ProcessorType == processorType)
            {
                return;
            }

            // Runs that still hold a processor of the replaced registration return it to the detached pool.
            _processorPools[commandType] = new ProcessorPool(processorType);
        }

        public void AddRegistration<TCommand, TProcessor>()
            where TCommand : ICommand
            where TProcessor : ICommandProcessor
        {
            AddRegistration(typeof(TCommand), typeof(TProcessor));
        }

        public TCommand RentCommand<TCommand>() where TCommand : ICommand, new()
        {
            if (_commandPools.TryGetValue(typeof(TCommand), out var pool) && pool.TryPop(out var command))
            {
                return (TCommand)command;
            }

            return new TCommand();
        }

        public void ReleaseCommand(ICommand command, HashSet<ICommand> excluded = null)
        {
            if (command is CommandGroup group)
            {
                foreach (var child in group.Commands)
                {
                    if (excluded != null && excluded.Contains(child))
                    {
                        continue;
                    }

                    ReleaseCommand(child, excluded);
                }
            }

            command.Reset();

            var commandType = command.GetType();

            if (!_commandPools.TryGetValue(commandType, out var pool))
            {
                pool = new Stack<ICommand>();
                _commandPools[commandType] = pool;
            }

            pool.Push(command);
        }

        public CommandHandle Execute(ICommand command)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return Launch(command, null, default, false, null);
        }

        public CommandHandle Execute(IReadOnlyList<ICommand> commands, GroupMode mode)
        {
            if (commands is null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            return Launch(null, commands, mode, false, null);
        }

        public CommandHandle ExecuteAndRelease(ICommand command, HashSet<ICommand> excluded = null)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return Launch(command, null, default, true, excluded);
        }

        public CommandBuilder GetBuilder(GroupMode mode) => new(this, mode);

        /// <summary>
        /// Cancels every running command tree and detaches the executor from the player loop.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_autoTick)
            {
                CommandExecutorPlayerLoop.Unregister(this);
            }

            for (var i = 0; i < _runners.Count; i++)
            {
                _runners[i].Cancel();
            }

            if (!_isTicking)
            {
                RecycleFinished();
            }
        }

        internal void Tick(float deltaTime)
        {
            if (_isDisposed)
            {
                return;
            }

            if (_isTicking)
            {
                throw new InvalidOperationException($"{nameof(CommandExecutor)}.{nameof(Tick)} is not re-entrant");
            }

            _isTicking = true;
            TickIndex++;

            try
            {
                // Runs started during this tick are only started, their first Tick happens next frame.
                var count = _runners.Count;

                for (var i = 0; i < count; i++)
                {
                    _runners[i].Tick(deltaTime);
                }
            }
            finally
            {
                _isTicking = false;
                RecycleFinished();
            }
        }

        internal ProcessorPool GetProcessorPool(ICommand command)
        {
            var commandType = command.GetType();

            if (!_processorPools.TryGetValue(commandType, out var pool))
            {
                throw new ArgumentException($"Command processor for type '{commandType}' not found");
            }

            return pool;
        }

        internal ICommandProcessor RentProcessor(ProcessorPool pool)
        {
            if (pool.TryRent(out var processor))
            {
                return processor;
            }

            var processorType = pool.ProcessorType;

            if (!(_factory.TryCreate(processorType, out var instance) || _defaultFactory.TryCreate(processorType, out instance))
                || instance is not ICommandProcessor created)
            {
                throw new InvalidOperationException($"Command processor '{processorType}' cannot be created");
            }

            pool.OnCreated(created);
            return created;
        }

        internal CommandNode CreateNode(ICommand command, TreeRunner runner)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command), "Command group contains a null command");
            }

            if (command is CommandGroup group)
            {
                return CreateGroupNode(group.Commands, group.Mode, group.Repeat, runner);
            }

            var node = _processorNodesPool.Count > 0 ? _processorNodesPool.Pop() : new ProcessorNode(this);
            node.Setup(runner, command);
            return node;
        }

        internal GroupNode CreateGroupNode(
            IReadOnlyList<ICommand> commands,
            GroupMode mode,
            int repeat,
            TreeRunner runner)
        {
            var node = _groupNodesPool.Count > 0 ? _groupNodesPool.Pop() : new GroupNode(this);
            node.Setup(runner, commands, mode, repeat);
            return node;
        }

        internal void ReleaseNode(ProcessorNode node)
        {
            _processorNodesPool.Push(node);
        }

        internal void ReleaseNode(GroupNode node)
        {
            _groupNodesPool.Push(node);
        }

        private CommandHandle Launch(
            ICommand command,
            IReadOnlyList<ICommand> commands,
            GroupMode mode,
            bool releaseCommand,
            HashSet<ICommand> excluded)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(CommandExecutor));
            }

            var runner = _runnersPool.Count > 0 ? _runnersPool.Pop() : new TreeRunner(this);

            // Starts synchronously: instant commands at the head of the tree run inside this call.
            runner.Start(command, commands, mode, releaseCommand, excluded);

            // The executor was disposed by one of the commands that just ran.
            if (_isDisposed)
            {
                runner.Cancel();
            }

            if (runner.IsFinished)
            {
                _runnersPool.Push(runner);
                return default;
            }

            _runners.Add(runner);
            return new CommandHandle(runner);
        }

        private void RecycleFinished()
        {
            var write = 0;

            for (var read = 0; read < _runners.Count; read++)
            {
                var runner = _runners[read];

                if (runner.IsFinished)
                {
                    _runnersPool.Push(runner);
                    continue;
                }

                _runners[write++] = runner;
            }

            _runners.RemoveRange(write, _runners.Count - write);
        }
    }
}
