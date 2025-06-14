using System;
using System.Collections.Generic;
using UnityEditor;
using ZLinq;

namespace Xprees.EditorTools.Extensions
{
    public static class ReflectionExtensions
    {
        /// Returns all implementations of the interface or class
        /// Supports both Unity Editor and runtime environments. Might not work in AOT builds.
        public static List<T> GetImplementations<T>()
        {
#if UNITY_EDITOR
            // TypeCache is much faster than AppDomain.GetAssemblies() in Unity Editor
            var types = TypeCache
                .GetTypesDerivedFrom<T>()
                .AsValueEnumerable();
#else
            var type = typeof(T);
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .AsValueEnumerable()
                .SelectMany(s => s.GetTypes())
                .Where(p => type.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);
#endif

            return types
                .Select(Activator.CreateInstance)
                .Cast<T>()
                .ToList();
        }
    }
}