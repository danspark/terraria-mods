# Full-world generation design

Richer Biomes owns a planned world skeleton early, then repairs and decorates named features around vanilla world-generation passes. A feature is complete only when its final tiles preserve its silhouette, traversal graph, districts, biome style, and signs of use or decay.

## Feature grammar

Every major region is built in five layers and verified by a sixth:

1. **Silhouette:** the large-scale outline visible on the map and while approaching.
2. **Route graph:** a primary route, branches, loops, entrances, and exits.
3. **Districts:** chambers or subregions with different movement and story roles.
4. **Style:** local biome materials, walls, liquids, furniture, and accents.
5. **Age:** collapsed, flooded, overgrown, sealed, or repaired sections that prevent repetition.
6. **Validation:** measurements of the finished tile grid, not placement return values or planned coordinates.

The generator reserves complete graphs before placing them. It prepares a union of all authored rail cells before laying any track, so one route cannot erase the approach to another. Structure bounds derive from the final floor elevation, not an early surface sample.

Authored construction follows a thickness and transition grammar. Walkable solid floors, bridge decks, gallery keels, and foundations are at least three tiles thick. Platforms are reserved for short stairs, drop bays, landings, and other places where the player must pass through a floor. Long one-tile strips are rejected. Organic borders and material changes use bounded, correlated noise; independent per-tile randomness and periodic stripes are not acceptable substitutes for shape.

## Pass ownership

`RicherBiomesWorldSystem.ModifyWorldGenTasks` fails with the missing anchor name if the expected vanilla pass list changes.

| Vanilla anchor | Richer Biomes ownership |
| --- | --- |
| After `Terrain` | Plan and form the regional heightfield, soil, stone, and walls. |
| Before `Floating Islands` | Reinforce biome-aware twin-peak mountain silhouettes and carve early crossings. |
| After `Floating Islands` | Form large sky highlands, satellites, lakes, galleries, underside routes, and mountain cloud belts. |
| After `Wavy Caves` | Carve regional surface-to-Cavern routes and loops. |
| After `Corruption` | Reopen regional and mountain routes without overwriting sensitive tiles. |
| After the reopened mountain routes | Blend broad, depth-varying material seams between adjacent surface biomes. |
| After `Shimmer` | Reserve build terraces and the guaranteed mine graph around progression sites. |
| After `Hives` | Form mountain valleys, refill sky lakes, excavate mine sections, and carve rail corridors. |
| After `Smooth World` | Repair routes and terraces. |
| After `Micro Biomes` | Stabilize summit buttresses, reopen late cave and mountain routes, repair highland keels, construct mountain bridges, and build landmark shells. |
| After `Stalac` | Add sparse, biome-aware accents while preserving quiet space. |
| After `Final Cleanup` | Repair visible biome seams and valley payloads, rebuild and furnish landmarks, lay the complete rail union, finish mountain host-biome skins and organic wall fields, repair mountain decoration and entrances, refill valley liquids, repair highland keels and vertical routes, bridge portals, grounding spines, regional routes, terraces, and rails, then record and validate the actual world. |

## Mountains, valleys, and bridges

Each mountain region is a ground-connected twin-peak range. The planner selects a Highland, Alpine, or Sky-piercing altitude family, with weighted shares of three, three, and two. Adjacent ranges cannot repeat the same family. Peak positions, peak heights, crown widths, shoulders, asymmetry, and saddle depths are seeded independently. Highland and Alpine peaks remain below Space; Sky-piercing peaks deliberately enter it. The surface heightfield, pre-island reinforcement, and late summit buttress protect the silhouette against desert, cave, and micro-biome passes.

Each range selects one of four seeded interior grammars: Branching Grottoes, Switchback Climb, Split-Level Caves, or Open Fault. Two visible foothill entrances feed a winding primary crossing, summit routes, irregular shafts, and eight to ten chambers with varied horizontal and vertical radii. Each column samples stable terrain beneath the artificial body and carries that material family up the range. Snow remains Snow and Ice; Jungle uses Jungle Grass and Mud; Desert uses hardened sand and sandstone; evil columns use their Corruption or Crimson families; ordinary ground uses Grass, Dirt, and Stone. A final natural-tile-only skin pass restores this ownership after vanilla biome mutation without touching slopes, transitions, structures, liquids, or frame-important objects.

Open cells receive coordinate-warped natural walls rather than rectangular patches. Correlated wall voids leave dark, open-background cave districts. Two to four chambers gain suspended, irregular natural ledges with the same host-biome material and clustered vine curtains, while route-distance checks preserve the main crossing. A separate decoration pass places pots, rubble, torches, ropes, platforms, and furnished ledge vignettes, then repairs deterministic minimums after late cave work.

