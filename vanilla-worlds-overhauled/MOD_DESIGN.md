# Full-world generation design

Vanilla Worlds Overhauled owns a planned world skeleton early, then repairs and decorates named features around vanilla world-generation passes. A feature is complete only when its final tiles preserve its silhouette, traversal graph, districts, biome style, and signs of use or decay.

## Feature grammar

Every major region is built in five layers and verified by a sixth:

1. **Silhouette:** the large-scale outline visible on the map and while approaching.
2. **Route graph:** a primary route, branches, loops, entrances, and exits.
3. **Districts:** chambers or subregions with different movement and story roles.
4. **Style:** local biome materials, walls, liquids, furniture, and accents.
5. **Age:** collapsed, flooded, overgrown, sealed, or repaired sections that prevent repetition.
6. **Validation:** measurements of the finished tile grid, not placement return values or planned coordinates.

The generator reserves complete graphs before placing them. It prepares a union of all authored rail cells before laying any track, so one route cannot erase the approach to another. Structure bounds derive from the final floor elevation, not an early surface sample.

Authored construction follows a thickness and transition grammar. Walkable solid floors, bridge decks, gallery keels, and foundations are at least three tiles thick. Platforms are reserved for short stairs, drop bays, landings, and other places where the player must pass through a floor. Long one-tile strips are rejected. Organic borders and material changes share a deterministic, domain-warped field with macro, detail, and grain scales. Constant rows, constant columns, rectangle edges, independent per-tile randomness, and periodic stripes are not acceptable substitutes for shape. Functional rails, floors, doors, decks, and reserved traversal lanes may remain straight; their decorative material fields and joins into natural terrain may not. Late repairs reuse the original boundary function.

## Pass ownership

`VanillaWorldsOverhauledWorldSystem.ModifyWorldGenTasks` fails with the missing anchor name if the expected vanilla pass list changes.

| Vanilla anchor | Vanilla Worlds Overhauled ownership |
| --- | --- |
| After `Terrain` | Plan and form the regional heightfield, soil, stone, and walls. |
| Before `Floating Islands` | Reinforce biome-aware twin-peak mountain silhouettes and carve early crossings. |
| After `Floating Islands` | Form large sky highlands, satellites, lakes, galleries, underside routes, and mountain cloud belts. |
| After `Wavy Caves` | Carve regional surface-to-Cavern routes and loops. |
| After `Corruption` | Reopen regional and mountain routes without overwriting sensitive tiles. |
| After the reopened mountain routes | Blend broad, depth-varying material seams between adjacent surface biomes. |
| After `Shimmer` | Reserve build terraces and the guaranteed mine graph around progression sites. |
| After `Hives` | Form mountain valleys, optional Forest lake crossings, and protected mountain ponds and lakes; reserve those scenes before excavating mine sections and rail corridors. |
| After `Smooth World` | Repair routes and terraces. |
| After `Micro Biomes` | Stabilize summit buttresses, reopen late cave and mountain routes, repair highland keels, construct mountain bridges, and build landmark shells. |
| After `Stalac` | Add sparse, biome-aware accents while preserving quiet space. |
| After `Final Cleanup` | Repair visible biome seams and valley payloads, rebuild and furnish landmarks, lay the complete rail union, finish mountain host-biome skins and organic wall fields, repair mountain decoration and entrances, refill valley liquids, repair highland keels and vertical routes, bridge portals, grounding spines, regional routes, terraces, and rails, then record and validate the actual world. |

## Mountains, valleys, and bridges

Each mountain region is a ground-connected twin-peak range. The planner selects a Highland, Alpine, or Sky-piercing altitude family, with weighted shares of three, three, and two. Adjacent ranges cannot repeat the same family. Peak positions, peak heights, crown widths, shoulders, asymmetry, and saddle depths are seeded independently. Highland and Alpine peaks remain below Space; Sky-piercing peaks deliberately enter it. The surface heightfield, pre-island reinforcement, and late summit buttress protect the silhouette against desert, cave, and micro-biome passes.

Each range selects one of four seeded interior grammars: Branching Grottoes, Switchback Climb, Split-Level Caves, or Open Fault. Two visible foothill entrances feed a winding primary crossing, summit routes, irregular shafts, and eight to ten chambers with varied horizontal and vertical radii. Each column samples stable terrain beneath the artificial body and carries that material family up the range. Snow remains Snow and Ice; Jungle uses Jungle Grass and Mud; Desert uses hardened sand and sandstone; evil columns use their Corruption or Crimson families; ordinary ground uses Grass, Dirt, and Stone. A final natural-tile-only skin pass restores this ownership after vanilla biome mutation without touching slopes, transitions, structures, liquids, or frame-important objects.

