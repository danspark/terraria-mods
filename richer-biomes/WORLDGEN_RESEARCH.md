# World-generation research record

This record separates observations, design conclusions, implementation targets, and external references. It began with the 0.3.0 majestic-world rewrite on 2026-08-31 and includes the 0.3.1 styling and block-state audit.

## Reference-world audit

Reference artifact: `/home/danielreis/.local/share/Terraria/tModLoader/Worlds/The_Degenerative_Slime.wld`.

The file was inspected read-only. Its hashes were checked before and after the audit and did not change.

The large Corruption world showed why counters alone were not enough:

- Only one mountain-like landmass was genuinely connected from ordinary ground into Space. Cloud components could make the earlier summit scan report false positives.
- Eleven recorded landmarks shared essentially the same small, hollow form and contained no meaningful furnishing. A recorded placement was not evidence of a finished building.
- Twenty-four isolated sky components existed, but they were small islands rather than a walkable biome with several route layers.
- The world contained 18,648 minecart-track tiles split across 26 components, yet no track component reached the surface. Total track count hid the absence of the requested surface mine.
- The old feature corridor and late mutations erased or isolated earlier work because feature ownership was not expressed as connected graphs with final-state checks.

These observations led to four audit rules:

1. Exclude clouds and Sunplate when measuring a ground-connected mountain.
2. Validate actual furniture, doors, walls, and room scale after cleanup.
3. Measure the largest connected highland component, not the sum of sky tiles.
4. Flood-fill rails from the exact surface entrance and check every authored edge.

## Exact-seed screenshot audit

The second audit used `Majesty-Matrix-Large-001` (`1180213525`) and eight in-game screenshots. It exposed failures that aggregate counters and the first validator still missed:

- ordinary Stone made the floating region read as a displaced cave, while long one-tile floors and repeating material bands made it look procedural rather than built or eroded;
- bridge endpoints were blocked, decks were a single platform line, and blank backgrounds gave the spans no structural logic;
- solid loft and shaft floors prevented downward traversal where bounded platform openings were required;
- landmarks reused a plain gabled box, a nearly uniform wall field, and arbitrary platform strips without enough architectural or furnishing rhythm;
- the mine had no obvious open entrance, a mostly linear descent, rounded wobbly rails, few meaningful junctions, and repeated empty corridors;
- the infected mine annex was a rectangular fill instead of an organic quarantined pocket.

The durable response is a set of shape-aware contracts. Structural floors are at least three tiles thick. Platforms mark only transitions. Bridge portals are wired and actuated. Material fields use correlated clusters rather than stripes. Mine rails are an explicit cyclic graph rasterized as flat and exact 45-degree segments. Organic chambers and containment shells vary their edge by column, and every late destructive pass is followed by a final ownership repair before validation.

## Variance and clearance audit

The 2026-09-01 follow-up generated three small worlds plus medium and large worlds, then rendered the large world at tile resolution with crops for every authored feature. It found and corrected interactions that a single exact seed did not expose:

- late mountain grounding columns could close regional cave routes, so the final route repair now removes only natural terrain and runs after the grounding repair;
- bridge portals and summit repairs could cross a mine entrance, so all mountain mutations preserve the rail clearance envelope;
- an upper rail branch could place its Living Wood support inside a lower branch's headroom, so supports now consult the complete planned graph before placement;
- terraces, valleys, landmarks, and mine districts could hide an early biome seam, so only boundaries still observable in the finished world remain in the manifest;
- measuring a transition at the nearest center crossing confused incidental material flecks with the authored boundary, so validation now samples the deterministic boundary at two-tile depth intervals;
- a highland touching every mountain made the relationship look mandatory, so attachment was first capped at one and planned only one third of the time. The later altitude audit reduced this to one fifth and excluded Highland ranges.

