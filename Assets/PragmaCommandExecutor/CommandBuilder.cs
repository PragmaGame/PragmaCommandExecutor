using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Pragma.CommandExecutor
{
    public struct CommandBuilder
    {
        private readonly ICommandExecutor _executor;
        private readonly CommandGroup _root;

        private List<CommandGroup> _stack;
        private HashSet<ICommand> _externalCommands;
        
        public CommandBuilder(ICommandExecutor executor, CommandExecuteFormat executeFormat)
        {
            _executor = executor;
            _stack = ListPool<CommandGroup>.Get();
            _externalCommands = HashSetPool<ICommand>.Get();

            var root = executor.GetCommand<CommandGroup>();
            root.ExecuteFormat = executeFormat;
            _root = root;
            _stack.Add(root);
        }
        
        public CommandBuilder Join<TCommand>(Action<TCommand> command) where TCommand : ICommand, new()
        {
            var instance = _executor.GetCommand<TCommand>();
            command?.Invoke(instance);
            _stack[^1].Commands.Add(instance);
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
        
        public CommandBuilder JointGroup(CommandExecuteFormat executeFormat, Action<CommandBuilder> builder, int loop = 0)
        {
            var group = _executor.GetCommand<CommandGroup>();
            group.ExecuteFormat = executeFormat;
            group.Loop = loop;
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
        
        public CommandBuilder JoinDelay(float duration)
        {
            var instance = _executor.GetCommand<DelayCommand>();

            instance.Duration = duration;

            _stack[^1].Commands.Add(instance);
            return this;
        }
        
        public CommandBuilder JoinCallback(Action callback)
        {
            var instance = _executor.GetCommand<CallbackCommand>();

            instance.Callback = callback;

            _stack[^1].Commands.Add(instance);
            return this;
        }
        
        public CommandBuilder JoinLog(string message , LogType logType)
        {
            var instance = _executor.GetCommand<LogCommand>();

            instance.Message = message;
            instance.LogType = logType;

            _stack[^1].Commands.Add(instance);
            return this;
        }
        
        public CommandBuilder JoinScale(
            Transform context,
            Vector3 from,
            Vector3 to,
            float duration,
            AnimationCurve curve = null)
        {
            var instance = _executor.GetCommand<ScaleCommand>();
            instance.Context = context;
            instance.From = from;
            instance.To = to;
            instance.Duration = duration;
            instance.Curve = curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
            _stack[^1].Commands.Add(instance);
            return this;
        }
    }
}