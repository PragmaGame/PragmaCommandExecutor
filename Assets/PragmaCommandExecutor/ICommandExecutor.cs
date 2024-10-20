using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public interface ICommandExecutor
    {
        public UniTask Execute(IEnumerable<ICommand> commands, CancellationToken token,
            ExecuteFormat format = ExecuteFormat.Parallel);
        public UniTask Execute(ICommand command, CancellationToken token);
    }
}