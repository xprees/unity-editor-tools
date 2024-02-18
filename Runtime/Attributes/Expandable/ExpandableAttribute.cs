using System;
using UnityEngine;

namespace Xprees.EditorTools.Attributes.Expandable
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ExpandableAttribute : PropertyAttribute
    { }
}