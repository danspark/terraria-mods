# World-generation research record

This record separates observations, design conclusions, implementation targets, and external references. It began with the 0.3.0 majestic-world rewrite on 2026-08-31 and includes the 0.3.3 organic-boundary audit and 0.3.4 structure-and-watershed study.

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

Beautiful procedural regions need more than a silhouette. The reusable grammar for Vanilla Worlds Overhauled is:

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
- Shape long rail edges from a few macro-grade controls. Level runs, climbs, and descents should form readable motifs; independent per-cell grade changes create chatter, while a single endpoint interpolation creates a monotonous diagonal.
- Treat an intentional minecart jump as two rideable endpoints plus a validated transfer envelope. The approach must rise, the landing must sit slightly lower, the missing-track span must remain bounded, and the whole flight path must stay clear after late ownership passes.
- Give the mine one continuous local-biome background field. Structural timber belongs in bents, foundations, and work areas; it should not replace Snow, Jungle, Desert, evil, or Cavern walls with a generic checkerboard.
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

- The [tModLoader `StructureMap` reference](https://docs.tmodloader.net/docs/stable/class_structure_map.html) defines the required `CanPlace`/`AddProtectedStructure` ownership pattern. Vanilla Worlds Overhauled adds reservations after placing structures and keeps separate tile-level safety checks for late repairs.
- The [tModLoader `WorldGen` reference](https://docs.tmodloader.net/docs/stable/class_world_gen.html) is the authoritative API inventory for tile placement, liquids, clouds, framing, and world flags.
- The [tModLoader `TileObjectData` reference](https://docs.tmodloader.net/docs/stable/class_tile_object_data.html) explains multitile origins, anchors, styles, and why successful furniture placement must be verified against the final tiles.
- The [tModLoader world-generation guide](https://github-wiki-see.page/m/tModLoader/tModLoader/wiki/World-Generation) recommends named pass insertion, isolated pass testing, and complete real-world generation tests. The final harness follows that last requirement.
- The official Terraria Wiki describes [Floating Islands](https://terraria.wiki.gg/wiki/Floating_Island) as isolated Forest masses on Cloud/Rain Cloud with Sunplate structures. Vanilla Worlds Overhauled retains that palette but expands the form into a route-rich biome.
- The official [Underground Cabin](https://terraria.wiki.gg/wiki/Underground_Cabin) reference shows that vanilla cabins combine biome-specific blocks and walls with doors, platforms, chests, pots, lights, and multiple furniture families. Vanilla Worlds Overhauled uses the same layered visual vocabulary without copying a cabin layout.
- [Minecart Tracks](https://terraria.wiki.gg/wiki/Minecart_Track) are continuous rapid-traversal furniture tiles with slopes and junction behavior. This is why the mine contract is connectivity-based rather than a track-count target.
- The [housing rules](https://terraria.wiki.gg/wiki/Home) establish the frame, background-wall, entrance, light, flat-surface, and comfort roles. Vanilla Worlds Overhauled landmarks use those visual and functional categories without promising that every themed ruin is valid NPC housing.
- The [background-wall guide](https://terraria.wiki.gg/wiki/Wall) distinguishes safe and unsafe walls and their enemy-spawn/housing effects. Landmark wall choices are deliberate by biome and structure role.
- The [biome-spread guide](https://terraria.wiki.gg/wiki/Biome_spread) notes that a one-tile barrier is insufficient and recommends at least three tiles of air or non-corruptible material. Sealed evil mine and valley sections use thick Gray Brick shells.

## Inspiration and licensing boundary

- [Starlight River](https://github.com/ProjectStarlight/StarlightRiver) was reviewed for high-level lessons about staging large authored regions and protecting their identity. Its code is GPL-3.0. No code or assets were copied.
- [Calamity Mod Public](https://github.com/CalamityTeam/CalamityModPublic) was used only as a high-level example of multi-biome worldgen organization. Its [license](https://github.com/CalamityTeam/CalamityModPublic/blob/1.4.4/LICENSE.md) is proprietary/source-available. No code or assets were copied.
- Repositories without an explicit compatible license are idea-only references. Public visibility is not permission to copy.

Vanilla Worlds Overhauled uses its own algorithms and vanilla Terraria tile IDs. External sources and target-binary inspection informed design and API use, not copied implementation.

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

## 0.3.2 biome-owned mine verification record

The mine-wall and route-profile audit was tested on 2026-09-01 against Terraria 1.4.4.9 and tModLoader 2026.07.3.0. A reference screenshot showed a Snow mine built from alternating rectangular wall patches around one long diagonal rail. Tile-grid inspection made the cause measurable: wall selection occurred at too small a scale, and endpoint interpolation determined almost the entire route silhouette.

The implemented contract assigns every centerline sample a smoothed biome theme. A corridor uses that theme's primary unsafe wall continuously, while chamber accents may use only its paired wall from the same biome family. Structural timber is placed as readable overhead bents, hanging posts outside cart clearance, and track foundations. Route centerlines use rolling, terraced, dip-and-rise, and launch-transfer profiles. The transfer retains a four-to-six-tile missing-track gap after final repairs, with a rising launch, a lower landing, and clear flight space.

| Mode and size | Seed | Mine entrance component |
| --- | --- | ---: |
| Classic small | `1399794971` | 2,584 tiles |
| Journey medium | `204860939` | 3,073 tiles |
| Classic large | `Majesty-Matrix-Large-001` | 4,010 tiles |

Every row completed generation, strict final-tile validation, first reload, save, and second reload with manifest version 5 intact. The validator independently required local-biome walls across at least 95% of sampled rail-envelope cells, at most 2% missing walls, complete timber bents, multiple rail-profile families, several routes containing both a climb and a descent, bounded grade changes, and the intact launch transfer. The large seed also proved that route planning preserves the Jungle Temple exclusion envelope.

An independent renderer inspected the small seed's 2,584-tile surface-connected component. Its Snow crop contained 78,233 Ice-wall samples, with only small counts from legitimate biome-transition and feature-overlap areas, and showed the rail network alternating level work lines, climbs, descents, and the jump transfer. The rendered crop is a structural tile-state view rather than a lit in-game screenshot; it is intended to expose wall ownership, track continuity, support placement, and clearance.

## 0.3.3 organic-boundary verification record

The organic-boundary audit found that noisy planning alone does not prevent straight finished seams. Four late-stage patterns could still expose generator geometry: a shared world-surface cutoff under a mountain, the perimeter of a reserved transition band, a later owner repainting an earlier irregular boundary, and a repair pass that examined only cells inside its own rectangle. Independent per-tile randomness hid neither the rectangle nor the pass boundary; it only added speckle around it.

The durable rule is to give every natural material field a deterministic, correlated boundary function. Vanilla Worlds Overhauled now uses a shared domain-warped field with macro, detail, and grain scales, independent channels for blocks and walls, and stable seeds that late owners can reuse. A repair pass samples through its perimeter so it can compare the first owned cell with the neighboring finished world. Intentionally constructed traversal geometry remains readable: rails, decks, floors, doors, and reserved movement lanes may be straight, but their foundations, background fields, shells, and joins into terrain must not inherit those straight edges.

Finished-grid validation now measures the failure mode directly. Mountain natural blocks and walls, transition walls, and floating-highland layers reject exact horizontal or vertical material runs longer than 26 tiles. Landmark interior wall panels use an 18-tile limit, and mine-section wall fields use a 22-tile limit. A retained lowland transition must also expose at least six depth crossings, span at least one fifth of its band or 18 tiles, and reverse direction at least three times. These checks run after all final owners rather than trusting the input profiles.

The exact final-code matrix ran on 2026-09-01 against Terraria 1.4.4.9 and tModLoader 2026.07.3.0:

| Mode and size | Seed | Evil | Mountain families | Mine entrance component | Generation time |
| --- | --- | --- | --- | ---: | ---: |
| Classic small | `1399794971` | Crimson | Alpine / Split-Level Caves | 2,328 tiles | 20.5 s |
| Journey medium | `204860939` | Corruption | Sky-piercing / Open Fault; Alpine / Split-Level Caves | 3,073 tiles | 27.0 s |
| Classic large | `Majesty-Matrix-Large-001` (`1180213525`) | Crimson | Sky-piercing / Branching Grottoes; Highland / Split-Level Caves | 4,010 tiles | 60.2 s |

Every row completed generation, strict final-tile validation, first reload, save, and second reload with manifest version 5 intact. An independent tile-state renderer then inspected each full world and its complete surface-connected mine crop. The maps retained deliberate rail grades and work lines while showing irregular mountain skins, highland layers, wall fields, mine district ownership, and broad biome folds without visible reservation-box outlines.

## 0.3.4 structure-and-watershed verification record

The landmark study treated a building as a room graph before treating it as a shell. A three-column ground level establishes horizontal circulation; selected upper components create terraced, tower, and split silhouettes; and every disconnected upper component receives an explicit stair edge. This avoids the common failure where two towers look distinct outside but only one is reachable. Room roles, furniture, roof family, foundation behavior, and terrain relationship carry biome identity together. Changing only the block palette does not produce a biome-characteristic building.

Snow exposed a separate vertical rule. A descending igloo cannot share the surface floor with its first basement ceiling: doing so preserves the outline but collapses usable headroom. The buried grammar therefore owns one domed surface room and two separately spaced basement levels, then validates that the recorded below-ground rooms remain. All landmark families retain open approaches and incomplete housing boundaries; Terraria's real housing query remains the final authority on whether progression-breaking NPC occupancy is possible.

The watershed study applies the same ownership model to liquids. A level settled water surface is physically expected, but the bed, shoreline, retaining material, walls, supports, and approach terrain must come from correlated contours. Forest lake crossings and mountain ponds are reserved before the mine and landmarks, store their feature seeds, and replay the same shape during final repair. This prevents later clearance or refill work from replacing an organic basin with a rectangular scar. Mountain water also supplies a stable humidity context for denser vine curtains.

The final-code matrix ran on 2026-09-02 against Terraria 1.4.4.9 and tModLoader 2026.07.3.0:

| Mode and size | Seed | Landmarks | Forest lake bridges | Mountains / mountain waters | Mine entrance component |
| --- | --- | ---: | ---: | --- | ---: |
| Classic small | `1399794971` | 11 | 0 | 1 / 2 | 2,328 tiles |
| Journey medium | `204860939` | 11 | 1 | 2 / 5 | 3,073 tiles |
| Classic large | `882350129` | 11 | 1 | 2 / 4 | 3,777 tiles |

Every row completed strict finished-tile validation, first reload, save, and second reload with manifest version 6 intact. The small and medium seeds prove both sides of the optional Forest feature: omission remains valid and occurrence remains substantial. Across the matrix, every mountain retained at least two protected water bodies, and all eleven landmarks retained the recorded room, floor, stair, material, furniture, clearance, and anti-housing contracts.

An independent tile-state render then inspected all three world overviews, all 33 landmark crops, every mountain and mountain-water crop, and both generated Forest bridge lakes. The landmark sheets show broad halls, terraced compounds, separated towers, asymmetric wings, varied roof families, and the medium seed's three-level buried Snow igloo rather than one repeated cabin outline. Mountain crops show large background-open chambers, crossing routes, dense hanging curtains, bridges, and multiple visibly separate ponds. Both Forest scenes keep readable level decks while their beds, banks, supports, and approach terrain break up the reservation footprint; the small seed correctly contains no such scene.

## Natural-vine, cave-turn, and graveyard verification record

The mountain-vine audit distinguished Terraria's growing vegetation from `VineRope`, the player-placeable rope tile that merely uses a vine-like texture. Terraria assigns each natural vine family a living ceiling root: Vines grow from Grass; Jungle Vines from Jungle Grass and its evil variants; Corrupt and Crimson Vines from their corresponding Grass; Mushroom Vines from Mushroom Grass; and Ash Vines from Ash Grass. Mountain decoration now creates a short compatible living-root patch before extending the matching natural vine downward. It refuses protected, mine-owned, frame-important, and progression-critical roots. Validation counts only the six growing vine families, rejects any retained Vine Rope, and traces the top of every curtain back to a compatible living root.

The reference cave image also exposed a shape requirement that decoration density alone cannot satisfy. Vanilla-like caves change heading repeatedly, widen into chambers, and periodically leave short, stable floors where ambient objects can settle. Each authored mountain connection is therefore overlaid with deterministic nineteen-to-thirty-two-tile cave legs whose lateral direction alternates. Thick host-material shelves add level runs with clear headroom and sloped ends; validation requires at least eighteen substantial route turns and three usable wall-backed floor runs per mountain.

Terraria's Graveyard check counts Tombstone tile cells rather than placed objects. In tModLoader 2026.07.3.0, the biome threshold is 28 cells and nearby Sunflower cells subtract at half weight. A complete Tombstone occupies four cells, so selected stone crossings place nine complete, spaced objects for 36 cells. The final validator reproduces the game's biome-scan footprint at the center of the bridge, including Sunflower suppression, instead of validating only the structure rectangle. Timber, living-wood, suspension, and rail crossings never receive this treatment.

The final manifest-version-7 matrix ran on 2026-09-02 against Terraria 1.4.4.9 and tModLoader 2026.07.3.0:

| Mode and size | Seed | Mountains | Growing vine tiles | Graveyard bridges / tombstone cells | Generation time |
| --- | --- | ---: | ---: | --- | ---: |
| Classic small | `Majesty-Matrix-Small-001` (`1399794971`) | 1 | at least 260 | 0 / 0 | 22.3 s |
| Journey medium | `Majesty-Matrix-Medium-001` (`204860939`) | 2 | at least 260 each | 1 / 36 | 29.7 s |
| Classic large | `Majesty-Matrix-Large-001` (`1180213525`) | 2 | 417; 297 | 1 / 36 | 46.2 s |

All three rows passed finished-tile validation, first reload, save, and second reload. The small row proves that graveyards remain optional; medium and large exercise their successful occurrence. A persisted large-world tile render then showed the new turning cave chains, flat natural shelves, bright natural-vine curtains, and nine complete tombstones on the selected stone bridge. The renderer found no player-placeable Vine Rope in either mountain.

## Minecart endpoint and transfer research

The minecart traversal audit used the installed Terraria 1.4.4.9 and tModLoader 2026.07.3.0 assemblies as the primary source. `Terraria.Minecart.Initialize` defines four endpoint collision sentinels: `-1` is a regular stopping bumper, `-2` is a bouncy bumper, `-3` is a launch ramp, and `-4` is an open end. `TrackCollision` handles them differently. A bouncy bumper reverses horizontal velocity; a ramp detaches the cart with a 45-degree launch vector while retaining its horizontal speed; an open end releases the cart under gravity. `FrameTrack(..., pound: true)` cycles among the endpoint forms that fit the neighboring rail connection, matching a player hammering the rail.

This explains why a visually rising jump could still kill momentum: ordinary non-pound framing selected a regular bumper at the exposed launch tile. The durable implementation frames by behavior after the full rail union exists. The collapsed jump now requires frame 16–19 at its launch, corresponding to the four ramp orientations in this Terraria build, and a flat open frame 14 or 15 at its lower landing. Every graph vertex with degree one is hammered until `DrawBouncyBumper` confirms its terminal frame.

Automatic descent uses a different motif. Every sufficiently long route whose endpoint is at least 24 tiles deeper receives four flat upper rails, a two-or-three-column missing-track fall, and four flat lower rails three or four tiles below. Both exposed rails use flat open frames, so the upper edge releases a moving cart instead of braking it. The short drop and visible lower shelf allow reverse traversal by jumping the cart toward the upper rail. The sealed evil spur is deliberately exempt because a fall opening cannot also maintain the required actuated quarantine shell. The final ownership pass recuts the exact fall columns after supports and displays, preventing a late beam or foundation from occupying the transfer.

Finished-world validation checks the behavior-bearing tile state rather than trusting the planner. It requires the ramp, open-end, and bouncy frame families; bounded gaps and vertical offsets; flat run-in shelves; empty fall and flight columns; every planned downhill transfer; and surface connectivity across both transfer types. Geometric tile checks cannot fully replace riding the route, so a repeatable live-cart trajectory harness remains future work.

The 0.3.5 verification matrix ran on 2026-09-02 against Terraria 1.4.4.9 and tModLoader 2026.07.3.0:

| Mode and size | Seed | Mine entrance component | Gravity transfers | Generation time |
| --- | --- | ---: | ---: | ---: |
| Classic small | `Majesty-Matrix-Small-001` (`1399794971`) | 2,528 tiles | 6 | 22.8 s |
| Journey medium | `Majesty-Matrix-Medium-001` (`204860939`) | 3,091 tiles | 6 | 29.5 s |
| Classic large | `Majesty-Matrix-Large-001` (`1180213525`) | 3,608 tiles | 6 | 65.9 s |

All three rows passed finished-tile validation, first reload, save, and second reload with manifest version 7 intact. Each mine retained the six automatic downhill transfers, one ramp-framed launch, its open landing, at least four bouncy terminal turnarounds, all eleven required edges, and the sealed evil quarantine. An independent persisted-world scan of the large seed found the expected frame families and rendered the complete mine plus focused gravity-drop, launch-ramp, and bouncy-terminal crops.

## Torch God activation and temple research

The Torch God implementation was checked against the installed Terraria 1.4.4.9 and tModLoader 2026.07.3.0 assemblies. `Player.TryRecalculatingTorchLuck` scans from 40 tiles left to 40 tiles right and from 40 tiles above to 40 tiles below the stored player center: an 81-by-81 square accumulated one row at a time. A torch counts only when its tile belongs to `TileID.Sets.Torch` and its frame is lit (`frameX < 66`). `UpdateTorchLuck_ConsumeCountersAndCalculate` starts the unmodified event only below the world surface, outside its cooldown, before the player has unlocked biome torches, and when `nearbyTorches > 100`. One hundred is therefore the correct dormant count and 101 is the first activating count.

Once active, `Player.TorchAttack` searches a larger 201-by-201 square around the current player, selects lit torch tiles, changes their frames to the extinguished family, and tracks them for relighting. The authored arena keeps every one of its 100 starting torches within 34 tiles of the activation point, so the entire set lies inside both the initial scan and the attack search. The supplied chest item is an ordinary vanilla Torch; placing it in the wall-backed empty socket raises the local total from 100 to 101 without introducing a custom event or reward.

The structure is planned after the required authored landmarks, mine, mountains, waters, transitions, and routes have completed their final repairs and records. This ordering matters: an earlier prototype reserved its broad activation envelope too soon and could reduce the sites available to a later required landmark, while planning before the last liquid refill could invalidate a previously dry cave. At the final boundary, the building and access passage reject progression tiles, Dungeon and Jungle Temple cells, Shimmer, chests, and wiring. The wider activation square additionally rejects progression objects, wiring, chests, and housing walls, but permits ordinary cave pots and decoration that the temple never mutates. Only pre-existing torches in that square are removed before the exact themed array is placed.

The first reload audit exposed a separate liquid-timing hazard. Generation and the immediate world-load callback both saw 100 valid supported torches, but Terraria's subsequent liquid-settle phase allowed a nearby lava reservoir to enter the passage and remove five of them before the next save. Restricting torches to brick anchors did not address the cause. The planner now rejects liquid inside the future body and around the complete passage, including exterior masonry buffers, without disqualifying an unrelated sealed pool elsewhere in the activation square. The harness recounts live tiles on both reloads so persisted metadata cannot conceal another loss.

The 0.3.6 manifest-version-8 matrix ran on 2026-09-02:

| Mode and size | Seed | World evil | Temple | Lit torches | Generation time |
| --- | --- | --- | --- | ---: | ---: |
| Classic small | `Majesty-Matrix-Small-001` (`1399794971`) | Crimson | Desert Crucible Vault | 100 | 16.8 s |
| Journey medium | `Majesty-Matrix-Medium-001` (`204860939`) | Corruption | Desert Sunken Basilica | 100 | 43.0 s |
| Classic large | `Majesty-Matrix-Large-001` (`1180213525`) | Crimson | Desert Stepped Reliquary | 100 | 65.5 s |

Every row passed finished-tile validation, first reload, save, and second reload. The persisted manifest retained the temple's bounds, layout, theme, activation point, altar chest, empty socket, torch count, furniture count, entrance count, and brick count. The reload logger independently counted exactly 100 live lit tiles after both save cycles. Generation validation also found one ordinary Torch in the complete two-by-two chest, a dry empty socket, at least 420 themed brick cells, multi-tile side walls, at least 350 open unsafe-wall cells, no valid NPC housing, at least six furniture or prop placements, and a flood-filled route from every recorded cave portal to the altar.

Independent persisted-world renders inspected all three matrix temples at tile resolution. The compact Crucible Vault rises into a narrow dome, the Sunken Basilica uses a wider rounded shell and nested central arch, and the Stepped Reliquary shifts its ledges and roof mass asymmetrically. Their thick Sandstone-and-Mudstone masonry blends into the Underground Desert host while the darker unsafe-wall interior, altar chest, ledges, columns, stairs, layout-specific torch motifs, and open cave portals remain legible. The medium and large examples each retain two independent cavern approaches; the small example retains one. This is structural map inspection, not a substitute for playing the event; a repeatable live-player activation and combat test remains future work.

## Future research queue

- Move the independent deterministic full-world and feature-crop renderer into the repository test harness so shape review becomes as repeatable as connectivity validation.
- Prototype more bridge families, especially a natural stone arch and a partially collapsed rope bridge with a safe lower detour.
- Evaluate mine junction frame states in live play, including high-speed carts at diagonal crossings.
- Add a repeatable live-player Torch God activation and arena-combat test.
- Add housing-query integration tests if future landmarks promise valid NPC housing rather than decorated exploration structures.
- Test ordinary-seed compatibility against representative structure-heavy mods, one integration at a time.
