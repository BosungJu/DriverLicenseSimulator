# AGENTS.md

Guidance for AI coding agents (Codex, Claude Code) working in the DriverLicenseSimulator Unity project.
Codex reads this file directly; Claude Code imports it via `CLAUDE.md`. Edit this file, not `CLAUDE.md`, when changing shared rules.

## Scope and Priorities

- Precedence: explicit user instructions > this project file (and any deeper directory `AGENTS.md`) > personal global defaults (`~/.codex/AGENTS.md`).
- Within this project, match the file being edited first, then nearby subsystem conventions, then the rules below. Apply C# and Unity rules only to their respective technologies.
- Prioritize correctness, stability, existing architecture, readability, maintainability, then performance optimization. Respect established performance requirements (this is a real-time driving simulator connected to external hardware).
- Communicate with the user in Korean. Code, identifiers, and commit messages follow the rules below.

## Project Rules

These come from the project `README.md` and are agreed upon by the team. They override the generic fallback style in this file.

### Naming

- Folders: nouns.
- Asset files: `FileType_ResourceType_Detail` (e.g. `Animation_Archor_Shot`).
- Classes, methods, properties: PascalCase.
- Variables and fields (including serialized private fields): camelCase.
- Indentation: **tabs** (not spaces).

### Branching (GitHub-flow)

- Never commit directly to `master`. `master` is only for release-quality versions (final or Release Candidate).
- Work on `feature/<short-name>` branches (e.g. `feature/add-subtitle`).
- Open PRs against `develop`.
- Always tell the user before creating a PR, and do not create one unless asked.

### Commit Messages

- Bug fixes: `fix: <summary>` (e.g. `fix: click evt null ref except.`).
- Feature work: `feat: <summary>` (e.g. `feat: user move`).

### Ownership

- Map building: 주보성. Vehicle controls: 김태원. UI and sound: shared.
- Avoid changing another owner's area as a side effect; if a change there is necessary, say so explicitly.
- This repository contains the Unity side only. Hardware integration lives in a separate repository; do not assume hardware code or protocols exist here.

## Project Assumptions

- This is a Unity project. Prefer Unity-native workflows and existing project conventions over introducing external tooling.
- Treat `Assets/`, `Packages/`, and `ProjectSettings/` as the primary project surface.
- Do not modify generated or cache folders such as `Library/`, `Temp/`, `Obj/`, `Logs/`, `Build/`, `Builds/`, or `UserSettings/` unless explicitly requested.
- Do not edit or regenerate the root `*.csproj` / `*.slnx` files; Unity regenerates them.
- Keep `.meta` files paired with their assets. When adding, moving, or deleting Unity assets, handle the corresponding `.meta` files consistently.
- Odin Inspector (Sirenix) is present. Reuse its attributes where the surrounding code already does; do not introduce it into code that does not use it without a reason.

## Working Approach

- Read relevant instructions, files, callers, and available build/test commands before editing. Understand the existing behavior and intent.
- Check the working tree before broad edits when possible. Prefer `rg` for searching and targeted patches for edits.
- Implement the smallest complete change. Extend existing types and execution paths before introducing abstractions; preserve established class hierarchies and subsystem patterns.
- For bugs, identify the root cause. Trace call flow, state, and execution order when investigating null references, indexing, races, events, or asynchronous behavior.
- State material assumptions. Ask when missing information would materially change correctness or scope; otherwise proceed with a reasonable assumption.
- Preserve unrelated user changes and never revert them unless asked. Limit cleanup to obsolete state, imports, comments, or branches made unnecessary by the affected flow.
- Do not remove existing functionality without an explicit request, reformat files or scenes without a request, or add frameworks or upgrade dependencies without a concrete reason.
- Explain risky Unity-specific changes (scene, prefab, package, or project setting edits) before making them.
- If the task requires opening Unity, installing packages, or accessing the network, ask for approval when the environment requires it.
- Do not commit unless explicitly requested.

## Implementation

