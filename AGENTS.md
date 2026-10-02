# Repository Guidelines

## Project Structure & Module Organization

This is a Unity 6 project using Editor **6000.6.3f1**, URP, and the Input System.

- `Assets/Scripts/Entity/`: runtime assembly `Entity.Runtime`; organized into Input, Data, Logic, Physics, and Presentation, with `Entity` and `EntityBrain` coordinating components.
- `Assets/Editor/`: demo asset generation, scene assembly, and validation tools.
- `Assets/Scenes/SampleScene.unity`: current demo and enabled build scene.
- `Assets/TestAssemblies/`: EditMode and PlayMode tests.
- `Assets/Input/`, `Assets/Settings/`: input actions and rendering configuration.
- `Docs/`: game design, implementation records, and shared planning/progress files.
- `Packages/` and `ProjectSettings/`: dependency and editor configuration.

## Build, Test, and Development Commands

Open the repository in the pinned Unity version. Run `SampleScene` with Play; create player builds through **File → Build Profiles**. No scripted player-build entry point is currently provided.

PowerShell examples, with `$unityExe` set to the installed Unity executable:

```powershell
& $unityExe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults "$env:TEMP/crown-editmode.xml" -logFile "$env:TEMP/crown-editmode.log"
& $unityExe -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults "$env:TEMP/crown-playmode.xml" -logFile "$env:TEMP/crown-playmode.log"
& $unityExe -batchmode -nographics -projectPath . -executeMethod CreateSceneDemo.Verify -quit
```

Tests exit automatically; do **not** add `-quit` to `-runTests`. Use the **Crown Tide** editor menu to generate demo assets, assemble the scene, or validate it.

## Coding Style & Architecture

Use four-space indentation and braces on separate lines. Use PascalCase for types, methods, properties, and fields; camelCase for parameters and locals. Match neighboring code where existing serialization names differ.

Keep nullable analysis enabled (`Assets/csc.rsp`, `Directory.Build.props`); use `?` for legitimate empty values. No repository formatter or linter is configured.

Keep Data focused on configuration and runtime values, Logic on gameplay decisions, and Presentation on display. Route damage through `EntityBrain.TakeDamage`. Keep shared ScriptableObject templates separate from per-entity runtime state.

## Testing Guidelines

Use Unity Test Framework/NUnit: EditMode for data rules; PlayMode for component interactions. Name tests descriptively, following existing Chinese behavior-based names. Add relevant regression coverage for gameplay changes; no numeric coverage threshold is configured. Report tests actually run and failures separately from visual verification.

## Commit & Pull Request Guidelines

Recent commit subjects are `123`, so no meaningful message convention is established. Write descriptive, scoped subjects, such as `Fix possession effect expiration`. PRs should explain behavior changes, validation, related issues when applicable, and include screenshots for visual changes.

Preserve asset `.meta` files. Do not commit generated caches or builds, or hand-edit demo scene YAML; use Unity/editor tools. Update affected documentation when behavior changes.

## Agent 日志与开发者身份

- 每次开始工作、写日志前，在当前仓库读取 `git config user.name`，用实际返回值作为本次“开发者”；不要沿用其他电脑或历史日志中的身份。
- 每条新增日志注明日期、任务、`开发者：<Git 用户名>`、`Agent：<实际执行的 Agent 名称>`，记录改动、验证结果和未完成事项。
- Git 用户名为空时询问用户署名；无法确定 Agent 名称时如实注明未知，不猜测，不自动修改 Git 身份配置。
- 共享日志统一追加到 `Docs/progress.md`。保留历史作者和记录，不因拉取、接手或续写而改署名。
- planning-with-files 的记录统一放在 `Docs/task_plan.md`、`Docs/findings.md`、`Docs/progress.md`，不放项目根目录，也不按开发者再拆目录。
