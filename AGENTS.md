# Agent guide — ZappAR Unity Showcase

A WebGL image-tracking AR showcase (Unity 6000.5.9f1, URP 17, Zappar Universal AR 3.2) hosted on ZapWorks,
plus the tooling around it: a Zappar editor plugin (`Assets/editor/ZapparCli`) exposed to the Unity CLI,
and batch scripts so every step can run without a person in the Editor.
Goal of the repo: an ever more automated "Zappar + Unity SDK extension". Prefer adding a command over
documenting a manual step.

Repo: `github.com/markobozx/my-zappar-unity-showcase` (PUBLIC), working branch `urp-migration`.
Paths contain spaces: always quote them.

## Hard rules

- **Never commit Asset Store packs** (Synty, KayKit, ithappy, Cosmic Retro, JMO/Cartoon FX, FlatKit, …). They are
  gitignored; keep it that way. Only `Assets/Scripts`, `Assets/editor`, `Assets/Showcase`, `Assets/Scenes/Showcase.unity`,
  `Assets/StreamingAssets/*.zpt`, settings and `tools/` are ours.
- **One MonoBehaviour per file, file name = class name** (WebGL players crash otherwise). Plain classes may share files.
- **Never open or kill the Unity Editor.** If a batch run prints `BLOCKED` (exit 3) the user has the Editor open: either
  use the Unity CLI path (`tools/zappar.sh` does this automatically) or ask the user to close it.
- Only one Unity process per project: `tools/unity-batch.sh` takes a lock and waits; don't bypass it.
- WebGL's built-in font lacks ✓ ✗ ▶ ✎ — use √ × ► › in any player-facing text.
- Release zip limits: ZapWorks accepts 25 MB; soft limit 20 MB (warning), hard limit 22 MB (build fails). Current ≈ 10 MB.
- Versions: `0.0.x` until the v0.1.0 milestone (10 polished experiences, polished editor plugin, automated workflows).
  Auto-bump stops at `0.0.99`; milestone versions are set by hand (`zappar_set_version`).
- Fictional brand names only in experiences.

## Commands

One entry point: `tools/zappar.sh <command> [-arg value …]`. With the Editor open it runs in it through the Unity CLI
(`unity command <command> --arg value`); with the Editor closed it runs headless through `tools/unity-batch.sh`.
Exit code 0 = success; output lines are tagged `[ZAPPAR]`.

| Command | What it does |
|---|---|
| `zappar_status` | product, next version, ZapWorks project id, every target (trained? in scene?), rebuild needed, last zip |
| `zappar_add_target -image <png/jpg> [-dpi N] [-rebuild false]` | copies the image to `Assets/Showcase/Targets/<id>.png`, trains `Assets/StreamingAssets/<id>.zpt` with `zapworks train`, rebuilds the scene |
| `zappar_remove_target -id <id> [-rebuild false]` | deletes the image + .zpt, rebuilds |
| `zappar_train_missing` | trains every target image without a .zpt |
| `zappar_rebuild_scene` | full scene build (every trained target gets a tracker and appears on the start screen) |
| `zappar_build_release [-version x.y.z]` | WebGL build → `Builds/ZappAR-Unity-WebGL-<ProductName>-v<version>.zip`, limit check, bumps next version |
| `zappar_set_version -version x.y.z` / `zappar_set_project -id <zapworks id>` | settings |
| `showcase_experiences` | experience ids (for mappings and `?exp=` links) |
| `showcase_set_target_experience -target <id or *> -experience <id or hub>` | edits `RemoteConfig/showcase-config.json` |

Unity CLI directly: `unity command zappar_status` (Editor open) or `unity run . --command zappar_status --format json`
(headless; arguments after `--`). `unity command` lists everything. The commands live in
`Assets/editor/ZapparCli/ZapparCliCommands.cs` and `Assets/editor/Showcase/ShowcaseCliCommands.cs` (`[CliCommand]`,
package `com.unity.pipeline`); `ZapparBatch.Run` makes every one of them usable in batch mode too, so a new
`[CliCommand]` is automatically available everywhere.

Other tools:

| Tool | Use |
|---|---|
| `tools/compile-check.sh` | Roslyn compile of our scripts in seconds, no Unity needed. Must print `OK: compiles`. |
| `tools/unity-batch.sh <Class.Method> <log-name> [--graphics] [--no-quit] [args]` | any batch method, with the lock; log in `Logs/batch-<log-name>.log` |
| `…ShowcaseSceneBuilder.BuildOne build-<id> -showcaseExperience <id>` | rebuild one experience |
| `…ShowcasePreview.Render preview-<id> --graphics -showcaseExperience <id>` | renders `Builds/Preview/<id>/{front,top,left,close}.png` |
| `…ShowcasePlayTest.Run playtest-<id> --graphics --no-quit -showcaseExperience <id\|all\|hub>` | play-mode test; must end `[SHOWCASE-PLAYTEST] PASS`; screenshots in `Builds/Preview/<id>/play-*.png` (hub: `play-scan.png` is the start screen) |

