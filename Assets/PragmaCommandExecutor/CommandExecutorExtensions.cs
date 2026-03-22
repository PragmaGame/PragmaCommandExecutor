using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public static class CommandExecutorExtensions
    {
        public static UniTask Execute(this ICommandExecutor executor, CommandGroup group, CancellationToken token)
        {
            return executor.Execute(group.Commands, group.ExecuteFormat, token);
        }
        
        public static UniTask Execute(this ICommandExecutor executor, CommandGraph graph, CancellationToken token = default)
        {
            return graph.Root == null ? UniTask.CompletedTask : executor.Execute(graph.Root, token);
        }
    }
}