Open cells receive coordinate-warped natural walls rather than rectangular patches. Correlated wall voids leave dark, open-background cave districts. Two to four chambers gain suspended, irregular natural ledges with the same host-biome material and clustered vine curtains, while route-distance checks preserve the main crossing. Every range also receives at least two protected Spring Pond, Cavern Lake, or Hanging Pool bodies. Their beds, shores, shells, walls, and spill lips use stored feature seeds and correlated contours; the final owner replays those contours before measuring the settled liquid. A separate decoration pass places pots, rubble, torches, ropes, platforms, furnished ledge vignettes, and humidity-biased vine curtains around water, then repairs deterministic minimums after late cave work.

The final validator measures natural ground only in a bounded band around the authored surface, so detached sky bodies cannot masquerade as summits and large interior faults cannot hide a valid crown. It checks the planned altitude family, requires clouds only from Sky-piercing ranges, and verifies two entrances, majority host-biome material ownership, substantial wall-backed cave area, open-background cells, suspended natural ledges, at least three long vine curtains and roughly 220 or more retained vine tiles, wide cavities across at least one third of the range, two water bodies with at least 240 combined water cells, organic block, wall, bed, and shoreline boundaries without long exact-axis seams, six pots, and eighteen climbing-aid tiles. Multiple-range worlds must use at least two altitude and interior families.

The saddle receives one seeded valley theme: wooded, water lake, lava basin, or sealed world-evil grotto. Liquid and evil boundaries use column-correlated jitter instead of rectangular fills. A bridge joins the inner shoulders of every range. Bridge styles rotate between timber suspension, stone arch, and rail trestle. Every bridge has a three-tile structural deck, custom wall panels or trusses, supported platform drop bays, clear headroom, and wired actuated portals at both mountain approaches. A final corridor owner clears incidental terrain and reapplies actuator state to intentional endpoint posts. Validation rejects any solid tile in either planned entry corridor.

## Forest lake crossings

Forest lake crossings are optional world scenes, not a required attachment to the Forest landmark. Their deterministic occurrence chance rises from 58% on small worlds to 72% on large worlds, where a second scene can fit. Candidate search requires broad Forest support, moderate finished relief, distance from spawn and protected features, and enough room for organic approaches. The scene is reserved before the mine and landmarks so later graphs route around the complete lake and bridge footprint.

The deck may stay level for readable traversal. Its Timber Footbridge, Living Wood Causeway, or Stone-and-Timber structure receives varied beam supports and drop bays, while a stored feature seed shapes the bed, shoreline, retaining material, walls, abutments, and exterior blend. Final repair replays the same shape instead of clearing a box. Validation requires a continuous clear deck, retained supports, at least 240 water cells, a bed with vertical span and repeated direction changes, and Forest support at both approaches.

## Floating highlands

A highland is not an automatic mountain cap. Each world has a one-in-five chance to attach at most one highland, and only Alpine or Sky-piercing ranges are eligible. The remaining highlands are deliberately placed away from mountain envelopes and prior sky masses. An attachment uses the peak farther from world center so it does not cover the spawn approach. Size is fixed by world size rather than shrinking into a prefab:

| World | Main mass | Count |
| --- | --- | --- |
| Small | 280×90 tiles | 1 |
| Medium | 360×110 tiles | up to 2 |
| Large | 440×140 tiles | up to 2 |

The three grammars are Terraced Meadow, Cloud Basin, and Broken Archipelago. They vary profile, chamber scale, satellite count, vertical access, and whether a lake exists. The authored mass uses grass, dirt, Sunplate, Cloud, and Rain Cloud; ordinary Stone is rejected inside the body because it makes the sky biome read as a displaced cave. Correlated material fields shape the topsoil, Cloud, Rain Cloud, Sunplate, lower keel, satellite caps, causeways, and lake floors without horizontal banding. Each style retains a walkable top, irregular wall-backed interior space, upper and lower galleries, broad shafts and landings, a structural underside keel, and satellites or causeways appropriate to the grammar. The keel is repaired again at the final ownership boundary after mountain crossings and landmark cleanup. The validator measures the largest eight-neighbor component, including platforms, requires meaningful width and depth, style variety, and bounded attachment count, and rejects long exact-axis terrain-layer seams in the authored upper body.

