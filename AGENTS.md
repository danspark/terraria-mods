# Repository instructions

## tModLoader release artwork

- Before reporting a tModLoader mod as ready for release, make sure its root contains an original, reviewed `icon.png` in 80×80 RGBA format. Create or update the icon when it is missing or no longer represents the mod.
- Inspect the icon at its native 80×80 size and confirm the canonical build packages it. If the current tModLoader toolchain accepts a custom `icon_small.png`, derive it from the same artwork at 30×30 and inspect that file at native size too.

## Headless tModLoader runs

- Never launch `dotnet tModLoader.dll -server` with the tModLoader process reading stdin directly from a pipe, FIFO, `/dev/null`, or another redirected stream. In tModLoader 2026.06.3.6, a startup or world-generation failure enters an error path that calls `Console.ReadKey`. Redirected input makes that error handler throw `InvalidOperationException: Cannot read keys when either application does not have a console or when console input has been redirected`, which hides the first exception and can open a fatal-error dialog.
- Give the dedicated server a pseudo-terminal. In shell automation, run it through `script -qefc '<command>' /dev/null`. In an interactive Codex command, allocate a TTY. Feeding a FIFO into `script` is acceptable because the tModLoader child still reads from the pseudo-terminal.
- Monitor the captured output while generating a world. On the first world-generation or load failure, preserve `tModLoader-Logs/server.log`, send a newline through the pseudo-terminal so `Console.ReadKey` can return, and let a bounded timeout stop a stuck process.
- Diagnose the earliest exception in `server.log`. Treat the `Cannot read keys` exception as a secondary harness failure, never as the world-generation root cause.

## Vanilla Worlds Overhauled installed package

- After every successful Vanilla Worlds Overhauled build, synchronize `.playtest/build-save/Mods/VanillaWorldsOverhauled.tmod` to `$TML_SAVE_DIRECTORY/Mods/VanillaWorldsOverhauled.tmod`. When `TML_SAVE_DIRECTORY` is unset, use `$HOME/.local/share/Terraria/tModLoader`.
- Use `vanilla-worlds-overhauled/scripts/build-mod.sh` as the canonical build command. The script installs the completed package and fails if the installed bytes do not match the build artifact.
- Before reporting world-generation work as complete, compare the build artifact and the installed package with `cmp` or `sha256sum`. Do not leave a previously loaded or intermediate package in the local `Mods` directory.
- Updating the file does not reload a running tModLoader client. State that a mod reload or game restart is required, and do not claim that the running client loaded the new version until `tModLoader-Logs/client.log` records the expected Vanilla Worlds Overhauled version.

## Vanilla Worlds Overhauled source installation

- Keep the development source at `${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}/ModSources/VanillaWorldsOverhauled` as a symbolic link to this repository's `vanilla-worlds-overhauled` directory. This places Vanilla Worlds Overhauled beside locally created mods such as `testmod` without maintaining a second, stale source copy.
- Use `vanilla-worlds-overhauled/scripts/install-mod-source.sh` to create or verify the link. The canonical build script runs it automatically.
- Never replace an unrelated file, directory, or link already named `VanillaWorldsOverhauled` in `ModSources`. Stop and report the conflict instead.

## Vanilla Worlds Overhauled organic boundaries

- Do not generate a custom biome, mountain, natural wall field, terrain layer, structure-to-ground join, or material transition from a constant row, constant column, rectangular fill edge, or independent per-tile randomness. Use deterministic correlated variation at more than one scale, with enough amplitude to remain visible on the map.
- Before modeling a terrain-integrated structure, sample the finished terrain, walls, liquids, and biome materials across its complete padded footprint. Derive the foundation depth, approaches, supports, shoreline, basin walls, and exterior material blend from those samples. Do not clear a rectangle and disguise its edge afterward.
- Keep functional construction geometry readable. Rails, room floors, doors, bridge decks, and reserved traversal lanes may be straight when their role requires it, but their decorative material fields and joins into natural terrain must use the organic boundary field.
- A settled liquid surface may be level, but its shore, bed, spill lip, retaining material, and structure approaches must follow correlated contours and blend into the host biome. The same rule applies to a straight bridge deck: its abutments, supports, banks, and background field must not expose the generator footprint.
- A late repair pass must reapply the same seeded boundary function as the original owner. It must not restore a feature with a simpler axis-aligned approximation.
- Validate the finished tile and wall grid for long exact-axis material seams, rectangular clearance scars, abrupt host-material changes, and unsupported structure edges. Planned jitter or a noisy placement mask does not count if later passes leave a straight boundary in the saved world.

## Vanilla Worlds Overhauled custom construction

- Build custom structure shells, roofs, floors, towers, abutments, and load-bearing supports from visually substantial multi-tile masses. Never model a custom building as a one-tile-wide outline; reserve one-tile elements for deliberately thin details such as platforms, rails, rope, trim, and wiring.
