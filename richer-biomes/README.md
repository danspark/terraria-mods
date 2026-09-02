# Richer Biomes

Richer Biomes 0.3.1 turns ordinary Terraria worlds into connected exploration regions while retaining vanilla materials, biomes, loot, and progression. It creates height-varied mountain ranges, floating highland biomes, furnished regional landmarks, protected building terraces, cave routes, and one guaranteed surface-to-Cavern mine district.

The mod targets Terraria 1.4.4.9 on tModLoader 2026.07.3.0. Secret seeds stop before terrain mutation because their layer and progression rules need separate plans.

## World contract

Every supported world receives the following features:

- Correlated full-world terrain with quiet lowlands, hills, plateaus, basins, valleys, and size-aware mountain ranges.
- Ground-connected mountain ranges selected from Highland, Alpine, and Sky-piercing altitude families. Only Sky-piercing ranges enter Space and grow cloud belts. Every range inherits the material family beneath each part of its body, including Snow, Jungle, Desert, Corruption, or Crimson; has two foothill entrances and one of four cave grammars; and contains wall-backed chambers, open-background pockets, suspended natural ledges, vine curtains, climbing aids, furnished vignettes, pots, rubble, a themed valley, and a bridge between its peaks.
- One floating highland on small worlds and up to two on medium and large worlds. A highland is approximately 280×90, 360×110, or 440×140 tiles before its satellites and uses a Terraced Meadow, Cloud Basin, or Broken Archipelago grammar. At most one highland may touch an Alpine or Sky-piercing mountain; attachment is attempted in one fifth of plans, so detached highlands are normal.
- A guaranteed visible surface mine with a headframe and eleven required rail edges from the Surface into the deep Cavern layer. The graph includes multiple junctions, cycles, horizontal work lines, correlated cavern-height changes above its rails, and additional branches to flooded, collapsed, and sealed world-evil sections. Work areas contain beams, mixed walls, lighting, work furniture, platform drop bays, and a mountain-rail district.
- A styled, connected exploration landmark for Forest, Snow, Desert, Jungle, world evil, Sky, Glowing Mushroom, Cavern, and Underworld, plus separate left and right Ocean landmarks. Ground bays, upper rooms, two-tile posts and ceilings, correctly oriented sloped gable roofs, diagonal platform stairs, strict interior wall fields, themed vanilla furniture, and rounded open arches make them readable places while deliberately failing NPC housing checks. Forest structures use ordinary Wood Blocks, Wood walls, and Wooden Beams instead of Living Wood.
- Wide forest, snow, desert, jungle, and world-evil transition bands whose stepped, wavy material boundaries vary with depth instead of following a vertical line. Only seams still visible after later feature ownership are retained in the final manifest.
- Protected flat ground at spawn and additional building terraces, connected regional cave routes, and contextual accents that leave quiet construction space.

No generated landmark or mine section contains special loot. Richer Biomes does not duplicate progression rewards or overwrite the Dungeon, Jungle Temple, Aether, chests, wiring, or other protected content.

## Validation guarantees

Generation fails instead of accepting a partial world when the finished tile grid violates the contract. The validator checks actual tiles after vanilla cleanup, including:

- every mountain surface matching its planned Highland, Alpine, or Sky-piercing altitude family, two visible entrances, and a cloud belt only where the plan is Sky-piercing;
- substantial wall-backed mountain cave area, organically bounded natural-wall fields, open-background cave pockets, suspended natural ledges, distinct vine curtains, wide chambers, pots, and rope or platform climbing aids in every range, plus different altitude and interior families when a world has multiple ranges;
- a three-tile structural bridge with background panels, platform drop bays, clear headroom, actuated mountain portals, and no solid endpoint-corridor blockers, plus a valley payload for every mountain range;
- a connected floating-highland component spanning at least three quarters of its target width and two thirds of its target depth, with varied styles and no more than one mountain attachment;
- all eleven landmarks with connected room plans, mixed interior walls, thick foundations, sloped solid roofs, diagonal sloped-platform stairs, retained multitile furniture, open approaches, no exterior wall leakage, no doors, and failed real NPC-housing queries;
- at least two or three visible organic biome seams by world size, each measured across many depth samples with at least eight tiles of lateral boundary movement;
- a minecart-track component reachable from the surface entrance with at least 300, 500, or 700 tiles on small, medium, or large worlds;
- every authored mine route tile and all eleven required mine edges in that entrance component, with at least six tiles of clear headroom, a measured mix of tighter and cavernous ceilings, the required junctions, cycles, and straight rail grades;
- preserved vanilla progression structures, world evil, and Shimmer.

The version 4 world manifest stores final landmark layouts, mountain interior and decoration measurements, valley, bridge, highland style and attachment state, surviving transition seams, mine sections, and validation records. The playtest harness verifies that this metadata survives generation, reload, save, and a second reload.

## Build

Run from `richer-biomes`:

```bash
./scripts/build-mod.sh
```

The canonical build writes `.playtest/build-save/Mods/RicherBiomes.tmod`, installs the same bytes into `${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}/Mods/RicherBiomes.tmod`, and fails if the two packages differ. A running client does not load changed package bytes automatically; reload mods or restart tModLoader.

## Generate a playtest world

Choose a mode, size, and unique seed:

```bash
./scripts/generate-playtest-world.sh --classic --size small --seed Majesty-Matrix-Small-001
./scripts/generate-playtest-world.sh --journey --size medium --seed Majesty-Matrix-Medium-001
./scripts/generate-playtest-world.sh --classic --size large --seed Majesty-Matrix-Large-001
```

The harness builds and installs the mod, gives the dedicated server a pseudo-terminal, monitors the first world-generation exception, requires the strict validation report, reloads the `.wld` and `.twld`, saves them, and reloads again to verify manifest persistence. It preserves logs under `.playtest/Logs` and refuses to overwrite an existing world.

Run the full size/mode matrix with:

```bash
./scripts/validate-worldgen-matrix.sh
```

The matrix uses a new temporary save directory and prints its location before generation.

## Install a generated world

Use the same mode, size, and seed used for generation:

```bash
./scripts/install-playtest.sh --classic --size large --seed Majesty-Matrix-Large-001
```

The installer copies the selected world and mod into the regular tModLoader save folders. It does not alter the enabled-mod list or overwrite an existing world.

See [MOD_DESIGN.md](MOD_DESIGN.md) for implementation ownership, [WORLDGEN_IDEAS.md](WORLDGEN_IDEAS.md) for the broader design catalog, [WORLDGEN_FUTURE_WORK.md](WORLDGEN_FUTURE_WORK.md) for implementation coverage and gaps, and [WORLDGEN_RESEARCH.md](WORLDGEN_RESEARCH.md) for the reference-world audit and research record.
