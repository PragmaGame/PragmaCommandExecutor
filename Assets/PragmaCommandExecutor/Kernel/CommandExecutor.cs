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

        // A copy: changing the settings passed to the constructor does not affect the executor.
        private readonly CommandExecutorSettings _settings;

        private bool _isTicking;
        private bool _isDisposed;

        internal long TickIndex { get; private set; }

        // The only constructor on purpose: DI containers such as VContainer pick the one with the most parameters.
        // VContainer ignores default parameter values, so it needs a registered CommandExecutorSettings instance.
        /// <summary>
        /// Creates an executor configured by <paramref name="settings"/> (<c>null</c> — the defaults: ticked by the player
        /// loop at the beginning of <c>Update</c> on scaled time). An auto-ticked executor is ticked until
        /// <see cref="Dispose"/> is called.
        /// </summary>
        public CommandExecutor(
            IObjectFactory factory,
            IEnumerable<ICommandRegistrationContext> registrationContexts,
            CommandExecutorSettings settings = null)
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

            _settings = settings?.Clone() ?? new CommandExecutorSettings();

            if (_settings.IsAutoTicked)
            {
                CommandExecutorPlayerLoop.Register(this, _settings.UpdatePhase);
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

            var isStateless = typeof(IStatelessCommandProcessor).IsAssignableFrom(processorType);

            if (isStateless == typeof(IStatefulCommandProcessor).IsAssignableFrom(processorType))
            {
                throw new ArgumentException(
                    $"Command processor '{processorType}' must implement either {nameof(IStatelessCommandProcessor)} " +
                    $"or {nameof(IStatefulCommandProcessor)}",
                    nameof(processorType));
            }

            // Runs that still hold a processor of the replaced registration return it to the detached pool.
            _processorPools[commandType] = new ProcessorPool(processorType, isStateless);
        }

        public void AddRegistration<TCommand, TProcessor>()
            where TCommand : ICommand
            where TProcessor : ICommandProcessor<TCommand>
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

            if (_settings.IsAutoTicked)
            {
                CommandExecutorPlayerLoop.Unregister(this, _settings.UpdatePhase);
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

        /// <summary>
        /// Advances every running command tree by one frame of <paramref name="deltaTime"/> seconds. Only for an executor
        /// created with <see cref="CommandExecutorSettings.IsAutoTicked"/> set to <c>false</c>: the player loop ticks the
        /// others. Not re-entrant; does nothing after <see cref="Dispose"/>.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_isDisposed)
            {
                return;
            }

            if (_settings.IsAutoTicked)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommandExecutor)} is ticked by the player loop; create it with " +
                    $"{nameof(CommandExecutorSettings)}.{nameof(CommandExecutorSettings.IsAutoTicked)} = false to tick it yourself");
            }

            TickRunners(deltaTime);
        }

        internal void TickFromPlayerLoop(float deltaTime, float unscaledDeltaTime)
        {
            if (_isDisposed)
            {
                return;
            }

            TickRunners(_settings.TimeMode == CommandTimeMode.Unscaled ? unscaledDeltaTime : deltaTime);
        }

        internal IStatefulCommandProcessor RentProcessor(ProcessorPool pool)
        {
            return pool.TryRent(out var processor)
                ? processor
                : CreateProcessor<IStatefulCommandProcessor>(pool.ProcessorType);
        }

        /// <summary>
        /// Returns the node that runs <paramref name="command"/> in <paramref name="runner"/>, not started yet. A command of
        /// a stateless processor needs no node: it is executed right here, and the result is <c>null</c>.
        /// </summary>
        internal CommandNode CreateNodeOrExecute(ICommand command, TreeRunner runner)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command), "Command group contains a null command");
            }

            if (command is CommandGroup group)
            {
                return CreateGroupNode(group.Commands, group.Mode, group.Repeat, runner);
            }

            var pool = GetProcessorPool(command);

            if (pool.IsStateless)
            {
                (pool.Shared ??= CreateProcessor<IStatelessCommandProcessor>(pool.ProcessorType)).Execute(command);
                return null;
            }

            var node = _processorNodesPool.Count > 0 ? _processorNodesPool.Pop() : new ProcessorNode(this);
            node.Setup(runner, command, pool);
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

        private void TickRunners(float deltaTime)
        {
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

        private ProcessorPool GetProcessorPool(ICommand command)
        {
            var commandType = command.GetType();

            if (!_processorPools.TryGetValue(commandType, out var pool))
            {
                throw new ArgumentException($"Command processor for type '{commandType}' not found");
            }

            return pool;
        }

        private TProcessor CreateProcessor<TProcessor>(Type processorType) where TProcessor : class
        {
            if (!(_factory.TryCreate(processorType, out var instance) || _defaultFactory.TryCreate(processorType, out instance))
                || instance is not TProcessor processor)
            {
                throw new InvalidOperationException($"Command processor '{processorType}' cannot be created");
            }

            return processor;
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
