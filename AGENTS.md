# Repository instructions

## Headless tModLoader runs

- Never launch `dotnet tModLoader.dll -server` with the tModLoader process reading stdin directly from a pipe, FIFO, `/dev/null`, or another redirected stream. In tModLoader 2026.06.3.6, a startup or world-generation failure enters an error path that calls `Console.ReadKey`. Redirected input makes that error handler throw `InvalidOperationException: Cannot read keys when either application does not have a console or when console input has been redirected`, which hides the first exception and can open a fatal-error dialog.
- Give the dedicated server a pseudo-terminal. In shell automation, run it through `script -qefc '<command>' /dev/null`. In an interactive Codex command, allocate a TTY. Feeding a FIFO into `script` is acceptable because the tModLoader child still reads from the pseudo-terminal.
- Monitor the captured output while generating a world. On the first world-generation or load failure, preserve `tModLoader-Logs/server.log`, send a newline through the pseudo-terminal so `Console.ReadKey` can return, and let a bounded timeout stop a stuck process.
- Diagnose the earliest exception in `server.log`. Treat the `Cannot read keys` exception as a secondary harness failure, never as the world-generation root cause.

## Richer Biomes installed package

- After every successful Richer Biomes build, synchronize `.playtest/build-save/Mods/RicherBiomes.tmod` to `$TML_SAVE_DIRECTORY/Mods/RicherBiomes.tmod`. When `TML_SAVE_DIRECTORY` is unset, use `$HOME/.local/share/Terraria/tModLoader`.
- Use `richer-biomes/scripts/build-mod.sh` as the canonical build command. The script installs the completed package and fails if the installed bytes do not match the build artifact.
- Before reporting world-generation work as complete, compare the build artifact and the installed package with `cmp` or `sha256sum`. Do not leave a previously loaded or intermediate package in the local `Mods` directory.
- Updating the file does not reload a running tModLoader client. State that a mod reload or game restart is required, and do not claim that the running client loaded the new version until `tModLoader-Logs/client.log` records the expected Richer Biomes version.

## Richer Biomes organic boundaries

- Do not generate a custom biome, mountain, natural wall field, terrain layer, structure-to-ground join, or material transition from a constant row, constant column, rectangular fill edge, or independent per-tile randomness. Use deterministic correlated variation at more than one scale, with enough amplitude to remain visible on the map.
- Keep functional construction geometry readable. Rails, room floors, doors, bridge decks, and reserved traversal lanes may be straight when their role requires it, but their decorative material fields and joins into natural terrain must use the organic boundary field.
- A late repair pass must reapply the same seeded boundary function as the original owner. It must not restore a feature with a simpler axis-aligned approximation.
- Validate the finished tile and wall grid for long exact-axis material seams. Planned jitter or a noisy placement mask does not count if later passes leave a straight boundary in the saved world.
