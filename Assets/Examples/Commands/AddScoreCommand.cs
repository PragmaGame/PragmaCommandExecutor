using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// A game service that processors depend on. In a real project it would come from a DI container.
    /// </summary>
    public class ScoreBoard
    {
        public int Score { get; private set; }

        public void Add(int amount)
        {
            Score += amount;
        }
    }

    [Serializable]
    public class AddScoreCommand : ICommand
    {
        [field: SerializeField] public int Amount { get; set; }

        public void Reset()
        {
            Amount = 0;
        }
    }

    /// <summary>
    /// Has no parameterless constructor, so the default <see cref="ActivatorFactory"/> cannot create it:
    /// the executor gets an <see cref="IObjectFactory"/> that knows how, see <see cref="ScoreObjectFactory"/>.
    /// </summary>
    public class AddScoreCommandProcessor : ICommandProcessor<AddScoreCommand>, IStatelessCommandProcessor
    {
        private readonly ScoreBoard _scoreBoard;

        public AddScoreCommandProcessor(ScoreBoard scoreBoard)
        {
            _scoreBoard = scoreBoard;
        }

        public CommandStatus Start(AddScoreCommand command)
        {
            _scoreBoard.Add(command.Amount);
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Creates the processors that need dependencies and returns false for everything else, so the executor
    /// falls back to <see cref="ActivatorFactory"/>. An adapter over a DI container looks the same.
    /// </summary>
    public class ScoreObjectFactory : IObjectFactory
    {
        private readonly ScoreBoard _scoreBoard;

        public ScoreObjectFactory(ScoreBoard scoreBoard)
        {
            _scoreBoard = scoreBoard;
        }

        public bool TryCreate(Type type, out object instance)
        {
            if (type == typeof(AddScoreCommandProcessor))
            {
                instance = new AddScoreCommandProcessor(_scoreBoard);
                return true;
            }

            instance = null;
            return false;
        }
    }
}
