# Richer Biomes mod design

## First playable milestone

A new world contains one featured corridor that starts outside the spawn clearing. Moving away from spawn takes a new character through a layered forest, across a sky-piercing mountain by either its surface or its protected interior passage, and into a supported surface mine that reaches the cave layer.

The first milestone implements the success case in `WORLDGEN_IDEAS.md`. Desert, snow, Jungle, evil, ocean, sky-island, Underworld, and Hardmode experiments remain later work.

## Usage

tModLoader calls `RicherBiomesWorldSystem.ModifyWorldGenTasks`. The system appends five passes after vanilla `Final Cleanup`:

1. Plan the corridor from the completed vanilla world.
2. Shape the forest and mountain.
3. Carve the three route bands and the mine.
4. Add vanilla trees, timber, rope, platforms, walls, and lights.
5. inspect the resulting tiles and reject an invalid world.

## Types and ownership

`WorldPlan` owns corridor orientation, logical distances, feature spans, elevation, and depth. Logical distance always increases away from spawn, so generators do not branch on left-facing and right-facing worlds.

`WorldPlanner` chooses the safer side of spawn and derives dimensions from the finished vanilla terrain. It prefers ordinary forest ground and penalizes chests, progression tiles, and special liquids.

`LandformGenerator` owns solid terrain. `RouteGenerator` owns the forest's low and high routes plus the mountain crossing. `MineGenerator` owns the quarry, shaft, branches, and supports. `TileEditor` is the only low-level tile mutation boundary.

`WorldValidator` reads the actual tile grid after every generator finishes. It checks the spawn buffer, vertical relief, Space-height summit, interior headroom and floor continuity, mine depth, and route furniture. A failed check throws during world generation, so an invalid playtest world is not silently accepted.

`RicherBiomesWorldSystem` owns tModLoader lifecycle hooks and saved metadata. The `.twld` sidecar records the corridor direction and validation report so the player gets a short orientation message on entry.

## Alternatives considered

Replacing vanilla terrain generation would give the world skeleton complete control before biome assignment. It also requires reimplementing or carefully adapting every vanilla progression-site assumption before the first playable world. That is too broad for the first milestone.

Editing terrain after vanilla generation gives this prototype a bounded ownership area. The corridor avoids the spawn clearing and world edges, uses no custom content, and leaves progression sites elsewhere in the large world untouched. This is the chosen design.

Generating a collection of small prefabs would be easier, but it would fail the brief's scale and connected-route requirements. The corridor is generated from continuous profiles and routes rather than stamped rooms.

## Risks

The late pass intentionally owns its corridor, so broad world-generation mods can conflict with it. Compatibility with those mods is outside the current brief.

The movement validator checks tile-level headroom, floor continuity, fall breaks, and route dimensions. It does not simulate every Terraria movement accessory or enemy encounter. The generated large world remains the final playtest artifact.