The final validator measures natural ground only in a bounded band around the authored surface, so detached sky bodies cannot masquerade as summits and large interior faults cannot hide a valid crown. It checks the planned altitude family, requires clouds only from Sky-piercing ranges, and verifies two entrances, majority host-biome material ownership, substantial wall-backed cave area, open-background cells, suspended natural ledges, at least three vine curtains, wide cavities across at least one third of the range, organic wall boundaries without long exact-axis seams, six pots, twenty vine tiles, and eighteen climbing-aid tiles. Multiple-range worlds must use at least two altitude and interior families.

The saddle receives one seeded valley theme: wooded, water lake, lava basin, or sealed world-evil grotto. Liquid and evil boundaries use column-correlated jitter instead of rectangular fills. A bridge joins the inner shoulders of every range. Bridge styles rotate between timber suspension, stone arch, and rail trestle. Every bridge has a three-tile structural deck, custom wall panels or trusses, supported platform drop bays, clear headroom, and wired actuated portals at both mountain approaches. A final corridor owner clears incidental terrain and reapplies actuator state to intentional endpoint posts. Validation rejects any solid tile in either planned entry corridor.

## Floating highlands

A highland is not an automatic mountain cap. Each world has a one-in-five chance to attach at most one highland, and only Alpine or Sky-piercing ranges are eligible. The remaining highlands are deliberately placed away from mountain envelopes and prior sky masses. An attachment uses the peak farther from world center so it does not cover the spawn approach. Size is fixed by world size rather than shrinking into a prefab:

| World | Main mass | Count |
| --- | --- | --- |
| Small | 280×90 tiles | 1 |
| Medium | 360×110 tiles | up to 2 |
| Large | 440×140 tiles | up to 2 |

The three grammars are Terraced Meadow, Cloud Basin, and Broken Archipelago. They vary profile, chamber scale, satellite count, vertical access, and whether a lake exists. The authored mass uses grass, dirt, Sunplate, Cloud, and Rain Cloud; ordinary Stone is rejected inside the body because it makes the sky biome read as a displaced cave. Correlated material fields create large Cloud, Rain Cloud, and Sunplate clusters without horizontal banding. Each style retains a walkable top, irregular wall-backed interior space, upper and lower galleries, broad shafts and landings, a structural underside keel, and satellites or causeways appropriate to the grammar. The keel is repaired again at the final ownership boundary after mountain crossings and landmark cleanup. The validator measures the largest eight-neighbor component, including platforms, and requires meaningful width and depth, style variety, and bounded attachment count.

## Surface biome transitions

The transition pass identifies long neighboring runs of Forest, Snow, Desert, Jungle, and world evil. A 52–94 tile band blends the two palettes through 46 tiles of ordinary lowland soil. Where a boundary crosses a mountain-scale landform, the blend continues through the entire above-surface mountain body to 60 tiles below the world-surface datum. Its boundary uses a deterministic depth profile, a low-amplitude curve, seeded offsets, and correlated edge noise. This guarantees large lateral changes without turning the seam into per-tile static or exposing a vertical palette wall below a summit.

Later structures own their exact cells. The final seam pass repairs only natural blendable tiles and omits bands that a valley, terrace, landmark, bridge, highland, or mine district has substantially occluded. The validator requires at least two surviving seams on small worlds and three on medium or large worlds. Each retained seam needs at least six observable depth crossings and at least eight tiles of lateral movement.

## Guaranteed surface mine

The mine planner searches mountain, plateau, hill, and bounded random candidates, excluding spawn, Dungeon, protected structures, and progression tiles. Small, medium, and large districts span about 560, 700, and 840 tiles horizontally.

Eleven required rail edges connect a visible surface headframe to upper, middle, and deep Cavern workings. Three additional spurs lead to flooded, collapsed, and sealed world-evil districts. The graph has at least three degree-three junctions, two independent cycles, and four horizontal lines; constrained worlds compress the distance between depth bands instead of creating an impossible rail grade. The eight districts are Workyard, Working, Mountain Rail, Flooded, Collapsed, and Sealed Evil variants. Supports, mixed plank and stone walls, platform drop portals, ropes, torches, work benches, tables, chairs, anvils, debris, junction stations, and a workyard loft make the route read as an abandoned industrial place rather than an empty tunnel.