The visual result established several useful scale rules. Mountain cave quality needs both total wall-backed air and horizontal distribution; raw air count alone can hide one large void. Sky biomes need style-level changes to satellite count, lake use, and route layout, not only a different outline. Landmark clearance is best expressed as a measured open arch rather than a door-placement result. Rail clearance must be owned by the union of the graph because branches can cross at different elevations.

## Vanilla 1.4.4.9 styling and block-state audit

The 0.3.1 audit decompiled the installed Terraria 1.4.4.9 assembly shipped with tModLoader 2026.07.3.0. The inspected owners were `Terraria.GameContent.Biomes.CaveHouse.HouseBuilder`, `WorldGen.IslandHouse`, and `WorldGen.HellFort`. This was an implementation study of the exact target binary; no source was copied.

Vanilla cave houses establish a useful construction order: clear rooms, reserve the structure, place stairs and entrances, add platforms and beams, place priority objects, fill furniture, age the structure, add chests, then apply biome objects. The order matters because framing and later mutations can invalidate earlier visual state.

The reusable findings are:

- vanilla stair flights are diagonal individual platforms with alternating slope state and a four-platform landing, rather than horizontal platform strips;
- Snow, Desert, Jungle, and Mushroom cabins choose coordinated platform, table, chair, work-bench, and bookcase styles instead of changing only the shell material;
- a multitile object exists only when the placement API succeeds and its final tiles survive read-back; manually assigning frame coordinates does not establish a valid furniture object;
- solid masks are authored before slopes and framing, and slope state is verified after the final owner runs;
- background walls belong to room interiors and stop below the roof envelope;
- bridge clearance is a final passability and actuator-state contract across the whole entry corridor, not a count of actuated tiles near a portal;
- mountain walls read naturally when a continuous substrate is varied by coordinate-warped fields; cell grids and independent rectangular accents expose generator boundaries;
- a mountain needs one host-biome material owner per column. Sampling inside the artificial body merely repeats its temporary Dirt and Stone; stable samples must come from beneath the body, and final repainting must preserve authored transition bands.

These findings define current generation contracts in `MOD_DESIGN.md`; this section owns the underlying target-version research.

## Roof, altitude, and cavern-envelope audit

The 2026-09-01 screenshot follow-up compared four in-game crops with the generated tile state, then generated independent small, medium, and large worlds. It found six failures that earlier feature counts could not express:

- the left and right gable slopes used Terraria's solid-corner names as if they described visual direction, reversing both roof faces;
- the sloped roof line sat above a flat ceiling without a filled gable, exposing sky through a sawtooth gap;
- Forest structures used Living Wood, which reads as a tree structure rather than an ordinary wooden forest building;
- mine routes owned the same seven-tile excavation envelope at every rail cell, so an interconnected graph still looked like a uniform utility tube;
- every planned mountain used one Space-height formula and every late summit repair forced Snow and Ice, erasing altitude and host-biome identity;
- mountain chambers had wall-backed air and scattered decorations, but lacked deliberate open-background districts, suspended terrain, and clustered vine silhouettes.

The Terraria slope names describe the solid half of a tile. A roof that rises from left to center therefore uses `SlopeDownRight`; the mirrored face uses `SlopeDownLeft`. The durable roof contract is physical rather than numeric: correctly oriented slope state, a second structural course, a filled background gable below the roof, no wall cells above it, and enough headroom through the room plan.

Altitude is now a family choice rather than a universal target. Highland and Alpine plans are clamped below Space after all peak asymmetry is applied; Sky-piercing plans retain the dangerous Space route and are the only mountains that receive cloud belts. Adjacent ranges choose different altitude families. Floating-highland attachment is evaluated separately, occurs in one fifth of plans at most, and excludes Highland ranges.

Mine cavern variation uses a correlated 29-cell ceiling field. Interpolating neighboring macro samples creates long rises and falls; occasional sinusoidal swells create larger chambers without changing rail grade. The final rail owner restores only the six-tile minimum, so it cannot erase the extra air. Validation independently measures the finished upward clearance distribution instead of reusing the generation profile.

