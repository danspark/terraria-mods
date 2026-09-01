# World-generation coverage and future work

This reference maps the 0.3.0 world generator against the design catalog in `WORLDGEN_IDEAS.md`. It names the areas that the overhaul implements, leaves partial, or delegates to vanilla Terraria. Use it to choose future work without assuming that a styled landmark means the surrounding biome has a complete terrain generator.

## Status definitions

| Status | Meaning |
| --- | --- |
| Implemented | Richer Biomes owns the final terrain or structure and validates its player-facing contract. |
| Partial | The generator changes part of the area, but a design requirement still lacks an owner or final-state validator. |
| Vanilla | Terraria owns generation. Richer Biomes avoids destructive overlap and may validate a minimum progression invariant. |
| Unsupported | Generation stops before mutation because the current plan cannot preserve the world's rules. |

## World structure and movement

| Area | Status | Current coverage | Future work |
| --- | --- | --- | --- |
| Surface heightfield | Implemented | The planner divides ordinary worlds into quiet lowlands, hills, plateaus, basins, valleys, and mountain ranges. It preserves coast margins and reserves building terraces. | Add biome-specific landform variants only after their movement and progression checks exist. |
| Surface traversal | Partial | Slope limits, terraces, mountain interiors, bridge headroom, and local platform openings keep authored features passable. | Reserve and validate one starter-character route across every surface region. The validator still lacks jump-height, fall, slope, door, and liquid-state simulation for the full route. |
| Transition zones | Implemented | Forest, Snow, Desert, Jungle, and world-evil boundaries receive 52–94 tile bands with stepped, curved, correlated material edges. Lowland blends extend 46 tiles below local surface; mountain blends continue through the above-surface body to 60 tiles below the world-surface datum. Final validation retains only visible seams with repeated crossings and at least eight tiles of lateral movement. | Add transition-specific surface props, short ecotone landmarks, and biome-aware cave blending without weakening the current geometry checks. |
| Local watersheds | Partial | Mountain valleys, sky lakes, and the flooded mine district contain bounded liquids. | Plan springs, streams, waterfalls, drains, oases, and meltwater as connected local systems. Add post-settle leak and downstream-structure validation. |
| Regional caves | Partial | Every planned region receives curved routes and chambers that reconnect after destructive vanilla passes. | Give each surface biome a cave grammar for chamber shape, materials, walls, liquids, route loops, and entrances. The current cave carver is biome-neutral. |
| Visual regression testing | Partial | Fixed seeds have strict tile validation and independent full-map, mountain, highland, bridge, mine, landmark, and transition crop inspection. | Move the inspector and crop renderer into the repository so CI can compare silhouettes and authored districts. |
| Seed coverage | Partial | The current audit covers multiple Classic small worlds across Corruption and Crimson plus a Classic-small, Journey-medium, and Classic-large release matrix. Every matrix row generates, reloads, saves, and reloads again. | Add a bounded ordinary-seed soak test, generation-time limits, and automatic failure artifact retention. |

## Surface and sky biomes

