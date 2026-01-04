using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Xprees.EditorTools.Editor.Extensions;

namespace Xprees.EditorTools.Editor.Toolbar
{
    /// Helper class to style main toolbar elements in the Unity Editor 6.3 and newer.
    public static class MainToolbarElementStyler
    {
        /// Helper method to style a main toolbar element by its name (toolbar path).
        public static void StyleElement<T>(string elementName, Action<T> styleAction) where T : VisualElement
        {
            var treatedElementName = elementName.Replace(" ", ""); // Toolbar elements have identifiers without spaces
            EditorApplication.delayCall += () =>
            {
                ApplyStyle(treatedElementName, element =>
                {
                    T targetElement = null;

                    if (element is T typedElement)
                    {
                        targetElement = typedElement;
                    }
                    else
                    {
                        targetElement = element.Query<T>().First();
                    }

                    if (targetElement != null)
                    {
                        styleAction(targetElement);
                    }
                });
            };
        }

        private static void ApplyStyle(string elementName, Action<VisualElement> styleCallback)
        {
            var element = FindElementByName(elementName);
            if (element != null)
            {
                styleCallback(element);
            }
        }

        private static VisualElement FindElementByName(string name)
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in windows)
            {
                var root = window.rootVisualElement;
                if (root == null) continue;

                VisualElement element;
                if ((element = root.FindElementByName(name)) != null) return element;
                if ((element = root.FindElementByTooltip(name)) != null) return element;
            }

            return null;
        }
    }
}