For mountain material, stable deep support is sampled below the temporary landform. A final natural-tile-only pass repaints the upper body and restores slope and half-block state. Feature bounds and transition bands retain ownership. Interior inclusions use the same palette, keep distance from planned routes, and provide material anchors for real Forest, Jungle, Corrupt, Crimson, Mushroom, or Ash vine curtains where those types fit.

## Design synthesis

Beautiful procedural regions need more than a silhouette. The reusable grammar for Richer Biomes is:

1. **Silhouette:** a memorable map-scale outline.
2. **Route graph:** an obvious main route plus choices, loops, and secondary exits.
3. **Districts:** spaces with distinct movement and narrative roles.
4. **Style:** a coherent local material, wall, liquid, and furniture palette.
5. **Age:** damage, flooding, overgrowth, isolation, or repair that tells a history.
6. **Proof:** finished-world measurements for all player-facing promises.

Contrast matters at world scale. Majestic regions need quiet ground before and after them. A mountain, mine, house, waterfall, and giant tree touching in one screen reads as noise, not richness.

Build the macro shape first, then add bounded jitter. Jitter should disturb a coherent ellipse, arch, slope, material cluster, or roof line; it should not replace that shape with independent noise. Movement surfaces remain readable even when nearby natural borders are irregular.

### Mountains

- Use twin or chained peaks rather than one symmetrical triangle.
- Put the saddle, valley, bridge, and interior hall in a single route graph so the player can understand how the exterior and interior relate.
- Keep the exposed Space summit dangerous. Harpies are part of the reward and risk. Provide a protected interior crossing so the dangerous route is optional.
- Add cloud belts where altitude changes the biome, especially where a highland physically meets a peak.
- Use bridge families with different structural logic: suspension cables and towers, compressed stone arches, or rail trestles with repeated supports.
- Give bridges thick decks, supported platform drop bays, wall or truss panels, clear headroom, and actuated portals where the deck enters solid mountain terrain.

### Floating highlands

- Preserve a main mass several screens wide. Satellites support its silhouette but never substitute for it.
- Provide a top route, interior gallery, underside route, and at least two vertical connections.
- Give the underside a continuous visual keel so cave carving cannot reduce the biome to unrelated blobs.
- Mix forest ground, Sunplate, Cloud, Rain Cloud, safe walkways, and a lake. Ordinary Stone does not belong in the authored sky body. Use large correlated material clusters rather than repeated depth stripes.
- Repair the continuous three-tile keel after every later pass that can carve the attached mountain or place structures in the sky.

### Houses and landmarks

- Start with a local building type, not a universal rectangle with a palette swap.
- Use a readable base, roof line, vertical rhythm, divider, loft or balcony, entrances, and a biome silhouette.
- Furniture should imply use: work area, seating, table surface, storage or books, lighting, and debris. Empty wall-backed rooms are unfinished.
- Use several wall fields, framed windows, roof trim, dormers or chimneys, supported porches, and a bounded loft opening. A platform strip is not a substitute for a floor plan.
- Exploration landmarks that resemble houses must deliberately fail the real NPC-housing query when NPC occupation would bypass progression. Unsafe walls, open side arches, and no doors are stronger evidence than assuming a ruin is invalid.
- Record bounds from the final floor. On sloped terrain, a first surface sample can sit below the actual building and make later decoration or validation operate outside its owner.

### Surface mine

- Make the entrance visible and immediately rideable. A deep rail network without surface access does not satisfy the fantasy.
- Model rails as one authored graph before terrain mutation. Preparing one edge after placing another can erase junction approaches.
- Use a large cyclic switchback graph for reliable traversal and shorter branches for story districts. Require several degree-three junctions, multiple independent loops, and horizontal work lines.
- Rasterize each rail edge as flat–45-degree–flat. Rounded interpolation produces a visibly wobbly rail even when all cells remain connected.
- Include Workyard, Working, Mountain Rail, Flooded, Collapsed, and Sealed Evil districts. The sealed evil branch needs at least a three-tile non-corruptible shell or air gap because infection can jump nearby thin barriers.
- Keep rewards ordinary and let geometry, traversal, and atmosphere carry the discovery.