- Use descriptive names, focused functions/classes, and explicit readable conditions. Reuse existing helpers for meaningful duplication without speculative abstractions.
- Prefer early exits and staged fallback selection: try the preferred candidate, then the general candidate if needed.
- Initialize transient state when creating, assigning, or reusing an object. Expose inspection-only state through read-only properties rather than public fields.
- Handle established failure cases without silently swallowing exceptions or changing normal behavior through speculative defensive logic.
- Replace meaningful magic numbers with named constants (e.g. speed limits, distances, timing thresholds for test scoring). Comment non-obvious intent or constraints rather than restating code.
- Avoid introducing global state unless the project already has a clear service, manager, or dependency pattern.
- Never hardcode or expose secrets, credentials, or personal information.

## C# Style

Project rules above take precedence (notably **tab indentation** and camelCase fields). Otherwise:

- Use Allman braces and one blank line between logical blocks.
- Use PascalCase for types, methods, and properties; descriptive camelCase for locals and fields. Keep public and serialized names stable unless the task requires a change.
- Use explicit types for important domain objects and state; use `var` when the right-hand side makes the type obvious.
- Prefer expression-bodied properties for simple read-only accessors, and spaces inside collection initializer entries such as `{ key, value }`.
- Use explicit `if` blocks for multi-step conditions and fallbacks.
- Prefer small, focused MonoBehaviours and ScriptableObjects over large multipurpose classes. Keep gameplay, editor tooling, UI, and data code separated according to the existing folder structure.
- Use Unity serialization intentionally:
  - Prefer `[SerializeField] private` fields for Inspector-configured dependencies.
  - Avoid public fields unless they are part of an intentional API.
  - When renaming a serialized field, use `[FormerlySerializedAs]` if scene or prefab data must be preserved.

## C# Readability

Apply these rules to new or touched code, respecting local conventions and preserving behavior. Do not treat every existing pattern as a preference.

- Make entry methods readable as a sequence of meaningful steps: validate prerequisites, select or calculate, update state, then notify when that matches the required behavior. Preserve existing side-effect and event order; never reorder operations merely to fit this outline.
- Extract a helper when it gives a coherent operation a useful name, separates abstraction levels, or removes meaningful duplication. Keep closely related statements together; do not split methods just to meet a line limit or create chains of trivial forwarding helpers.
- Names must reveal the actual role and effect. Use `Is` / `Has` / `Can` for boolean queries and verbs such as `Assign`, `Apply`, `Reset`, or `Complete` for state changes. Do not hide assignments or notifications in query-like methods. Preserve existing public/serialized names unless a rename is explicitly in scope.
- Name locals and parameters after domain roles (e.g. `targetLane`, `currentCheckpoint`, `steeringInput`, `elapsedTestTime`) rather than ambiguous `data`, `temp`, `obj`, or `time`. Short loop indices remain appropriate. Include units or coordinate space when ambiguity matters (e.g. `speedKmh`, `localOffset`).
- Put prerequisite and no-work exits near the start when semantics allow, then keep the main path at a shallow indentation level. Retain `else` when it makes two meaningful alternatives easier to compare; avoid nested ternaries and compressed multi-action statements.
- Give complicated domain decisions a meaningful boolean or predicate name. Keep simple comparisons inline, preserve short-circuit behavior, and avoid storing a condition that can become stale after state changes or coroutine yields.
- Use named intermediate values for repeated casts, deep member chains, or multi-stage calculations when they clarify meaning. Cache only for a lifetime in which the value is valid; re-evaluate mutable assignments across yields when required.
- Keep selection/query logic visibly separate from state mutation and notification. Show related state updates together so callers can see when an operation is complete and what observers will receive.
- For new classes without a local ordering convention, group constants, serialized configuration, runtime fields, properties/events, lifecycle methods, public operations, and private helpers. Keep related helpers near their callers and paired lifecycle operations easy to find. Do not reorder an existing class or serialized fields just for appearance.
- Use one blank line between logical stages. Use existing Inspector headers for coherent configuration groups; add comments for timing constraints, invariants, or non-obvious reasons rather than narrating each statement. Do not add regions merely to hide oversized methods.
- Before finishing, read the changed path from its entry point: can the reader identify prerequisites, the main operation, state changes, completion, and cancellation without jumping through unrelated helpers? Resolve ambiguities locally and verify that readability edits preserve behavior.

