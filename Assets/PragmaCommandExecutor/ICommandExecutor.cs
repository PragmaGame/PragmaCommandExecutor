using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public interface ICommandExecutor
    {
        public UniTask Execute(List<ICommand> commands, CommandExecuteFormat executeFormat, CancellationToken token = default);
        public UniTask Execute(ICommand command, CancellationToken token);
    }
}