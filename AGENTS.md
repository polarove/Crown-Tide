# Repository Guidelines

## 架构职责登记：每次工作必须先读

- 每次进入本仓库开始任务、收到新的开发指令／提示词、接手任务或上下文压缩恢复后，必须先完整读取 [Docs/架构职责登记.md](Docs/架构职责登记.md)，理解本次工作涉及的职责、数据流向、读写权限和未定边界，再制定方案或修改文件。不能只依赖历史聊天、摘要或架构图。
- 这是本项目的重要架构依据。登记中的“最新会议目标约定”指导后续新增与重构；旧类职责表记录现状，不代表旧实现已符合最新约定。用户后续明确决定优先，先同步登记再实施；未确认事项不得自行变成强制规则。
- 动手前明确本功能属于哪个模块、谁仲裁、谁管理状态、谁实施效果、谁经数据接口赋值。状态机只管流转，逻辑层负责效果与赋值；下层禁止反向修改上层。具体规则以登记为准。
- 功能开发遵循“方案文档 → 实现 → 架构审查 → 修正 → 复审通过”。每次大量变更必须完成架构审查，通过后才进入下一功能；验证结果与未完成事项写入 `Docs/progress.md`。
- 若登记无法读取，或本任务依赖的关键边界仍冲突／未定，先解决该缺口，不得凭猜测修改相关代码；可继续不依赖该缺口的工作。架构职责变化必须更新登记，并保持图稿一致。
- 架构与开发文档必须随实现持续维护，包括实际类职责、读写权限和验证边界；会议新增明确决定需先同步登记。旧实现快照中的冲突规则不得继续使用，也不能把网络规划、附身防误触备选或接口示意当成已批准实现。

## Project Structure & Module Organization

This is a Unity 6 project using Editor **6000.6.3f1**, URP, and the Input System.

- `Assets/Scripts/Entity/`: runtime assembly `Entity.Runtime`; organized into Input, Data, Logic, Physics, and Presentation, with `Entity` and `EntityBrain` coordinating components.
- `Assets/Scripts/Entity/Debug/`: isolated debug session, command execution, panel, and demo helpers; debug commands attach through the optional command-module interface only while Debug is enabled.
- `Assets/Scripts/Entity/Settings/`: single-player settings session; Data stores binding overrides, Logic validates/synchronizes runtime input copies and handles pause/exit, Presentation submits UI requests. Esc/Gamepad Menu opens settings; Backspace restarts the demo.
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