## Unity

### Runtime Code

- Preserve serialized layouts, inspector references, command lifecycles, and existing manager patterns. Avoid breaking scenes or prefabs through member renames.
- Respect `Awake`, `OnEnable`, `Start`, `Update`, `FixedUpdate`, and `OnDisable` ordering. Pair event subscription and unsubscription in `OnEnable` / `OnDisable` when appropriate to the intended lifetime.
- Put physics-driven vehicle logic (forces, `Rigidbody` velocity) in `FixedUpdate`; read input and update visuals in `Update` unless the existing code establishes otherwise.
- Guard Unity references that can be missing, including singleton managers and assigned objects. Prefer serialized references, dependency injection, and already assigned objects over scene-wide searches.
- Avoid repeated `GetComponent`, `FindObjectOfType`, or `FindAnyObjectByType` calls in frame loops and unnecessary allocations in hot paths. A one-time lookup is acceptable when consistent with the subsystem.
- Distinguish world, screen, canvas, and local UI coordinates explicitly.
- Give coroutines clear start and termination conditions. Polling must have an explicit exit or bounded retry; choose frame/time waits deliberately.
- When completion order matters, yield nested coroutines and explicitly wait for callback completion instead of starting dependent work fire-and-forget.
- On command cancellation, stop tracked child coroutines, clear their collection, reset running flags, and clear the main coroutine reference as required by the existing lifecycle. Keep completion callback invocation centralized.
- Use concise debugging logs identifying the subsystem, action, and relevant state.

### Asset Safety

- Be careful with scenes, prefabs, materials, animation controllers, terrain, and ScriptableObjects; small text changes can alter serialized references.
- Before editing serialized YAML files directly, inspect the surrounding structure and preserve IDs, GUIDs, and file references.
- Do not regenerate project files, package locks, or large serialized assets unless the task requires it.
- Do not remove unused-looking assets without checking references.
- Large scene/map edits should be done in the Unity Editor by the user; describe the steps instead of hand-editing large scene YAML.

### Packages and Dependencies

- Prefer Unity Package Manager packages already present in `Packages/manifest.json`.
- Do not add new packages or external dependencies unless they are necessary for the task.
- If a package change is needed, update both `Packages/manifest.json` and `Packages/packages-lock.json` when applicable.
- Do not change the Unity version or project settings unless explicitly requested.

## Testing and Validation

- For code changes, validate with the narrowest reliable check available:
  - Unity EditMode tests for editor, utility, or pure C# logic.
  - Unity PlayMode tests for runtime behavior that depends on scenes, physics, or frame updates.
  - A project compile check when tests are not available.
- If Unity cannot be run from the current environment, state that clearly and explain what was checked instead.
- For visual or gameplay changes (map, driving feel, UI), describe the manual verification path in Unity (scene to open, actions to perform, expected result).
- Never imply unrun tests passed.

### Unit Testing Workflow

