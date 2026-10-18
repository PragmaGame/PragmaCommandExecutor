using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Shared <see cref="LerpData{TValue}"/> for any <see cref="LerpCommand{TContext, TValue}"/> of <see cref="Vector3"/>,
    /// such as <see cref="ScaleCommand"/>.
    /// </summary>
    [CreateAssetMenu(fileName = nameof(LerpVector3PresetConfig), menuName = "Pragma/Command Executor/Lerp Vector3 Preset")]
    public class LerpVector3PresetConfig : CommandPresetConfig<LerpData<Vector3>>
    {
    }
}