Track construction is graph-first: rasterize every edge into a set, prepare all cells, place all tracks, then frame the full graph once. Each edge is a piecewise flat–45-degree–flat route with at most two grade changes, so the line is deliberate rather than a rounded, wobbly interpolation. The excavation envelope samples a smooth 29-cell height field with occasional broad three-tile swells, producing changing arch height and width while retaining six guaranteed clearance tiles. Supports consult the complete graph's headroom envelope, which lets branches cross at different elevations without blocking one another. A final ownership pass runs after every summit, cave, terrace, seam, and landmark repair; it restores the full rail union and minimum construction clearance without filling the extra cavern air.

The validator flood-fills from the exact authored surface entrance. Every cell of every authored edge must exist inside that component, every cell must retain six tiles of player headroom, at least one tenth of the graph must retain nine or more clear tiles, and the final ceiling range must span at least three tiles. All required edges must survive, and minimum connected sizes are 300, 500, and 700 tiles by world size. The sealed evil district uses a five-tile irregular Gray Brick shell, a wired actuated quarantine gate, and a varied top profile.

## Biome landmarks

The landmark planner classifies the final support tiles and searches deterministic candidates outside the mine, terraces, biome transitions, prior landmarks, and protected content. Each accepted area is approximately 55–83 tiles wide and contains connected ground bays plus upper rooms. Ocean landmarks have stilt-pier fallbacks; Forest, Snow, Desert, and Jungle have embedded fallbacks for difficult terrain.

Biome-specific foundations, wall panels, framed trim, two-tile posts and ceilings, filled gables, correctly oriented sloped roofs, diagonal platform stairs, landings, supports, torches, chandeliers, work furniture, seating, tables, bookcases, benches, and debris create the exploration structures. Forest landmarks use ordinary Wood Blocks, Wood walls, and Wooden Beams; Living Wood is reserved for terrain and tree-like structures. Other furniture palettes follow the target Terraria version's Snow, Desert, Jungle, Mushroom, and wood-house styles. Foundation ends use slopes, half blocks, and irregular depth so the prepared shelf does not end as a straight wall. Multitile objects are placed through `WorldGen` and accepted only when their final tiles confirm the placement; frame coordinates are never treated as a substitute for an object. Surface landmarks author no doors. Rounded open side arches, unsafe natural walls, and incomplete room boundaries ensure the structures fail Terraria's real NPC-housing query.

A final landmark must retain at least two wall types, three furniture families, a two-layer solid foundation across at least 90% of its width, sloped roof tiles, sloped stair platforms, open approaches, no walls above the roof envelope, no door tiles, and a bounded platform count. Wall placement is limited to strict room interiors. Landmark shells are rebuilt after vanilla cleanup, then furnished. Embedded Cavern and Underworld fallbacks use the same final-floor ownership rule as surface candidates.

Furniture validation scans retained multitile footprints inside the final area. Placement return values do not count as proof. The structures remain loot-neutral.

## Safety and determinism

`WorldPlanner` consumes one value from `WorldGen.genRand`, then uses local seeded random streams for the plan and individual features. Candidate budgets are fixed. `StructureMap` reservations prevent later overlap, while late repair paths may ignore stale reservations only when their tile-level safety checks still protect chests, frame-important objects, wires, actuators, Shimmer, Dungeon and Temple blocks, and other progression content.

Regional cave repairs reserve their restored paths. Spawn always receives a 150-tile terrace, and larger worlds receive additional quiet-ground terraces. Invalid secret seeds stop before mutation.

## Saved data and validation

Manifest version 4 saves terraces, landmark room/layout/furniture records, mountain grammar and decoration measurements, valleys, bridges, highland style and attachment state, surviving biome transitions, mine sections, the mine graph summary, accents, and the final validation summary. Loading restores this manifest even though no generation plan exists at runtime. Saving a loaded world preserves the restored data.

The headless harness exercises generation, validation, `.wld`/`.twld` creation, first reload, save, and second reload. It uses a pseudo-terminal so tModLoader's `Console.ReadKey` error path cannot hide the first world-generation exception.

The final validator also checks regional relief, cave-route headroom and continuity, terrace flatness, eleven landmark categories, contextual accent density, Dungeon and Temple blocks, world-evil objects, and Shimmer cells. A failed contract throws during generation and the harness rejects the artifact.

## Compatibility boundary

Version 0.3.1 targets ordinary Terraria 1.4.4.9 worlds on tModLoader 2026.07.3.0. Other world-generation mods can compete for pass anchors or protected sites. Richer Biomes detects those conflicts through named anchors, `StructureMap`, tile allow lists, bounded searches, and final scans; it does not promise compatibility with another total world replacement.
