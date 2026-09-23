using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    public partial class CommandExecutor : ICommandExecutor, IDisposable
    {
        // Keyed by command type: one lookup per started command finds both the processor type and its pool.
        private readonly Dictionary<Type, ProcessorSlot> _slots = new();
        private readonly IObjectFactory _defaultFactory = new ActivatorFactory();
        private readonly ITypedPool<ICommand> _commandsPool;
        private IObjectFactory _factory;

        private readonly List<CommandExecution> _executions = new();
        private readonly Stack<CommandExecution> _executionsPool = new();
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
            _commandsPool = new TypedPool<ICommand>(null);

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
            if (_slots.TryGetValue(commandType, out var slot) && slot.ProcessorType == processorType)
            {
                return;
            }

            // Runs that still hold a processor of the replaced registration return it to the detached slot.
            _slots[commandType] = new ProcessorSlot(processorType);
        }

        public void AddRegistration<TCommand, TProcessor>()
            where TCommand : ICommand
            where TProcessor : ICommandProcessor
        {
            AddRegistration(typeof(TCommand), typeof(TProcessor));
        }

        public TCommand GetCommand<TCommand>() where TCommand : ICommand
        {
            return _commandsPool.Get<TCommand>();
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
            _commandsPool.Release(command);
        }

        public CommandHandle Execute(ICommand command)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return Launch(command, null, default, false, null);
        }

        public CommandHandle Execute(IReadOnlyList<ICommand> commands, CommandExecuteFormat executeFormat)
        {
            if (commands is null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            return Launch(null, commands, executeFormat, false, null);
        }

        public CommandHandle ExecuteAndRelease(ICommand command, HashSet<ICommand> excluded = null)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return Launch(command, null, default, true, excluded);
        }

        public CommandBuilder GetBuilder(CommandExecuteFormat executeFormat) => new(this, executeFormat);

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

            for (var i = 0; i < _executions.Count; i++)
            {
                _executions[i].Cancel();
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
                var count = _executions.Count;

                for (var i = 0; i < count; i++)
                {
                    _executions[i].Tick(deltaTime);
                }
            }
            finally
            {
                _isTicking = false;
                RecycleFinished();
            }
        }

        internal ProcessorSlot GetProcessorSlot(ICommand command)
        {
            var commandType = command.GetType();

            if (!_slots.TryGetValue(commandType, out var slot))
            {
                throw new ArgumentException($"Command processor for type '{commandType}' not found");
            }

            return slot;
        }

        internal ICommandProcessor RentProcessor(ProcessorSlot slot)
        {
            if (slot.TryRent(out var processor))
            {
                return processor;
            }

            var processorType = slot.ProcessorType;

            if (!(_factory.TryCreate(processorType, out var instance) || _defaultFactory.TryCreate(processorType, out instance))
                || instance is not ICommandProcessor created)
            {
                throw new InvalidOperationException($"Command processor '{processorType}' cannot be created");
            }

            slot.OnCreated(created);
            return created;
        }

        internal CommandNode CreateNode(ICommand command, CommandExecution execution)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command), "Command group contains a null command");
            }

            if (command is CommandGroup group)
            {
                return CreateGroupNode(group.Commands, group.ExecuteFormat, group.Loop, execution);
            }

            var node = _processorNodesPool.Count > 0 ? _processorNodesPool.Pop() : new ProcessorNode(this);
            node.Setup(execution, command);
            return node;
        }

        internal GroupNode CreateGroupNode(
            IReadOnlyList<ICommand> commands,
            CommandExecuteFormat executeFormat,
            int loop,
            CommandExecution execution)
        {
            var node = _groupNodesPool.Count > 0 ? _groupNodesPool.Pop() : new GroupNode(this);
            node.Setup(execution, commands, executeFormat, loop);
            return node;
        }

        internal void ReturnNode(ProcessorNode node)
        {
            _processorNodesPool.Push(node);
        }

        internal void ReturnNode(GroupNode node)
        {
            _groupNodesPool.Push(node);
        }

        private CommandHandle Launch(
            ICommand command,
            IReadOnlyList<ICommand> commands,
            CommandExecuteFormat executeFormat,
            bool releaseCommand,
            HashSet<ICommand> excluded)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(CommandExecutor));
            }

            var execution = _executionsPool.Count > 0 ? _executionsPool.Pop() : new CommandExecution(this);

            // Starts synchronously: instant commands at the head of the tree run inside this call.
            execution.Start(command, commands, executeFormat, releaseCommand, excluded);

            // The executor was disposed by one of the commands that just ran.
            if (_isDisposed)
            {
                execution.Cancel();
            }

            if (execution.IsFinished)
            {
                _executionsPool.Push(execution);
                return default;
            }

            _executions.Add(execution);
            return new CommandHandle(execution);
        }

        private void RecycleFinished()
        {
            var write = 0;

            for (var read = 0; read < _executions.Count; read++)
            {
                var execution = _executions[read];

                if (execution.IsFinished)
                {
                    _executionsPool.Push(execution);
                    continue;
                }

                _executions[write++] = execution;
            }

            _executions.RemoveRange(write, _executions.Count - write);
        }
    }
}
