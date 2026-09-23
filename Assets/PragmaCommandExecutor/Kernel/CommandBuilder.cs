using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace Pragma.CommandExecutor
{
    public struct CommandBuilder
    {
        private readonly ICommandExecutor _executor;
        private readonly CommandGroup _root;

        private List<CommandGroup> _stack;
        private HashSet<ICommand> _externalCommands;
        
        public CommandBuilder(ICommandExecutor executor, GroupMode mode)
        {
            _executor = executor;
            _stack = ListPool<CommandGroup>.Get();
            _externalCommands = HashSetPool<ICommand>.Get();

            var root = executor.GetCommand<CommandGroup>();
            root.Mode = mode;
            _root = root;
            _stack.Add(root);
        }
        
        public CommandBuilder Join<TCommand>(Action<TCommand> configure) where TCommand : ICommand, new()
        {
            Join(out TCommand command);
            configure?.Invoke(command);
            return this;
        }

        /// <summary>
        /// Adds a pooled <typeparamref name="TCommand"/> to the current group and hands it out for configuration.
        /// Unlike <see cref="Join{TCommand}(Action{TCommand})"/> it needs no delegate, so a closure over the values is not allocated.
        /// </summary>
        public CommandBuilder Join<TCommand>(out TCommand command) where TCommand : ICommand, new()
        {
            command = _executor.GetCommand<TCommand>();
            _stack[^1].Commands.Add(command);
            return this;
        }

        /// <summary>
        /// Adds an externally owned <see cref="ICommand"/> instance to the current group.
        /// <para>
        /// Commands added via this method are <b>not returned to the pool</b> when <see cref="Execute"/>
        /// completes — the caller retains full ownership of the instance's lifecycle.
        /// </para>
        /// </summary>
        /// <param name="command">The externally owned command instance to add.</param>
        public CommandBuilder Join(ICommand command)
        {
            _stack[^1].Commands.Add(command);
            _externalCommands.Add(command);
            return this;
        }
        
        public CommandBuilder JoinGroup(GroupMode mode, Action<CommandBuilder> builder, int repeat = 0)
        {
            var group = _executor.GetCommand<CommandGroup>();
            group.Mode = mode;
            group.Repeat = repeat;
            _stack[^1].Commands.Add(group);

            _stack.Add(group);

            builder?.Invoke(this);

            _stack.RemoveAt(_stack.Count - 1);

            return this;
        }

        /// <summary>
        /// Starts the built command tree. Pooled commands are released when the run finishes
        /// (completed, cancelled or faulted); commands added via <see cref="Join(ICommand)"/> are left untouched.
        /// </summary>
        public CommandHandle Execute()
        {
            try
            {
                return _executor.ExecuteAndRelease(_root, _externalCommands);
            }
            finally
            {
                ListPool<CommandGroup>.Release(_stack);
                HashSetPool<ICommand>.Release(_externalCommands);
                _stack = null;
                _externalCommands = null;
            }
        }

        /// <summary>
        /// Builds and returns the root <see cref="ICommand"/> without playing it.
        /// <para>
        /// The caller is <b>responsible</b> for releasing the command tree by calling
        /// <c>executor.ReleaseCommand(root, excluded)</c> when it is no longer needed.
        /// Commands added via <see cref="Join(ICommand)"/> will <b>not</b> be released
        /// automatically — their lifecycle remains the caller's responsibility.
        /// </para>
        /// </summary>
        /// <returns>The root <see cref="ICommand"/> of the built tree.</returns>
        public ICommand Build()
        {
            ListPool<CommandGroup>.Release(_stack);
            _stack = null;

            HashSetPool<ICommand>.Release(_externalCommands);
            _externalCommands = null;

            return _root;
        }
    }
}