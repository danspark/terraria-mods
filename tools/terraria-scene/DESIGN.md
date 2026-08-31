# Renderer design

## Usage drives the model

The common terrain workflow stays short:

```text
palette tokens -> aligned map layers -> render
```

Anything that does not fit a 16-pixel map cell uses the same lower-level path:

```text
installed asset -> explicit source rectangle -> positioned entity -> render
```

Large scenes use a region or a set of independently rendered tiles. Authors do not need a different scene format.

## Ownership

`load_scene` owns TOML parsing and scene invariants. It resolves both character and token grids to the same `Grid` type. The renderer never parses text.

`AssetStore` owns installation discovery, XNB decoding, decoded-image caching, and the full XNB verification scan. `asset_catalog.py` owns recursive asset-name discovery and JSON catalog output. Neither component has a checked-in Terraria version table.

`Renderer` owns composition. Map layers supply Terraria-aware framing for common world cells. Entities supply exact rectangles and transforms for every other texture. `RenderRegion` changes only the output window; neighbor queries still read the full scene, so regional and tiled renders retain global framing.

## Draw flow

The renderer creates the sky and background first. It then interleaves entities with walls, liquids, terrain, and objects according to entity `z`. Every destination uses global scene coordinates and subtracts the region origin at composition time.

This rule makes a tile render a crop of the same global composition instead of a separate miniature scene. The test suite reassembles several tiles and compares the result pixel-for-pixel with a full render.

## Alternatives

A committed manifest for every Terraria sprite would be large and would become incomplete when the game changes. Runtime discovery is smaller and covers the user's installed version, including nested folders.

Deriving semantic frame rules for all texture sheets from Terraria internals would couple the tool to private game data structures. Explicit source rectangles are stable and exact. The small built-in set keeps semantic framing where it adds value: blocks, walls, liquids, platforms, ropes, torches, and forest trees.

A single full-canvas image cannot represent every practical world size because image formats and host memory are finite. Regional and tiled rendering remove that requirement while preserving one logical canvas and identical pixels at tile seams.

## Risks

An asset sheet does not describe its animation frame size. Authors must inspect the sheet or use known Terraria frame metadata before selecting a rectangle. This is deliberate. Guessing a frame layout would produce plausible but incorrect sprites.

The text map remains resident in memory for neighbor lookup. Terraria-sized maps fit comfortably as token tuples, while output pixels are bounded by regional rendering. A future disk-backed grid can replace `Grid` without changing the scene format or renderer coordinate model.
