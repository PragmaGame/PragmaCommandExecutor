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
        public List<float> Deltas { get; set; }
        public List<ICommandProcessor> Processors { get; set; }

        public void Reset()
        {
            Name = null;
            Frames = 0;
            ThrowOnStart = false;
            ThrowOnTick = false;
            Log = null;
            Deltas = null;
            Processors = null;
        }
    }

    /// <summary>
    /// Completes after <see cref="ProbeCommand.Frames"/> frames and records its lifecycle into <see cref="ProbeCommand.Log"/>
    /// and the delta of every tick into <see cref="ProbeCommand.Deltas"/>.
    /// </summary>
    public class ProbeProcessor : IStatefulCommandProcessor<ProbeCommand>
    {
        private ProbeCommand _command;
        private int _remaining;
        private bool _isStartTick;

        public void Start(ProbeCommand command)
        {
            _command = command;
            _remaining = command.Frames;
            _isStartTick = true;
            command.Processors?.Add(this);
            Write("start");

            if (command.ThrowOnStart)
            {
                throw new InvalidOperationException(command.Name);
            }
        }

        public CommandStatus Tick(float deltaTime)
        {
            _command.Deltas?.Add(deltaTime);

            // The tick right after Start belongs to the start frame: Frames counts the frames after it.
            if (_isStartTick)
            {
                _isStartTick = false;
                return Evaluate();
            }

            if (_command.ThrowOnTick)
            {
                throw new InvalidOperationException(_command.Name);
            }

            _remaining--;
            return Evaluate();
        }

        public void Cleanup(bool interrupted)
        {
            Write(interrupted ? "interrupt" : "cleanup");
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

    public class StatelessProbeCommand : ICommand
    {
        public List<ICommandProcessor> Processors { get; set; }

        /// <summary>
        /// Invoked inside <see cref="StatelessProbeProcessor.Execute"/>, after the processor is recorded.
        /// </summary>
        public Action OnExecute { get; set; }

        public void Reset()
        {
            Processors = null;
            OnExecute = null;
        }
    }

    /// <summary>
    /// Records the processor instance that served the run.
    /// </summary>
    public class StatelessProbeProcessor : IStatelessCommandProcessor<StatelessProbeCommand>
    {
        public void Execute(StatelessProbeCommand command)
        {
            command.Processors.Add(this);
            command.OnExecute?.Invoke();
        }
    }

    /// <summary>
    /// Implements neither processor contract.
    /// </summary>
    public class ContractlessProcessor : ICommandProcessor<ProbeCommand>
    {
    }

    /// <summary>
    /// Implements both processor contracts.
    /// </summary>
    public class AmbiguousProcessor : IStatelessCommandProcessor<ProbeCommand>, IStatefulCommandProcessor<ProbeCommand>
    {
        public void Execute(ProbeCommand command)
        {
        }

        public void Start(ProbeCommand command)
        {
        }

        public CommandStatus Tick(float deltaTime) => CommandStatus.Completed;

        public void Cleanup(bool interrupted)
        {
        }
    }
}
