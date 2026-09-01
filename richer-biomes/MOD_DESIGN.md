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
| After `Shimmer` | Reserve build terraces and the guaranteed mine graph around progression sites. |
| After `Hives` | Form mountain valleys, refill sky lakes, excavate mine sections, and carve rail corridors. |
| After `Smooth World` | Repair routes and terraces, then construct mountain bridges. |
| After `Micro Biomes` | Stabilize summit buttresses, reopen late cave and mountain routes, repair highland keels, and build landmark shells. |
| After `Stalac` | Add sparse, biome-aware accents while preserving quiet space. |
| After `Final Cleanup` | Rebuild and furnish landmarks, lay the complete rail union, repair final highland keels and bridge portals, record final features, and validate the actual world. |

## Mountains, valleys, and bridges

Each mountain region is a ground-connected twin-peak range. Planned peaks sit near 23% of `worldSurface`, safely inside the Space band. The surface heightfield, pre-island reinforcement, and late summit buttress protect the silhouette against desert, cave, and micro-biome passes.

Four foothill-to-hall routes provide two candidates on each side. Two internal chambers and rope chimneys connect the crossing to summit elevations. The final validator rejects cloud-only false peaks and requires at least 32 Space-band columns, two surviving entrances, and 24 cloud-belt tiles.

The saddle receives one seeded valley theme: wooded, water lake, lava basin, or sealed world-evil grotto. Liquid and evil boundaries use column-correlated jitter instead of rectangular fills. A bridge joins the inner shoulders of every range. Bridge styles rotate between timber suspension, stone arch, and rail trestle. Every bridge has a three-tile structural deck, custom wall panels or trusses, supported platform drop bays, clear headroom, and wired actuated portals at both mountain approaches. Final validation measures those properties on the retained tiles rather than trusting the build pass.

## Floating highlands

A highland attaches to the mountain peak farther from world center, allowing mountain and sky terrain to collide without covering the spawn approach. Its size is fixed by world size rather than shrinking into a prefab:

| World | Main mass | Count |
| --- | --- | --- |
| Small | 280×90 tiles | 1 |
| Medium | 360×110 tiles | up to 2 |
| Large | 440×140 tiles | up to 2 |

The authored mass uses grass, dirt, Sunplate, Cloud, and Rain Cloud; ordinary Stone is rejected inside the body because it makes the sky biome read as a displaced cave. Correlated material fields create large Cloud, Rain Cloud, and Sunplate clusters without horizontal banding. It contains a walkable top, four irregular interior chambers, upper and lower galleries, seven-tile vertical shafts with broad landings, a three-tile underside keel, a lake, and orbiting satellites joined by thick causeways. The keel is repaired again at the final ownership boundary after mountain crossings and landmark cleanup. The validator measures the largest eight-neighbor component, including platforms, and requires meaningful width and depth.

## Guaranteed surface mine

The mine planner searches mountain, plateau, hill, and bounded random candidates, excluding spawn, Dungeon, protected structures, and progression tiles. Small, medium, and large districts span about 560, 700, and 840 tiles horizontally.

Eleven required rail edges connect a visible surface headframe to upper, middle, and deep Cavern workings. Three additional spurs lead to flooded, collapsed, and sealed world-evil districts. The graph has at least three degree-three junctions, two independent cycles, and four horizontal lines; constrained worlds compress the distance between depth bands instead of creating an impossible rail grade. The eight districts are Workyard, Working, Mountain Rail, Flooded, Collapsed, and Sealed Evil variants. Supports, mixed plank and stone walls, platform drop portals, ropes, torches, work benches, tables, chairs, anvils, debris, junction stations, and a workyard loft make the route read as an abandoned industrial place rather than an empty tunnel.

Track construction is graph-first: rasterize every edge into a set, prepare all cells, place all tracks, then frame the full graph once. Each edge is a piecewise flat–45-degree–flat route with at most two grade changes, so the line is deliberate rather than a rounded, wobbly interpolation. The validator flood-fills from the exact authored surface entrance. Every cell of every authored edge must exist inside that component; all required edges must survive; minimum connected sizes are 300, 500, and 700 tiles by world size. The sealed evil district uses a five-tile irregular Gray Brick shell, a wired actuated quarantine gate, and a varied top profile.

## Biome landmarks

The landmark planner classifies the final support tiles and searches deterministic candidates outside the mine, terraces, prior landmarks, and protected content. Each accepted area is 39–51 tiles wide and contains two to four rooms.

Biome-specific foundations, wall panels, glass windows, framed trim, roof variants, dormers, chimneys, supported porches, lofts, doors, torches, chandeliers, work furniture, seating, tables, bookcases, benches, and debris create eleven distinct structures. A solid loft uses one bounded platform-and-rope drop bay instead of replacing the floor with a platform strip. A final landmark must retain at least two wall types, a framed window field, three furniture families, a two-layer solid foundation across at least 90% of its width, and a bounded platform count. Landmark shells are rebuilt after vanilla cleanup, then furnished. Embedded Cavern and Underworld fallbacks use the same final-floor ownership rule as surface candidates.

Furniture validation scans retained multitile footprints inside the final area. Placement return values do not count as proof. The structures remain loot-neutral.

## Safety and determinism

`WorldPlanner` consumes one value from `WorldGen.genRand`, then uses local seeded random streams for the plan and individual features. Candidate budgets are fixed. `StructureMap` reservations prevent later overlap, while late repair paths may ignore stale reservations only when their tile-level safety checks still protect chests, frame-important objects, wires, actuators, Shimmer, Dungeon and Temple blocks, and other progression content.

Regional cave repairs reserve their restored paths. Spawn always receives a 150-tile terrace, and larger worlds receive additional quiet-ground terraces. Invalid secret seeds stop before mutation.

## Saved data and validation

Manifest version 3 saves terraces, landmarks, mountains, valleys, bridges, sky highlands, mine sections, the mine graph summary, accents, and the final validation summary. Loading restores this manifest even though no generation plan exists at runtime. Saving a loaded world preserves the restored data.

The headless harness exercises generation, validation, `.wld`/`.twld` creation, first reload, save, and second reload. It uses a pseudo-terminal so tModLoader's `Console.ReadKey` error path cannot hide the first world-generation exception.

The final validator also checks regional relief, cave-route headroom and continuity, terrace flatness, eleven landmark categories, contextual accent density, Dungeon and Temple blocks, world-evil objects, and Shimmer cells. A failed contract throws during generation and the harness rejects the artifact.

## Compatibility boundary

Version 0.3.0 targets ordinary Terraria 1.4.4.9 worlds on tModLoader 2026.07.3.0. Other world-generation mods can compete for pass anchors or protected sites. Richer Biomes detects those conflicts through named anchors, `StructureMap`, tile allow lists, bounded searches, and final scans; it does not promise compatibility with another total world replacement.