| Area | Status | Current coverage | Future work |
| --- | --- | --- | --- |
| Forest | Partial | Forest regions receive the shared heightfield, cave routes, accents, terraces, and a furnished landmark. | Add root caves, canopy routes, wooded ridges, streams, and Living Wood crossings. No region-wide vertical-forest generator currently owns these shapes. |
| Mountain ranges and valleys | Implemented | Ground-connected ranges reach Space and select from four interior grammars with broad wall-backed caves, shafts, climb aids, pots, rubble, vines, cloud belts, saddle valleys, and structural bridges. | Add stronger inherited-biome route families, summit ruins, natural arches, waterfalls, and more bridge damage states. |
| Desert | Partial | Desert materials survive mountain repair, and the biome receives a sandstone landmark. | Add mesas, dry channels, oases, staged sinkholes, and safe transitions into the Underground Desert. |
| Snow | Partial | Snow and ice materials survive mountain repair, and the biome receives a Boreal landmark. | Add broad glacial shelves, crevasses, meltwater, frozen falls, and sheltered routes. |
| Jungle | Partial | Mud and Jungle materials survive mountain repair, and the biome receives a Mahogany landmark. | Add mud terraces, cenotes, root canyons, waterfalls, dry bypasses, and several protected Underground Jungle entrances. |
| Corruption and Crimson | Partial | Vanilla evil generation remains intact. Richer Biomes adds evil landmarks and irregular sealed mine or valley districts. | Shape the full surface evil as faults or drainage basins, connect secondary cracks to vanilla orb or heart chambers, and validate a dry crossing. |
| Hallow | Vanilla | Vanilla Hardmode conversion creates the surface and underground Hallow. | If geological Hardmode conversion proves safe, make converted ridges, basins, routes, and mine branches follow the planned landforms. |
| Oceans and coasts | Partial | Coast height blending protects world edges, and each coast receives a Palm Wood landmark. | Add dunes, coves, tide pools, underwater shelves, and sealed sea caves without changing ocean volume or draining water. |
| Floating highlands | Implemented | Terraced Meadow, Cloud Basin, and Broken Archipelago highlands contain top, interior, and underside routes, chambers, vertical shafts, optional lakes, satellites, and cloud belts. At most one may attach to a mountain, and detached placement is normal. | Integrate more district variants and test live sky combat and fishing. Vanilla Floating Island loot and houses remain under vanilla ownership. |

## Underground, Cavern, and Underworld

| Area | Status | Current coverage | Future work |
| --- | --- | --- | --- |
| Ordinary Underground | Partial | Generic regional routes connect the Surface to deeper layers. A furnished Cavern landmark can occupy a safe site. | Add rooted dirt chambers, clay pockets, ponds, secondary exits, and gradual soil-to-stone transitions. |
| Underground Desert | Vanilla | Terraria owns the biome, its walls, enemies, fossils, and structures. Richer Biomes protects progression-sensitive cells from authored routes. | Add supported sediment halls and route loops around the vanilla biome without exposing it directly to the Surface. |
| Underground Snow | Partial | Generic routes can pass through Snow, and a Snow landmark exists. | Add tall frozen fissures, alternating ledges, slush basins, and dry returns around flooded pockets. |
| Underground Jungle | Partial | Generic routes can pass through Jungle, while Temple blocks and progression objects are protected. | Add connected root basins, cenote entries, honey side pockets, and a protected approach cavern outside the Temple. |
| Underground evil and Hallow | Vanilla | Terraria owns infection, conversion, enemy walls, and biome spread. | Add fault-shaped rooms and route-preserving conversion only with resource, spread, and progression tests. |
| Glowing Mushroom | Partial | The biome receives a furnished landmark and contextual accents. | Add large basins with dry rims, pools, tall mushroom chambers, and enough flat mud for natural growth and building. |
| Cavern provinces | Partial | Generic routes, mine branches, and a Cavern landmark add authored destinations. | Add broad halls, chimneys, dense mining zones, aquifers, lava shelves, and route loops while keeping vanilla ore distribution. |
| Granite, Marble, Spider Caves, and Bee Hives | Vanilla | Terraria owns these regions. Richer Biomes has no generator or region-specific final-state validator for them. | Build separate multi-room prototypes for each region. Each prototype needs its own wall, enemy-space, chest, liquid, and progression checks before integration. |
| Aether | Vanilla | Terraria owns the basin and its placement. Richer Biomes protects Shimmer cells during authored edits and validates a minimum surviving volume. | Add a planned approach and overlook rooms around the protected basin. Validate transmutation space, biome materials, and side-of-world placement. |
| Underworld | Partial | The Underworld receives a furnished Ash Wood landmark and contextual debris. Broad terrain and lava remain vanilla. | Add ash shelves, lava deltas, pillar fields, broken crossings, and ruined districts. Validate a Wall of Flesh combat corridor and Hellforge access. |

## Structures, resources, and progression

