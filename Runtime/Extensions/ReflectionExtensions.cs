using System;
using System.Collections.Generic;
using System.Linq;

namespace Xprees.EditorTools.Extensions
{
    public static class ReflectionExtensions
    {
        /// Returns all implementations of the interface or class
        public static List<T> GetImplementations<T>()
        {
            var type = typeof(T);
            var types = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => type.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract)
                .ToList();

            return types
                .Select(Activator.CreateInstance)
                .Cast<T>()
                .ToList();
        }
    }
}