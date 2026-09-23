using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// UniTask front-end for the tick based executor. Keeps the previous awaitable API working:
    /// <c>await executor.Execute(command, token)</c>, <c>await builder.Execute(token)</c>, and makes
    /// <see cref="CommandHandle"/> itself awaitable (<c>await executor.Execute(command)</c>).
    /// Cancelling the token cancels the run; the await then completes without throwing, as before.
    /// </summary>
    public static class CommandExecutorUniTaskExtensions
    {
        public static UniTask ToUniTask(this CommandHandle handle, CancellationToken cancellationToken = default)
        {
            return CommandHandlePromise.Create(handle, cancellationToken);
        }

        public static UniTask.Awaiter GetAwaiter(this CommandHandle handle)
        {
            return handle.ToUniTask().GetAwaiter();
        }

        public static UniTask Execute(this ICommandExecutor executor, ICommand command, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.CompletedTask;
            }

            return executor.Execute(command).ToUniTask(cancellationToken);
        }

        public static UniTask Execute(
            this ICommandExecutor executor,
            IReadOnlyList<ICommand> commands,
            GroupMode mode,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.CompletedTask;
            }

            return executor.Execute(commands, mode).ToUniTask(cancellationToken);
        }

        public static UniTask Execute<TCommand>(
            this ICommandExecutor executor,
            Action<TCommand> builder,
            CancellationToken cancellationToken)
            where TCommand : ICommand
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.CompletedTask;
            }

            return executor.Execute<TCommand>(builder).ToUniTask(cancellationToken);
        }

        public static UniTask Execute(this CommandBuilder builder, CancellationToken cancellationToken)
        {
            return builder.Execute().ToUniTask(cancellationToken);
        }
    }
}