## Surface biome transitions

The transition pass identifies long neighboring runs of Forest, Snow, Desert, Jungle, and world evil. A 68–116 tile band blends the two palettes through 46 tiles of ordinary lowland soil. Where a boundary crosses a mountain-scale landform, the blend continues through the entire above-surface mountain body to 60 tiles below the world-surface datum. Independent domain-warped fields control the block and wall crossings, while each palette's shallow and deep material layers use their own correlated profiles. This produces broad folds, overhangs, and reversals without per-tile static, straight palette walls, or horizontal sublayer shelves.

Later structures own their exact cells. The final seam pass repairs only natural blendable tiles and omits bands that a valley, terrace, landmark, bridge, highland, or mine district has substantially occluded. The validator requires at least two surviving seams on small worlds and three on medium or large worlds. Each retained tile seam needs at least six observable depth crossings, a span of at least one fifth of its band or eighteen tiles, and three direction changes; its wall-material boundary may not remain exact-axis for more than twenty-six tiles.

## Guaranteed surface mine

The mine planner searches mountain, plateau, hill, and bounded random candidates, excluding spawn, Dungeon, protected structures, and progression tiles. Small, medium, and large districts span about 560, 700, and 840 tiles horizontally.

Eleven required rail edges connect a visible surface headframe to upper, middle, and deep Cavern workings. Additional spurs lead to flooded, collapsed, and sealed world-evil districts. The graph has at least three degree-three junctions, two independent cycles, and four horizontal lines; constrained worlds compress the distance between depth bands instead of creating an impossible rail grade. Workyard, Working, Mountain Rail, Flooded, Collapsed, and Sealed Evil districts use structural timber bents, masonry foundations, platform drop portals, ropes, torches, work benches, tables, chairs, anvils, debris, junction stations, and a workyard loft to read as an abandoned industrial place rather than an empty tunnel.

Track construction is graph-first: every edge receives a deterministic centerline and one of several macro-grade profiles before the union is prepared, placed, and framed. Long routes contain four to eight control segments that alternate level runs, climbs, and descents while preserving a maximum one-tile grade. Rolling, terraced, and dip-and-rise profiles create readable variation without short-period chatter. The collapsed spur uses a launch-transfer profile: a rising approach ends at a four-to-six-tile missing-track gap, with clear flight space and a masonry-supported landing one to three tiles lower.

The excavation envelope samples a smooth 29-cell height field with occasional broad swells, producing changing arch height and width while retaining six guaranteed clearance tiles. Section shells share correlated top, bottom, and thickness profiles. Every centerline sample also owns a smoothed local biome theme; a two-dimensional boundary offset makes theme changes fold through corridor walls and masonry instead of swapping on one cross-section. Chamber-scale accent fields use only the secondary wall from the same family and replace rectangular motifs with warped patches. Snow workings therefore use Ice and Snow unsafe walls, Desert workings use Sandstone and Hardened Sand walls, Jungle workings use Jungle walls, and evil, Mushroom, Cavern, Underworld, and ordinary districts follow their corresponding regional families. Timber supports are complete overhead bents with hanging posts outside the riding clearance and periodic foundations below the track. Supports consult the complete graph's headroom envelope, which lets branches cross at different elevations without blocking one another.

A final ownership pass runs after every summit, cave, terrace, seam, landmark, and quarantine repair. It restores biome walls, reopens jump flight space, replaces the full rail union, repairs six-tile headroom, and reapplies structural supports without overwriting actuated Gray Brick gates. The validator flood-fills from the exact authored surface entrance and treats a validated launch-to-landing pair as a traversal link. Every authored rail cell outside the jump gap must survive; every route must retain clearance; local-biome walls must occupy at least 95% of sampled rail-envelope cells with at most 2% missing walls; long routes need several grade changes without chatter; the graph must use at least three route profiles and contain several edges with both a climb and a descent; and the mine must retain complete timber bents. The sealed evil district uses a five-tile irregular Gray Brick shell, a wired actuated quarantine gate, and a varied top profile. Minimum entrance-connected sizes remain 300, 500, and 700 tiles by world size.

## Biome landmarks

