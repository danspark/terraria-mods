# Richer Biomes

Richer Biomes is a tModLoader world-generation mod. Version 0.1 implements the first playable milestone from `WORLDGEN_IDEAS.md`: a layered forest, a sky-piercing mountain with an interior crossing, and a supported surface mine that reaches the cave layer.

The mod uses Terraria's vanilla tiles and progression. It does not add items, enemies, recipes, or blocks.

## Build

Run:

```bash
./scripts/build-mod.sh
```

The script locates the Steam tModLoader installation, compiles against its exact API, and finds the resulting `RicherBiomes.tmod` package.

## Generate the playtest world

Run:

```bash
./scripts/generate-playtest-world.sh
./scripts/generate-playtest-world.sh --journey
```

The first command creates a Classic world; the second creates a Journey world. Both use the same fixed seed, wait for tModLoader's route validation, reload the saved world to verify its mode, and leave artifacts under `.playtest/`. The script never deletes an existing world.

After validation, `./scripts/install-playtest.sh --classic` or `./scripts/install-playtest.sh --journey` copies the selected world and mod into the normal tModLoader save folders without changing the enabled-mod list.

Create a geared test character from the repository root with `./tools/create-test-character.sh [--journey|--classic] [name]`. The default is a Journey character named `god`. Journey mode is required for Terraria's native godmode. The character has a Suspicious Looking Tentacle in the light-pet slot.
