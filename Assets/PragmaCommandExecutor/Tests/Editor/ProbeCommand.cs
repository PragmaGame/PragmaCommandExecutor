using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor.Tests
{
    public class ProbeCommand : ICommand
    {
        public string Name { get; set; }
        public int Frames { get; set; }
        public bool ThrowOnStart { get; set; }
        public bool ThrowOnTick { get; set; }
        public List<string> Log { get; set; }

        public void Reset()
        {
            Name = null;
            Frames = 0;
            ThrowOnStart = false;
            ThrowOnTick = false;
            Log = null;
        }
    }

    /// <summary>
    /// Completes after <see cref="ProbeCommand.Frames"/> ticks and records its lifecycle into <see cref="ProbeCommand.Log"/>.
    /// </summary>
    public class ProbeProcessor : ICommandProcessor<ProbeCommand>
    {
        private ProbeCommand _command;
        private int _remaining;

        public CommandStatus Start(ProbeCommand command)
        {
            _command = command;
            _remaining = command.Frames;
            Write("start");

            if (command.ThrowOnStart)
            {
                throw new InvalidOperationException(command.Name);
            }

            return Evaluate();
        }

        public CommandStatus Tick(float deltaTime)
        {
            if (_command.ThrowOnTick)
            {
                throw new InvalidOperationException(_command.Name);
            }

            _remaining--;
            return Evaluate();
        }

        public void Cancel()
        {
            Write("cancel");
        }

        public void Shutdown()
        {
            Write("shutdown");
            _command = null;
        }

        private CommandStatus Evaluate()
        {
            if (_remaining > 0)
            {
                return CommandStatus.Running;
            }

            Write("end");
            return CommandStatus.Completed;
        }

        private void Write(string stage)
        {
            _command.Log.Add($"{stage}:{_command.Name}");
        }
    }
}