## Numeric targets

| Feature | Small | Medium | Large |
| --- | ---: | ---: | ---: |
| Floating-highland main mass | 280×90 | 360×110 | 440×140 |
| Highland count | 1 | up to 2 | up to 2 |
| Minimum entrance-connected rail tiles | 300 | 500 | 700 |
| Approximate mine width | 560 | 700 | 840 |

All world sizes also require:

- every mountain to retain its planned Highland, Alpine, or Sky-piercing surface band; Space columns and cloud belts are required only for Sky-piercing plans;
- two visible foothill entrances, majority host-biome skin ownership, broad wall-backed caves, open-background pockets, suspended natural ledges, at least three vine curtains, wide cavities, pots, and climbing aids per mountain;
- one valley and one bridge per mountain;
- eleven furnished biome landmarks, including both oceans, with open side approaches, no authored doors, and failed NPC-housing queries;
- at least two surviving organic transition seams on small worlds and three on medium or large worlds;
- all eleven required mine edges and every authored rail cell connected to the surface entrance;
- six tiles of minimum mine headroom, at least ten percent of rail cells with nine-tile headroom, and at least three tiles of measured ceiling-height range;
- at least three degree-three mine junctions, two independent rail cycles, and four horizontal rail edges;
- three-tile bridge decks with platform drop bays, custom background panels, clear headroom, and at least sixteen actuated portal cells;
- at least two wall types, three furnishing families, framed windows, and a mostly two-layer foundation in every landmark;
- a highland component retaining at least 75% of target width and 66% of target depth.

## API and gameplay references

