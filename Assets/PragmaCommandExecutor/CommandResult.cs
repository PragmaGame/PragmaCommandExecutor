using System;

namespace Pragma.CommandExecutor
{
    public readonly struct CommandResult
    {
        public static CommandResult Completed => new(CommandOutcome.Completed, null);
        public static CommandResult Cancelled => new(CommandOutcome.Cancelled, null);

        public CommandOutcome Outcome { get; }
        public Exception Exception { get; }

        public bool IsCompleted => Outcome == CommandOutcome.Completed;
        public bool IsCancelled => Outcome == CommandOutcome.Cancelled;
        public bool IsFaulted => Outcome == CommandOutcome.Faulted;

        public CommandResult(CommandOutcome outcome, Exception exception)
        {
            Outcome = outcome;
            Exception = exception;
        }

        public static CommandResult Faulted(Exception exception) => new(CommandOutcome.Faulted, exception);

        public override string ToString() => IsFaulted ? $"{Outcome}: {Exception?.Message}" : Outcome.ToString();
    }
}
