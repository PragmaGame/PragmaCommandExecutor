using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Keeps running until <see cref="Condition"/> returns true. The condition is checked when the command starts
    /// and then once per frame, so an already satisfied condition completes it immediately.
    /// </summary>
    public class WaitUntilCommand : ICommand
    {
        public Func<bool> Condition { get; set; }

        public void Reset()
        {
            Condition = null;
        }
    }

    /// <summary>
    /// A running processor: it remembers the condition between frames, so it is pooled per run
    /// (one instance per concurrently running command) instead of being stateless.
    /// </summary>
    public class WaitUntilCommandProcessor : ICommandProcessor<WaitUntilCommand>
    {
        private Func<bool> _condition;

        public CommandStatus Start(WaitUntilCommand command)
        {
            _condition = command.Condition;
            return Tick(0f);
        }

        public CommandStatus Tick(float deltaTime)
        {
            return _condition == null || _condition() ? CommandStatus.Completed : CommandStatus.Running;
        }

        public void Cleanup(bool interrupted)
        {
            if (interrupted)
            {
                Debug.Log("WaitUntil: the run was cancelled while waiting");
            }

            // The processor goes back to the pool: drop the reference to the caller's delegate.
            _condition = null;
        }
    }
}