- The [tModLoader `StructureMap` reference](https://docs.tmodloader.net/docs/stable/class_structure_map.html) defines the required `CanPlace`/`AddProtectedStructure` ownership pattern. Richer Biomes adds reservations after placing structures and keeps separate tile-level safety checks for late repairs.
- The [tModLoader `WorldGen` reference](https://docs.tmodloader.net/docs/stable/class_world_gen.html) is the authoritative API inventory for tile placement, liquids, clouds, framing, and world flags.
- The [tModLoader `TileObjectData` reference](https://docs.tmodloader.net/docs/stable/class_tile_object_data.html) explains multitile origins, anchors, styles, and why successful furniture placement must be verified against the final tiles.
- The [tModLoader world-generation guide](https://github-wiki-see.page/m/tModLoader/tModLoader/wiki/World-Generation) recommends named pass insertion, isolated pass testing, and complete real-world generation tests. The final harness follows that last requirement.
- The official Terraria Wiki describes [Floating Islands](https://terraria.wiki.gg/wiki/Floating_Island) as isolated Forest masses on Cloud/Rain Cloud with Sunplate structures. Richer Biomes retains that palette but expands the form into a route-rich biome.
- The official [Underground Cabin](https://terraria.wiki.gg/wiki/Underground_Cabin) reference shows that vanilla cabins combine biome-specific blocks and walls with doors, platforms, chests, pots, lights, and multiple furniture families. Richer Biomes uses the same layered visual vocabulary without copying a cabin layout.
- [Minecart Tracks](https://terraria.wiki.gg/wiki/Minecart_Track) are continuous rapid-traversal furniture tiles with slopes and junction behavior. This is why the mine contract is connectivity-based rather than a track-count target.
- The [housing rules](https://terraria.wiki.gg/wiki/Home) establish the frame, background-wall, entrance, light, flat-surface, and comfort roles. Richer Biomes landmarks use those visual and functional categories without promising that every themed ruin is valid NPC housing.
- The [background-wall guide](https://terraria.wiki.gg/wiki/Wall) distinguishes safe and unsafe walls and their enemy-spawn/housing effects. Landmark wall choices are deliberate by biome and structure role.
- The [biome-spread guide](https://terraria.wiki.gg/wiki/Biome_spread) notes that a one-tile barrier is insufficient and recommends at least three tiles of air or non-corruptible material. Sealed evil mine and valley sections use thick Gray Brick shells.

## Inspiration and licensing boundary

- [Starlight River](https://github.com/ProjectStarlight/StarlightRiver) was reviewed for high-level lessons about staging large authored regions and protecting their identity. Its code is GPL-3.0. No code or assets were copied.
- [Calamity Mod Public](https://github.com/CalamityTeam/CalamityModPublic) was used only as a high-level example of multi-biome worldgen organization. Its [license](https://github.com/CalamityTeam/CalamityModPublic/blob/1.4.4/LICENSE.md) is proprietary/source-available. No code or assets were copied.
- Repositories without an explicit compatible license are idea-only references. Public visibility is not permission to copy.

Richer Biomes uses its own algorithms and vanilla Terraria tile IDs. External sources and target-binary inspection informed design and API use, not copied implementation.

## 0.3.0 verification record

The final package was tested on 2026-09-01 with tModLoader 2026.07.3.0. Each row completed generation, strict tile validation, first reload, save, and second reload with manifest version 4 intact.

| Mode and size | Seed | Mountains / bridges / highlands | Highland relation and styles | Visible seams | Cave routes | Mine entrance component |
| --- | --- | ---: | --- | ---: | ---: | ---: |
| Classic small | `Majesty-Matrix-Small-001` | 1 / 1 / 1 | detached Cloud Basin | 6 | 5 | 2,041 tiles |
| Journey medium | `Majesty-Matrix-Medium-001` | 2 / 2 / 2 | detached Terraced Meadow; detached Broken Archipelago | 7 | 6 | 2,542 tiles |
| Classic large | `Majesty-Matrix-Large-001` | 2 / 2 / 2 | attached Cloud Basin; detached Broken Archipelago | 8 | 9 | 2,834 tiles |

Separate small-seed audits covered both Corruption and Crimson and produced detached, attached Meadow, and attached Archipelago outcomes. Together with the final matrix, those runs demonstrate that a mountain connection is an occurrence rather than a requirement and that all three sky styles survive final validation.

An independent tModLoader inspection loaded the final large `.wld`, scanned its tile grid, and rendered a full map plus feature crops. It found:

- two Space-height, ground-connected mountain ranges using different interior grammars; the recorded wall-backed cave areas were 79,153 and 51,872 cells, with wide chambers across 646 of 704 and 509 of 559 columns;
- 188 and 152 retained pot tiles, 251 and 179 vine tiles, and 126 and 1,061 climbing-aid tiles in the two mountain ranges;
- one attached 527×208 Cloud Basin and one detached 517×213 Broken Archipelago, with 12,974 and 16,318 interior-route cells;
- two bridges with different structure over themed valleys;
- eleven retained surface transitions with irregular depth profiles after feature-occluded seams were omitted, including mountain-scale blends that remain organic through the full above-surface body;
- eleven landmarks measuring 61–79 tiles wide with three to five rooms, 34–43 retained furnishing cells, open approaches, no authored doors, and failed housing checks;
- a 2,834-tile authored mine component spanning a visible workyard, mountain rail, three working stations, flooded works, collapse, and sealed evil annex; all eleven required edges retained six tiles of headroom and connected to the surface entrance;
- the expected manifest and validation summary after the reload/save/reload cycle.

The final build artifact and the package installed in the normal tModLoader Mods directory were compared byte-for-byte after the last build. The release handoff records the resulting SHA-256 outside the packaged research file so the digest does not change itself.

## Pre-variance 0.3.1 styling verification record

This historical run records the styling package before the altitude-family, roof, host-material, and mine-envelope follow-up. It is useful for comparing the defects that prompted the variance audit, but its universal Space-height mountain behavior is no longer the current contract. The package was tested on 2026-09-01 with tModLoader 2026.07.3.0. Classic small, Journey medium, and Classic large worlds each completed strict generation validation, first reload, save, and second reload.

The final large-world inspection recorded:

- two Space-height mountains with 79,746 and 56,228 wall-backed cave-air cells and wide cavities across 650 and 514 columns;
- coherent snow-and-ice caps with no sand-family tiles in either summit envelope;
- 192 and 140 pot tiles, 327 and 197 vine or vine-rope tiles, and 129 and 1,072 climbing-aid tiles across the mountain ranges;
- two structural bridges whose complete planned endpoint corridors contained no solid blockers;
- one attached and one detached highland, preserving attachment as an occurrence rather than a requirement;
- eleven connected landmarks with three to five rooms, 18–44 retained furniture tiles, 13–26 platform tiles, sloped gable roofs, diagonal stairs, no doors, no exterior wall leakage, and failed housing checks;
- a 2,946-tile entrance-connected mine network whose rebuilt work displays retained real furniture objects after final rail ownership;
- organic mountain wall contours whose longest exact horizontal or vertical boundary run remained below the validator's 48-tile limit.

Independent tile-grid crops were reviewed for every mountain, bridge, and representative landmark after validation. The map renderer exposes structure and slope state rather than Terraria's lit in-game textures, making it useful for detecting clearance, wall ownership, repeated geometry, and invalid tile state.

## 0.3.1 variance verification record

The altitude, host-material, roof, and cavern-envelope follow-up was tested on 2026-09-01 with tModLoader 2026.07.3.0. Each world completed strict generation validation, first reload, save, and second reload with manifest version 4 intact.

| Mode and size | Seed | Evil | Regions | Mountains | Mine entrance component | Generation time |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| Classic small | `Majesty-Variance-Small-023` | Corruption | 7 | 1 | 2,046 tiles | 16.7 s |
| Journey medium | `Majesty-Variance-Medium-031` | Crimson | 11 | 2 | 2,617 tiles | 29.6 s |
| Classic large | `Majesty-Variance-Large-047` | Crimson | 13 | 2 | 2,918 tiles | 47.6 s |

These runs exercised both one- and two-range plans, all three world sizes, both world evils across the current audit set, one- and two-highland layouts, final host-material repair, liquid refill, and correlated mine-ceiling validation. Failed intermediate runs were retained long enough to diagnose the earliest generation exception; no failed world artifact was accepted.

The final uninterrupted release matrix used the repository's fixed seeds and the complete strengthened validator:

| Mode and size | Seed | Mountain altitude families | Mine entrance component |
| --- | --- | --- | ---: |
| Classic small | `Majesty-Matrix-Small-001` | Alpine | 1,969 tiles |
| Journey medium | `Majesty-Matrix-Medium-001` | Sky-piercing; Alpine | 2,442 tiles |
| Classic large | `Majesty-Matrix-Large-001` | Sky-piercing; Highland | 2,946 tiles |

All three rows generated, validated, reloaded, saved, and reloaded again. The installed package and canonical build artifact compared byte-for-byte after the final build; the release handoff reports the digest outside this packaged file.

## Future research queue

- Move the independent deterministic full-world and feature-crop renderer into the repository test harness so shape review becomes as repeatable as connectivity validation.
- Prototype more bridge families, especially a natural stone arch and a partially collapsed rope bridge with a safe lower detour.
- Evaluate mine junction frame states in live play, including high-speed carts at diagonal crossings.
- Add housing-query integration tests if future landmarks promise valid NPC housing rather than decorated exploration structures.
- Test ordinary-seed compatibility against representative structure-heavy mods, one integration at a time.