The landmark planner classifies the final support tiles and searches deterministic candidates outside the mine, terraces, biome transitions, Forest lake crossings, mountain water bodies, prior landmarks, and protected content. Each accepted area is approximately 68–94 tiles wide. Its room graph combines three ground columns with upper components in one of five topologies: Broad Hall, Terraced, Twin Tower, Tower Wing, or Buried. The resulting six or seven rooms occupy two or three physical floors, and every disconnected upper component receives its own stair connection. Ocean landmarks have stilt-pier fallbacks; Forest, Snow, Desert, and Jungle have embedded fallbacks for difficult terrain.

Thirty archetypes—three for each biome family—change silhouette, material, room use, and terrain relationship instead of applying palette swaps to one house. Ranger lodges, chalets, courtyards, canopy lodges, quarantine keeps, harbor halls, observatories, mushroom caps, stone vaults, and ash forts select from gable, steep gable, parapet, canopy, spire, stilt, cloud arch, mushroom cap, stone vault, battlement, and igloo roofs. Snow's Buried Igloo places a domed surface room above two separate basement levels with preserved headroom. Biome-specific foundations, wall panels, framed trim, two-tile posts and ceilings, diagonal platform stairs, landings, supports, lighting, work furniture, seating, tables, bookcases, benches, and debris create readable room roles such as hearths, workshops, studies, lookouts, shrines, forges, and observatories.

Every structure samples the finished terrain, walls, liquids, and biome support across a padded footprint before modeling. Correlated profiles derive its foundation depth, approaches, supports, wall panels, and exterior blend; foundation ends use slopes and half blocks so the prepared shelf does not end as a straight cut. Forest landmarks use ordinary Wood Blocks, Wood walls, and Wooden Beams, reserving Living Wood for terrain-like details. Other palettes follow the target version's Snow, Desert, Jungle, Mushroom, Ocean, Sky, Cavern, evil, and Underworld materials. Multitile objects are placed through `WorldGen` and accepted only when their final tiles confirm placement. Surface landmarks author no doors. Rounded open side arches, unsafe natural walls, and incomplete room boundaries ensure the structures fail Terraria's real NPC-housing query.

A final landmark must retain its recorded room, floor, and stair counts; a connected room graph; biome-characteristic shell materials; at least two wall types; three furniture families; a two-layer solid foundation across most of its core width; correct roof and stair slopes; open approaches; no walls above the roof envelope; no door tiles; and a bounded platform count. A Buried Igloo must retain its below-ground rooms. Wall placement is limited to strict room interiors. Landmark shells are rebuilt after vanilla cleanup, then furnished. Embedded Cavern and Underworld fallbacks use the same final-floor ownership rule as surface candidates.

Furniture validation scans retained multitile footprints inside the final area. Placement return values do not count as proof. The structures remain loot-neutral.

## Safety and determinism

`WorldPlanner` consumes one value from `WorldGen.genRand`, then uses local seeded random streams for the plan and individual features. Candidate budgets are fixed. `StructureMap` reservations prevent later overlap, while late repair paths may ignore stale reservations only when their tile-level safety checks still protect chests, frame-important objects, wires, actuators, Shimmer, Dungeon and Temple blocks, and other progression content.

Regional cave repairs reserve their restored paths. Spawn always receives a 150-tile terrace, and larger worlds receive additional quiet-ground terraces. Invalid secret seeds stop before mutation.

## Saved data and validation

Manifest version 6 saves terraces; landmark archetype, room, floor, stair, layout, and furniture records; mountain grammar, decoration, and water measurements; Forest lake crossings and their replay seeds; valleys; bridges; highland style and attachment state; surviving biome transitions; mine sections; the mine graph summary; accents; and the final validation summary. Loading restores this manifest even though no generation plan exists at runtime. Saving a loaded world preserves the restored data.

The headless harness exercises generation, validation, `.wld`/`.twld` creation, first reload, save, and second reload. It uses a pseudo-terminal so tModLoader's `Console.ReadKey` error path cannot hide the first world-generation exception.

The final validator also checks regional relief, cave-route headroom and continuity, terrace flatness, eleven landmark categories, contextual accent density, Dungeon and Temple blocks, world-evil objects, and Shimmer cells. A failed contract throws during generation and the harness rejects the artifact.

## Compatibility boundary

Version 0.3.4 targets ordinary Terraria 1.4.4.9 worlds on tModLoader 2026.07.3.0. Other world-generation mods can compete for pass anchors or protected sites. Vanilla Worlds Overhauled detects those conflicts through named anchors, `StructureMap`, tile allow lists, bounded searches, and final scans; it does not promise compatibility with another total world replacement.
