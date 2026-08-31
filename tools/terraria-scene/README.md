# Terraria scene renderer

`terraria_scene.py` turns TOML text maps and exact Terraria sprite frames into PNG scene studies. It reads textures from your installed copy of Terraria. The repository contains no game art.

The format supports compact character maps, named token maps, external map files, freeform sprite placement, arbitrary canvas dimensions, regional renders, and tiled output. Asset names resolve at runtime, so a new Terraria texture works without a tool update.

## Set up the tool

Install Python 3.11 or newer and the .NET 8 SDK. The SDK decompresses Terraria's XNB textures on first use.

From the repository root, run:

```bash
cd tools/terraria-scene
python3 -m venv .venv
source .venv/bin/activate
python -m pip install -r requirements.txt
```

The renderer finds the standard Linux Steam installation. For another installation, pass `--assets /path/to/Terraria` or set `TERRARIA_PATH`.

You can also pass an exported texture directory. The directory can contain PNG or XNB files and nested folders.

## Render the examples

Validate and render the biome study:

```bash
python3 terraria_scene.py validate examples/vertical-forest.toml
python3 terraria_scene.py render examples/vertical-forest.toml \
  --output examples/vertical-forest.png
```

Render a scene made only from exact asset frames:

```bash
python3 terraria_scene.py render examples/freeform-assets.toml \
  --output examples/freeform-assets.png
```

The first example uses terrain, walls, liquids, platforms, ropes, torches, slopes, and composed trees. The second uses an NPC frame, an item, a projectile, and a tile-sheet frame without a map.

## Find and inspect sprites

List every NPC texture in the installed game:

```bash
python3 terraria_scene.py list-assets 'NPC_*'
```

Write the complete recursive asset catalog:

```bash
python3 terraria_scene.py list-assets '*' --json terraria-assets.json
```

Inspect a sheet, then export the frame you want:

```bash
python3 terraria_scene.py inspect-asset NPC_1 \
  --output npc-1.png --scale 4

python3 terraria_scene.py inspect-asset NPC_1 \
  --source 0 0 32 26 --output slime.png --scale 4
```

Check that the tool can decode every installed texture and record its dimensions:

```bash
python3 terraria_scene.py verify-assets \
  --output verified-terraria-assets.json
```

On the Terraria copy used to develop this tool, the command verifies 15,123 textures, including nested asset folders. The result comes from the installed copy, not a checked-in list.

## Create a scene

For a terrain study:

1. Copy `examples/vertical-forest.toml`.
2. Set `boundary = "world"` for a continuous crop or `boundary = "open"` for a floating island.
3. Add built-ins or inline asset definitions to the palette.
4. Edit the aligned map layers.
5. Run `validate` after each edit.
6. Run `render` when the scene passes validation.

For long names or a large palette, set `map.encoding = "tokens"`. Each cell then uses a whitespace-separated name such as `jungle_grass` or `living_wood_wall`. Put a large grid in a separate file with `{ file = "terrain.map" }`.

For an NPC, item, projectile, decoration, UI texture, or an unusual multi-tile object, add an `[[entities]]` table. Give it an asset name and either a source rectangle or frame coordinates. Position it in tiles or pixels. Rotation, flips, scaling, tint, brightness, opacity, anchors, and draw order are explicit.

Read [FORMAT.md](FORMAT.md) for the complete reference.

## Render a large world

There is no fixed scene-size or scale cap. A full PNG still consumes memory proportional to its output dimensions.

Render one part of a scene:

```bash
python3 terraria_scene.py render world.toml \
  --region 4000 1200 160 90 --output region.png
```

For a world-scale study, render bounded PNG tiles:

```bash
python3 terraria_scene.py render-tiles world.toml \
  --tile-size 160 90 --output world-tiles
```

The output directory contains `manifest.json` and one PNG for each region. Reassembling those PNGs produces the same pixels as a full render.

## Run the checks

```bash
python3 -m unittest discover -s tests -v
```

The suite covers both map encodings, external maps, arbitrary canvas sizes, direct asset entities, tile stitching, Terraria framing, tree roots, recursive asset discovery, XNB decoding, and the complete installed texture scan when Terraria is available.

## Keep game art local

The tool caches decoded textures under `~/.cache/terraria-scene`. Generated PNGs and catalogs are local outputs. Do not commit or redistribute decoded sheets or renders that contain Terraria art.
