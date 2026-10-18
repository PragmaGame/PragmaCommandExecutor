using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Base of a command with a <see cref="Context"/> and <see cref="Data"/>. The data is stored in the command, or taken
    /// from a <see cref="CommandPresetConfig{TData}"/> asset shared by many commands (<see cref="IsUsePreset"/>), which
    /// the inspector switches between.
    /// <para>
    /// A command that goes back to the pool is <see cref="Reset"/>: the default <see cref="OnResetData"/> creates new data,
    /// override it to reset the data in place and keep pooled commands allocation free.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class Command<TContext, TData> : ICommand<TContext, TData>
    {
        [SerializeField] private TContext _context;
        [SerializeField] private bool _isUsePreset;
        [SerializeField, ShowIf(nameof(_isUsePreset), false)] private TData _data;
        [SerializeField, ShowIf(nameof(_isUsePreset))] private CommandPresetConfig<TData> _preset;

        public TContext Context
        {
            get => _context;
            set => _context = value;
        }

        /// <summary>
        /// The data of the preset when <see cref="IsUsePreset"/> is set, the command's own data otherwise.
        /// </summary>
        /// <exception cref="InvalidOperationException">The command uses a preset, but none is assigned.</exception>
        public TData Data
        {
            get
            {
                if (!_isUsePreset)
                {
                    return _data;
                }

                if (_preset == null)
                {
                    throw new InvalidOperationException($"{GetType().Name} uses a preset, but none is assigned");
                }

                return _preset.Data;
            }
        }

        public bool IsUsePreset => _isUsePreset;
        public CommandPresetConfig<TData> Preset => _preset;

        protected Command()
        {
            _data = CommandData.Create<TData>();
        }

        protected Command(TContext context, TData data)
        {
            _context = context;
            _data = data;
        }

        /// <summary>
        /// Takes <see cref="Data"/> from <paramref name="preset"/> from now on. The preset is shared, not copied: changing
        /// it changes every command that uses it.
        /// </summary>
        public void UsePreset(CommandPresetConfig<TData> preset)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            _isUsePreset = true;
            _preset = preset;
        }

        public virtual void Reset()
        {
            _context = default;

            // Cleared before the data is reset, so that OnResetData never touches the data of a shared preset.
            _isUsePreset = false;
            _preset = null;

            OnResetData();
        }

        /// <summary>
        /// Resets the command's own data when the command goes back to the pool.
        /// </summary>
        protected virtual void OnResetData()
        {
            _data = CommandData.Create<TData>();
        }

        /// <summary>
        /// Replaces the command's own data, which the command then owns, and stops using the preset: <see cref="Data"/>
        /// has no setter, and a value type <typeparamref name="TData"/> cannot be changed through it.
        /// </summary>
        internal void SetData(TData data)
        {
            _isUsePreset = false;
            _preset = null;
            _data = data;
        }
    }

    internal static class CommandData
    {
        /// <summary>
        /// What Unity puts into a serialized field of type <typeparamref name="TData"/>: a new object for a class.
        /// </summary>
        public static TData Create<TData>()
        {
            return typeof(TData).IsValueType ? default : Activator.CreateInstance<TData>();
        }
    }
}