(`…` = `Showcase.EditorTools.`)

## Workflows

**Add an image target** — `tools/zappar.sh zappar_add_target -image <path>` and you're done (copy, train, rebuild).
In the Editor: drop the image into `Assets/Showcase/Targets/` (auto-trained) or use Zappar > Zappar CLI Tools > Image Targets,
then press the orange "Rebuild scene for targets". Several targets → the start screen becomes a slideshow.
Every tracker runs each frame; more than ~4 targets slows older phones (the build warns).

**Choose what a target opens** — `showcase_set_target_experience`, then commit + push `RemoteConfig/showcase-config.json`:
the live app reads it from GitHub at startup (cached ≤ 5 min), no new build needed. The Editor reads the local file.
Order of precedence: URL `?exp=<id>` (all targets) or `?<targetId>=<id>` > config file > values baked at the last scene build.

**Add or change an experience**
1. Runtime code: `Assets/Scripts/Showcase/Experiences/<NN_Name>/` (namespace `Showcase.Experiences.<Name>`), a
   `ShowcaseExperience` subclass: `OnEnter()` resets everything (entered many times), `OnExit()` destroys spawned objects,
   `SelfTest()` plays the real win path and logs `Debug.LogError("[SELFTEST] …")` on wrong state, `DebugComplete()` shows the end.
2. Builder: `Assets/editor/Showcase/Experiences/<NN_Name>Builder.cs` with
   `[ShowcaseExperienceBuilder(order, id, title, blurb, previewPrefab)]` and `public static void Build(ShowcaseBuildContext ctx, Transform root)`.
3. Loop until clean: compile-check → BuildOne → Preview (read the PNGs, critique layout) → PlayTest PASS (read the play PNGs).

**Release**
1. `tools/compile-check.sh`, `zappar_rebuild_scene`, `ShowcasePlayTest.Run … -showcaseExperience all` → PASS.
2. `tools/zappar.sh zappar_build_release` → zip path + size line. Over 22 MB → find big assets in the build log
   (`ShowcaseSizeOptimizer.BuildAndReport` lists them).
3. Commit + push. Upload: the ZapWorks CLI upload currently returns 404, so the user uploads the zip by hand on my.zap.works.

## How the app works (for orientation)

- One scene, `Assets/Scenes/Showcase.unity`, generated entirely by `ShowcaseSceneBuilder` — never hand-edit it; change the builder and rebuild.
- `ShowcaseRig` modes: **Scanning** (start screen `TargetScanScreen`: camera feed + target image/slideshow, HUD hidden) →
  **Tracking** (AR on the found target) → **Free** (orbit camera, only after the target is lost or via "Explore without AR").
- `ShowcaseTargets` routes trackers: the first seen target anchors `World` (and the Zappar camera origin) and opens its experience.
- `World` holds `Hub` (10 pedestals) and `Experiences/<NN Title>`; one is active at a time. Use `ShowcaseRig.ActiveCamera`, never `Camera.main`.
- UI: bottom `ShowcaseTray` (tiles with live 3D thumbnails from `ModelPreview`, preview, up to 3 actions) for choices;
  `WorldTag` / `WorldCard` in the scene for what things are; `ShowcaseHud` for title, score, timer, toast, result.
- Stage space per experience: floor y = 0, x ∈ [-1.5, 1.5], z ∈ [-1, 1], viewer at -Z looking +Z.

## Where state lives

| File | Holds |
|---|---|
| `ProjectSettings/ZapparCliSettings.json` | ZapWorks project id, next version, auto-bump, build/target folders, auto-train, targets the scene was built with |
| `ProjectSettings/ProjectSettings.asset` | product name (`Showcase`, used in the zip name), bundle version (set on every release build) |
| `RemoteConfig/showcase-config.json` | target → experience mapping (live via GitHub raw) |
| `Assets/Showcase/Targets/` + `Assets/StreamingAssets/` | target images + trained .zpt files |
| `Builds/` (gitignored) | WebGL build, release zips, preview/play-test screenshots |

## Reporting

Report results plainly with the evidence line (`OK: compiles`, `[SHOWCASE-PLAYTEST] PASS`, `[SHOWCASE-RELEASE] … MB`).
If something fails, show the output; never call unverified work done. Commit only when asked; end commit messages with the
attribution lines your harness specifies.