| Area | Status | Current coverage | Future work |
| --- | --- | --- | --- |
| Biome landmarks | Implemented | Required biome categories receive large, styled, furnished exploration structures with mixed unsafe walls, thick foundations, open side arches, bounded platform openings, and final tile validation. They author no doors and must fail Terraria's real NPC-housing query. | Add more silhouette and district families, partially ruined variants, and biome-specific furniture styles without weakening the traversal or anti-housing checks. |
| Guaranteed surface mine | Implemented | The visible headframe connects to a cyclic rail graph with work, mountain, flooded, collapsed, and sealed evil districts. | Add safe minecart-switch frame tests, more damage states, drainage links, and secondary surface exits. |
| Vanilla cabins, Living Trees, pyramids, shrines, and ambient minecart tracks | Vanilla | Terraria owns their placement, layout, loot, and frequency. | Decide per structure whether Richer Biomes should improve the approach, expand the structure, or only protect it. Add structure-specific validators before changing any layout. |
| Dungeon, Jungle Temple, and Aether approaches | Partial | Richer Biomes protects progression tiles and validates minimum Dungeon brick, Temple brick, and Shimmer counts. | Add planned approach terrain and stronger checks for entrances, sealed boundaries, required rooms, objects, wiring, and usable Shimmer volume. |
| Ores, gems, chests, traps, pots, and loot tables | Vanilla | Vanilla distribution and rewards remain authoritative. Authored landmarks and mine districts are loot-neutral. | Add rewards only with world-size budgets and checks that prevent duplicated progression items or abnormal resource density. |
| Trees, plants, vines, cactus, coral, and ambient biome decoration | Partial | Terraria owns region-wide vegetation. Richer Biomes adds contextual debris and explicit mountain vines, pots, rubble, torches, and climb aids. | Add planting passes for each biome-specific terrain family after their route and structure bounds become stable. |
| Hardmode conversion and biome spread | Vanilla | Terraria creates the diagonal Hallow and evil bands and handles later spread. | Prototype geological seams last. Preserve Souls, Crystal growth, Jungle viability, containment rules, and protected structures on every world size. |

## Support boundaries

- Ordinary non-secret worlds are supported. Secret seeds are unsupported because they change layer, biome, and progression assumptions.
- Richer Biomes targets its own world plan. Compatibility with another total world-generation replacement needs a separate integration contract.
- Generation changes new worlds. The mod does not retrofit an existing `.wld` with new terrain or structures.
- The current scope uses vanilla content. Custom tiles, walls, furniture, enemies, items, music, quests, and progression systems need separate content designs.

## Explicitly untouched by this overhaul

These systems remain future work even though nearby authored features may pass through or protect them:

- vanilla Floating Island houses and loot allocation;
- Living Trees, pyramids, sword shrines, ordinary Underground Cabins, vanilla minecart networks, traps, and loot tables;
- the internal layouts and progression rules of the Dungeon, Jungle Temple, and Aether;
- region-wide Forest canopy generation, Desert mesas and oases, Snow glaciers, Jungle cenotes, Ocean shelves, and full evil-biome fault shaping;
- typed cave provinces for ordinary Underground, Underground Desert, Underground Snow, Underground Jungle, Glowing Mushroom, Granite, Marble, Spider, Hive, Cavern, and Underworld terrain;
- ore, gem, chest, statue, herb, cactus, coral, tree, and most plant distribution;
- Hardmode Hallow generation, diagonal conversion bands, later biome spread, and containment behavior;
- custom enemies, music, quests, items, tiles, walls, furniture, and progression rewards;
- retrofitting generated features into existing worlds; the overhaul applies only while creating a new supported world.

## Recommended sequence

1. Implement a complete vertical-forest region with a starter-character traversal test.
2. Add local watershed records and leak validation to `WorldPlan`.
3. Replace the biome-neutral regional cave shape with typed cave grammars, starting with Forest, Snow, and Jungle.
4. Add Desert, Ocean, Mushroom, and secondary underground region prototypes with their progression checks.
5. Build Underworld districts and verify the Wall of Flesh route.
6. Strengthen progression-site validation before changing Dungeon, Temple, Aether, or loot-bearing structures.
7. Prototype geological Hardmode conversion after every pre-Hardmode region has a stable route contract.
