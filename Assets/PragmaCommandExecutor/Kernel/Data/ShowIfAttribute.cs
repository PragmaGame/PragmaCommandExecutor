using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Shows the field in the inspector only while the bool field <see cref="Condition"/> next to it equals
    /// <see cref="Value"/>. Drawn by <c>ShowIfDrawer</c> of the editor assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class ShowIfAttribute : PropertyAttribute
    {
        public string Condition { get; }
        public bool Value { get; }

        public ShowIfAttribute(string condition, bool value = true)
        {
            Condition = condition;
            Value = value;
        }
    }
}
