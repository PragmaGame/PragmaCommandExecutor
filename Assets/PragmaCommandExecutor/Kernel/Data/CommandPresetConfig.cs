using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Data shared by every <see cref="Command{TContext, TData}"/> that uses this preset. Unity cannot create an asset of a
    /// generic type: declare a non-generic subclass with <c>[CreateAssetMenu]</c> for each <typeparamref name="TData"/>.
    /// </summary>
    public abstract class CommandPresetConfig<TData> : ScriptableObject
    {
        [field: SerializeField] public TData Data { get; private set; } = CommandData.Create<TData>();
    }
}