- Treat requests such as "unit test this function" or "이 기능 유닛테스트하고 싶어" as instructions to implement runnable tests, not just describe test cases. Identify the target from the conversation and current changes; ask only if the target or expected behavior remains materially ambiguous.
- Inspect the function, callers, intended contract, existing tests, framework versions, test discovery, and assembly/project references. Reuse the project's test framework and conventions. If none exist, configure the smallest compatible test setup needed and explain added dependencies; avoid unrelated upgrades.
- Briefly identify relevant scenarios, then implement them: normal behavior, boundary values, empty/null or invalid input where the contract defines it, and meaningful failure paths. Cover state transitions, side effects, callbacks, repeated calls, or cancellation when applicable. Do not invent error handling or expected results merely to make tests pass.
- Use readable Arrange-Act-Assert structure and behavior-focused names such as `Method_Scenario_ExpectedResult`, unless local conventions differ. Use parameterized cases for the same behavior with different inputs; keep distinct behaviors separate.
- Test observable behavior through the real production code. Derive expected results independently from the contract or concrete examples, not by duplicating the implementation. Prefer public entry points over reflection or exposing private members solely for tests.
- Keep tests deterministic and independent: use small explicit fixtures and lightweight fakes for external dependencies (including hardware input); control time/randomness when needed, avoid real network access and arbitrary sleeps, and clean up objects, subscriptions, files, and shared state after each test.
- If testability requires a change, make the smallest behavior-preserving seam consistent with the existing architecture. Do not introduce a mocking framework or restructure managers/singletons solely for convenience. Clearly distinguish production changes from test setup.
- Inspect the installed Unity Test Framework and existing test assemblies. Prefer EditMode tests for isolated logic; use PlayMode tests when correctness depends on runtime lifecycle, physics, frames, or coroutine execution. Label runtime integration tests accurately rather than presenting them as isolated unit tests.
- Place tests in existing test folders, or focused EditMode/PlayMode folders when absent. Configure test assembly references/discovery for the project's actual assembly layout and exclude test-only dependencies from player builds. Do not blindly add runtime `.asmdef` files or move scripts, since that changes compilation boundaries. Use `[Test]`, `[TestCase]`, or `[UnityTest]` according to the required execution model.
- Run the targeted tests with the project's supported runner, then relevant regression checks when warranted. For bug regressions, demonstrate failure before the fix and success after it when feasible. Diagnose failures rather than weakening assertions or skipping tests.
- Report covered scenarios, changed test/setup files, the exact command or Unity Test Runner steps, and actual results. If execution is unavailable, still prepare the runnable setup as far as possible, explicitly state that tests were not run, and identify the missing prerequisite. A compile check alone is not a passing test run.

### Test Code Organization

- Make test code easy to scan as well as execute. Use `#region` / `#endregion` when a fixture contains several meaningful groups, such as Setup and Teardown, Normal Cases, Boundary Cases, Failure Cases, or Test Helpers. Avoid nested regions, a region for every test, or regions in small fixtures that are already clear.
- Keep each fixture focused on one production type or cohesive behavior. Split unrelated responsibilities into separate test classes/files. Keep a behavior's normal, boundary, and failure cases easy to find together.
- Extract repeated, substantial arrangement into clearly named fixture builders, factories, or setup helpers. Keep the inputs and expected results important to each test visible in that test.
- Put lightweight fakes, stubs, and spies in focused test-only classes. A small helper used by one fixture may be nested; move sizeable or shared helpers into separate files in the test area.
- For Unity tests that need callbacks, lifecycle observation, or coroutine hosting, create a focused test-only component when useful. Create and destroy its GameObject in the fixture lifecycle, and keep it out of production scenes, prefabs, and player builds. Plain test data and logic helpers should remain ordinary C# classes.
- Prefer a simple test layout with fixtures and, only when needed, Helpers/Fakes/Components folders under the relevant test assembly.

## Reviews and Documentation

- Review correctness, regressions, maintainability, and concrete risks. Explain actionable findings with evidence; avoid subjective style-only feedback.
- Also check project rules: naming, tab indentation, `.meta` pairing, serialized-field renames, and ownership boundaries.
- Keep documentation concise and concrete. Explain inputs, processing, and outputs where useful, and document significant decisions and assumptions.

## Completion Report

After implementation, report in Korean using these sections with concise bullets:

- **요약 (Summary)**: behavior changed, root cause for bug fixes, and significant decisions or side effects.
- **수정 파일 (Modified Files)**: affected files and their purpose.
- **검증 (Verification)**: checks performed and results; remaining limitations or manual verification steps in Unity.
- **커밋 제안 (Suggested Commit)**: a `fix:` / `feat:` message and target `feature/*` branch, when relevant.

For questions or discussion without implementation, answer directly without forcing this report format.
