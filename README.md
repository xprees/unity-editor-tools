# Editor Tools

[![NPM Version](https://img.shields.io/npm/v/cz.xprees.editor-tools)](https://www.npmjs.com/package/cz.xprees.editor-tools)

## Feature Overview

- **Read-Only** Attribute: A custom attribute that can be applied to fields to make them read-only in the Unity Editor.
- **Expandable** Attribute: A custom attribute that allows fields to be displayed in an expandable manner in the Unity Editor.
- **Editor scripting Utilities**
    - PrefabExtensions
    - ReflectionExtensions

### Editor Tools Menu

- **Menu/Reload Domain**: A menu item that reloads the Unity domain, useful for testing and development purposes and if editor tools are not working
  initialized properly.
- **Remove Missing Scripts**: A menu item that removes missing scripts from selected GameObjects in the Unity Editor.
- **Optimize - Combine Meshes**: A menu item that combines meshes of selected GameObjects to lower # of render batches for better performance.

## Installation

Install the package using npm scoped registry in `Project Settings > Package Manager > Scoped Registries`

[Unity Docs - Install a UPM package from a Git URL](https://docs.unity3d.com/Documentation/Manual/upm-ui-giturl.html)

```json
{
    "name": "NPM - xprees",
    "url": "https://registry.npmjs.org",
    "scopes": [
        "cz.xprees",
        "com.dbrizov.naughtyattributes"
    ]
}

```

Then simply install the package using the Unity Package Manager using the _NPM - xprees_ scope or by the package name `cz.xprees.editor-tools`.
