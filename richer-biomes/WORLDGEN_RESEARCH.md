# World-generation research record

This record separates observations, design conclusions, implementation targets, and external references. It was prepared for the 0.3.0 majestic-world rewrite on 2026-08-31.

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

- at least one ground-connected mountain with 32 Space-band columns;
- two visible foothill entrances and 24 cloud-belt tiles per mountain;
- one valley and one bridge per mountain;
- eleven furnished biome landmarks, including both oceans;
- all eleven required mine edges and every authored rail cell connected to the surface entrance;
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

Richer Biomes 0.3.0 uses its own algorithms and vanilla Terraria tile IDs. External sources informed design and API use, not copied implementation.

## 0.3.0 verification record

The final package was tested on 2026-08-31 with the repository matrix. Each row completed generation, strict tile validation, first reload, save, and second reload with manifest version 3 intact.

| Mode and size | Seed | Mountains / bridges / highlands | Landmarks | Cave routes | Mine entrance component |
| --- | --- | ---: | ---: | ---: | ---: |
| Classic small | `Majesty-Matrix-Small-001` | 1 / 1 / 1 | 11 | 4 | 1,907 tiles |
| Journey medium | `Majesty-Matrix-Medium-001` | 2 / 2 / 2 | 11 | 4 | 2,481 tiles |
| Classic large | `Majesty-Matrix-Large-001` | 2 / 2 / 2 | 11 | 6 | 3,149 tiles |

An independent tModLoader 2026.07.3.0 inspection loaded the final exact-seed `.wld` and scanned its tile grid. It found:

- textual seed `Majesty-Matrix-Large-001` and numeric seed `1180213525` embedded in the world file;
- both planned mountain regions ground-connected into Space, with grounded peaks at y=267 and y=263 and cloud-connected authored terrain reaching the upper sky;
- two biome-scale connected sky bodies measuring 672×200 and 413×197 tiles, with lakes, layered routes, and retained platform shafts;
- 20–28 retained furnishing tiles, three to five wall types, complete door footprints, and bounded platform openings in every landmark;
- the authored 3,149-tile mine as the largest rail component, spanning y=388–1222 with 427 surface, 910 Underground, and 1,812 Cavern track tiles, plus 1,954 nearby beam cells;
- an irregular sealed evil annex with a wired actuated gate, plus distinct flooded and collapsed districts;
- the expected 0.3.0 manifest and validation summary after the reload/save/reload cycle.

The final build artifact and the package installed in the normal tModLoader Mods directory were compared byte-for-byte after the last build. The release handoff records the resulting SHA-256 outside the packaged research file so the digest does not change itself.

## Future research queue

- Move the independent deterministic full-world and feature-crop renderer into the repository test harness so shape review becomes as repeatable as connectivity validation.
- Prototype more bridge families, especially a natural stone arch and a partially collapsed rope bridge with a safe lower detour.
- Evaluate mine junction frame states in live play, including high-speed carts at diagonal crossings.
- Add housing-query integration tests if future landmarks promise valid NPC housing rather than decorated exploration structures.
- Test ordinary-seed compatibility against representative structure-heavy mods, one integration at a time.
