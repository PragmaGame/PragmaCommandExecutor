using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;

namespace Pragma.CommandExecutor
{
    public partial class CommandExecutor : ICommandExecutor
    {
        private readonly Dictionary<Type, Type> _registrations;
        private readonly ITypedPool<ICommandProcessor> _processorsPool;
        private readonly ITypedPool<ICommand> _commandsPool;

        public CommandExecutor(IObjectFactory factory, IEnumerable<ICommandRegistrationContext> registrationContexts)
        {
            _registrations = new Dictionary<Type, Type>();
            _processorsPool = new TypedPool<ICommandProcessor>(factory);
            _commandsPool = new TypedPool<ICommand>(null);

            if (registrationContexts == null)
            {
                return;
            }

            foreach (var context in registrationContexts)
            {
                foreach (var pair in context.Registrations)
                {
                    _registrations[pair.Key] = pair.Value;
                }
            }
        }

        public void SetFactory(IObjectFactory factory)
        {
            _processorsPool.SetFactory(factory);
        }

        public void AddRegistration(Type commandType, Type processorType)
        {
            _registrations[commandType] = processorType;
        }

        private ICommandProcessor GetProcessor(ICommand command)
        {
            if (command is null)
            {
                throw new ArgumentNullException($"Feedback '{nameof(command)}' is null");
            }

            var commandType = command.GetType();

            if (!_registrations.TryGetValue(commandType, out var processorType))
            {
                throw new ArgumentException($"Feedback processor for type '{commandType}' not found");
            }

            return _processorsPool.Get(processorType);
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

        public void AddRegistration<TCommand, TProcessor>()
            where TCommand : ICommand
            where TProcessor : ICommandProcessor
        {
            AddRegistration(typeof(TCommand), typeof(TProcessor));
        }

        private async UniTask ExecuteConcrete(ICommand command, CancellationToken token = default)
        {
            var processor = GetProcessor(command);

            try
            {
                await processor.Execute(command, token).SuppressCancellationThrow();
            }
            finally
            {
                _processorsPool.Release(processor);
            }
        }

        public async UniTask Execute(List<ICommand> commands, CommandExecuteFormat executeFormat,
            CancellationToken token = default)
        {
            if (executeFormat == CommandExecuteFormat.Parallel)
            {
                var tasks = ListPool<UniTask>.Get();

                try
                {
                    foreach (var command in commands)
                    {
                        tasks.Add(Execute(command, token));
                    }

                    var cancelled = await UniTask.WhenAll(tasks).SuppressCancellationThrow();

                    if (cancelled)
                    {
                        throw new OperationCanceledException(token);
                    }
                }
                finally
                {
                    ListPool<UniTask>.Release(tasks);
                }
            }
            else
            {
                foreach (var command in commands)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    var cancelled = await Execute(command, token).SuppressCancellationThrow();

                    if (cancelled)
                    {
                        break;
                    }
                }
            }
        }

        public UniTask Execute(ICommand command, CancellationToken token)
        {
            if (command is CommandGroup group)
            {
                return PlayGroup(group, token);
            }

            return ExecuteConcrete(command, token);
        }

        private async UniTask PlayGroup(CommandGroup group, CancellationToken token)
        {
            var counter = -1;

            while (true)
            {
                var frame = Time.frameCount;

                var cancelled = await Execute(group.Commands, group.ExecuteFormat, token).SuppressCancellationThrow();

                if (cancelled || token.IsCancellationRequested || ++counter == group.Loop)
                {
                    return;
                }

                // A group whose children all complete synchronously (zero-duration lerps, callbacks, logs)
                // would spin here forever inside a single frame when it loops. Yielding only when the
                // iteration consumed no frames keeps the timing of every other group untouched.
                if (Time.frameCount == frame)
                {
                    await UniTask.Yield(token, cancelImmediately: true).SuppressCancellationThrow();
                }
            }
        }

        public async UniTask Execute<TCommand>(Action<TCommand> builder, CancellationToken token = default)
            where TCommand : ICommand
        {
            var feedback = _commandsPool.Get<TCommand>();

            builder?.Invoke(feedback);

            try
            {
                await Execute(feedback, token);
            }
            finally
            {
                _commandsPool.Release(feedback);
            }
        }

        public CommandBuilder GetBuilder(CommandExecuteFormat executeFormat) => new(this, executeFormat);
    }
}
