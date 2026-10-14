# PracticingPrayer — guidance for AI coding agents

This file is committed and public. Machine-local, personal instructions (cross-machine handoffs, vault routing) live in `CLAUDE.local.md`, which is gitignored and loaded automatically alongside this file.

## Branching & merge discipline

The full branch model and contribution process are in [CONTRIBUTING.md](CONTRIBUTING.md) — that is the source of truth. Operational essentials for agents:

- **`master` and `dev` are branch-protected.** You cannot push to them directly. Every change reaches them through a pull request that passes the required `Unit Tests` CI check and is **squash-merged** (one commit per PR).
- **Never edit `dev` or `master` directly.** Before any edit, confirm `git branch --show-current` is not `dev`/`master`; if it is, create a branch first.
- **Branch from the right base:**
  - `feature/*`, `fix/*`, `release/*` → branch from `dev`
  - `hotfix/*` → branch from `master`
- **One concern per branch.** If a new concern surfaces mid-branch, branch separately off the appropriate base.
- **PR flow:** push the branch → `gh pr create --base <dev|master> --fill` → wait for CI green → `gh pr merge --squash --delete-branch`.
- **Closing issues:** `Closes #N` only auto-closes when a PR merges into the **default** branch (`master`). Work merges into `dev`, so the keyword never fires — close the issue manually after merge with `gh issue close <n>`.
- **`release/*` and `hotfix/*` merge twice:** into `master` (then tag the release) **and** back into `dev`, so the fix isn't lost in the next release.
- **`release/*` is optional ceremony:** cut one only when `dev` must keep moving while a build is frozen for stabilization / App Store review; otherwise `dev → master` directly is equivalent.
- **Commits are squashed**, so the PR title/description become the landed commit message — write them release-quality.

## Working in this codebase

- Read the file you're changing plus its direct callers/callees before editing.
- New behavior lands with tests; pure refactors are fine under existing green coverage.
- Run `dotnet test PrayerApp.Tests/PrayerApp.Tests.csproj` before opening a PR.
- Architecture and naming conventions: [ARCHITECTURE.md](ARCHITECTURE.md).
- Domain knowledge lives in **project skills** under [`.claude/skills/`](.claude/skills/) (`prayer-app-*`: database, viewmodels, views, services, navigation, theming, accessibility, ui-testing, unit-testing, platform, models). They auto-discover in Claude Code — consult the relevant one before working in that area.
- Source files are UTF-8 **without a BOM**. Some agent/editor file-write tooling injects a UTF-8 BOM into `.cs` files — byte-check changed files and strip any leading BOM before committing (the build and tests pass with a stray BOM, so nothing else catches it).

## E2E test isolation (non-negotiable)

The UITest suite shares one Appium session and one seeded SQLite database (seeded **once** in `TestDataSeed.SeedAsync`, `noReset=true`, never re-seeded — see the `AppiumSetup` #164 block). A test that passes alone but fails inside the full suite ("in-suite drift") is almost always one of these two rules being broken — and the fix is the rule, **not** a longer timeout:

1. **Namespaced, self-owned test data.** A test that needs a pre-existing card, request, or collection must seed its **own uniquely-named** fixture (suffix it with the test's name) in `TestDataSeed`, and touch only that data. Never read data another test can mutate, and never let a destructive test (delete / move) operate on a shared fixture — a deleted or moved shared card silently breaks every test that runs after it. Keep the one-time seed; do **not** re-seed per test, which would trade away the suite's speed.

2. **Every test starts on Home.** Begin each test from a known nav root (the Home tab) so a prior test's deep navigation is never a precondition for the next. `ResetAppUIState` + `EnsureOnTab` must reliably land on Home first; nav-state must never leak between tests.

## Simulators (shared with ios-budget-app)

This Mac keeps exactly four iOS simulators, shared with the ios-budget-app repo. Never create, boot, or address any other simulator, and never delete or rename these four. Address them by name, never by a default device name.

| Name | Device | Runtime | Use |
|---|---|---|---|
| `Dev iPhone` | iPhone 17 Pro Max | iOS 27.0 | Hand checks and App Store iPhone screenshots, both repos |
| `Dev iPad` | iPad Pro 13-inch (M5) | iOS 27.0 | Hand checks and App Store iPad screenshots, both repos |
| `Test iPhone` | iPhone 17 Pro | iOS 27.0 | ios-budget-app automated tests only |
| `Test iPad` | iPad (A16) | iOS 27.0 | PracticingPrayer E2E only (`IOS_SIMULATOR` default) |

- E2E needs an iPad: the iPhone keyboard has no dismiss button, so `DismissKeyboardIfPresent` fails.
- Shut down any simulator you booted for hand checks when you finish.
- The Android emulator `pp_api36` is outside this set.

## Solving a GitHub issue (milestone-driver)

This repo is a [milestone-driver](https://github.com/kenmulford/milestone-driver) consumer; its profile is `.milestone-config/driver.json` (with `.milestone-config/feeder.json` and the standing project docs under `.project/`, provisioned by milestone-bootstrapper). Drive one issue with `/solve-issue <n>`, or a whole milestone in dependency order with `/solve-milestone <name>` (order comes from the milestone description's Wave list).

Per issue the orchestrator — never authoring code itself — reads the issue → finds the root cause or **STOPs and comments** → dispatches the `implementer` subagent (TDD red→green, least-code, citations posted on the issue) → runs `dotnet test PrayerApp.Tests/…` → runs the E2E gate (`run-uitests.ps1`) for UI-touching changes → `/code-review` → opens a PR `--base dev` → auto-merges on the `Unit Tests` check green → closes the issue. Architecture is locked at plan-approval time; one-way-door decisions STOP and ask rather than drift.

**Mechanical gates** (active once the plugin is installed and Claude Code is restarted):
- `force-subagent` — main-thread edits to `PrayerApp/**`, `PrayerApp.Tests/**`, `PrayerApp.UITests/**` are blocked; dispatch the implementer instead (escape: `CLAUDE_HOOK_DISABLE_FORCE_SUBAGENT=1`).
- `tests-green` — source commits run the unit suite; red blocks the commit.
- `no-push` / `no-pr-to-protected` — pushing or opening a PR to `master` is blocked locally; GitHub branch protection is the server-side backstop.

Non-negotiables: MAUI .NET 10 + Community Toolkit; built with Xcode 27 / iOS 27 SDK (minimum iOS 16.0); Android API 36.
