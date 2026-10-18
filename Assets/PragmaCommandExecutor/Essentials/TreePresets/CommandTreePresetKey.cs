using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// The name under which a <see cref="CommandTreePreset"/> node finds a target of type <typeparamref name="T"/> in
    /// the arguments of <see cref="CommandTreePreset.Build"/>.
    /// </summary>
    [Serializable]
    public struct CommandTreePresetKey<T> : ICommandTreePresetKey
    {
        [SerializeField] private string _name;

        public CommandTreePresetKey(string name)
        {
            _name = name;
        }

        public string Name => _name;

        Type ICommandTreePresetKey.ValueType => typeof(T);

        /// <summary>
        /// The value of the key in <paramref name="args"/>. A <c>null</c> value is returned as is.
        /// </summary>
        /// <exception cref="KeyNotFoundException"><paramref name="args"/> has no value for the key.</exception>
        /// <exception cref="InvalidCastException">The value is not a <typeparamref name="T"/>.</exception>
        public T Resolve(IReadOnlyDictionary<string, object> args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            if (string.IsNullOrEmpty(_name))
            {
                throw new InvalidOperationException($"Tree preset key of {typeof(T).Name} has no name");
            }

            if (!args.TryGetValue(_name, out var value))
            {
                throw new KeyNotFoundException($"Tree preset arguments have no '{_name}' ({typeof(T).Name})");
            }

            if (value == null)
            {
                return default;
            }

            if (value is T typed)
            {
                return typed;
            }

            throw new InvalidCastException(
                $"Tree preset argument '{_name}' is {value.GetType().Name}, expected {typeof(T).Name}");
        }

        public override string ToString() => $"{_name} ({typeof(T).Name})";

        public static implicit operator CommandTreePresetKey<T>(string name) => new(name);
    }

    internal interface ICommandTreePresetKey
    {
        string Name { get; }
        Type ValueType { get; }
    }

    /// <summary>
    /// A key a <see cref="CommandTreePreset"/> looks up, and the type of the value it expects.
    /// </summary>
    public readonly struct CommandTreePresetKeyInfo : IEquatable<CommandTreePresetKeyInfo>
    {
        public string Name { get; }
        public Type Type { get; }

        public CommandTreePresetKeyInfo(string name, Type type)
        {
            Name = name;
            Type = type;
        }

        public bool Equals(CommandTreePresetKeyInfo other) => Name == other.Name && Type == other.Type;
        public override bool Equals(object obj) => obj is CommandTreePresetKeyInfo other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Name, Type);
        public override string ToString() => $"{Name} ({Type?.Name})";
    }
}
