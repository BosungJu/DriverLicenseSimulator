# AGENTS.md

Guidance for AI coding agents working in this Unity project.

## Project Assumptions

- This is a Unity project. Prefer Unity-native workflows and existing project conventions over introducing external tooling.
- Treat `Assets/`, `Packages/`, and `ProjectSettings/` as the primary project surface.
- Do not modify generated or cache folders such as `Library/`, `Temp/`, `Obj/`, `Logs/`, `Build/`, `Builds/`, or `UserSettings/` unless explicitly requested.
- Keep `.meta` files paired with their assets. When adding, moving, or deleting Unity assets, make sure the corresponding `.meta` files are handled consistently.

## Coding Conventions

- Follow the existing C# style in the repository before applying a new convention.
- Keep gameplay, editor tooling, UI, and data code separated according to the existing folder structure.
- Prefer small, focused MonoBehaviours and ScriptableObjects over large multipurpose classes.
- Avoid introducing global state unless the project already has a clear service, manager, or dependency pattern.
- Use Unity serialization intentionally:
  - Prefer `[SerializeField] private` fields for Inspector-configured dependencies.
  - Avoid public fields unless they are part of an intentional API.
  - Be careful when renaming serialized fields; use `[FormerlySerializedAs]` when preserving scene or prefab data matters.

## Unity Asset Safety

- Be careful with scenes, prefabs, materials, animation controllers, and ScriptableObjects because small text changes can alter serialized references.
- Before editing serialized YAML files directly, inspect the surrounding structure and preserve IDs, GUIDs, and file references.
- Do not regenerate project files, package locks, or large serialized assets unless the task requires it.
- Do not remove unused-looking assets without checking references.

## Packages And Dependencies

- Prefer Unity Package Manager packages already present in `Packages/manifest.json`.
- Do not add new packages or external dependencies unless they are necessary for the task.
- If a package change is needed, update both `Packages/manifest.json` and `Packages/packages-lock.json` when applicable.
- Avoid changing Unity version settings unless explicitly requested.

## Testing And Validation

- For code changes, validate with the narrowest reliable check available:
  - Unity EditMode tests when changing editor, utility, or pure C# logic.
  - Unity PlayMode tests when changing runtime behavior that depends on scenes or frame updates.
  - A project compile check when tests are not available.
- If Unity cannot be run from the current environment, state that clearly and explain what was checked instead.
- For visual or gameplay changes, describe the manual verification path in Unity.

## Git And Workspace Hygiene

- Do not revert user changes unless explicitly asked.
- Check the working tree before broad edits when possible.
- Keep changes scoped to the requested task.
- Avoid formatting entire files or scenes unless formatting is the task.
- Do not commit changes unless explicitly requested.

## Agent Behavior

- Read relevant files before editing.
- Prefer `rg` for searching.
- Use targeted patches for manual edits.
- Explain risky Unity-specific changes before making them, especially scene, prefab, package, or project setting edits.
- If the task requires opening Unity, installing packages, or accessing the network, ask for approval when the environment requires it.

