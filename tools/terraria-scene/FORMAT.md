# Terraria scene format

This file defines format version 1 for `terraria_scene.py`.

## Top-level fields

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `format` | Integer | Yes | The value must be `1`. |
| `name` | String | No | The study name. The input filename is the default. |
| `seed` | Integer | No | The deterministic frame and object variation seed. The default is `0`. |
| `canvas` | Table | No | Output scale and background settings. |
| `palette` | Table | Yes | A map from one-character symbols to sprite names. |
| `sprites` | Table | No | Custom sprite definitions. |
| `map` | Table | Yes | The aligned world-cell grids. |

## Canvas fields

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `scale` | Integer from 1 through 8 | `2` | The nearest-neighbor output scale. |
| `boundary` | String | `world` | How terrain and walls meet the viewport. Use `world` for a crop from a continuous world or `open` for exposed edges such as a floating island. |
| `background` | String | `forest-day` | The built-in background preset. Version 1 has `forest-day` and `sky`. |
| `background_layers` | Array of strings | None | Texture names to draw instead of the preset layers. `Background_0` still supplies the sky. |

The renderer draws custom background layers in array order. Each texture repeats horizontally and aligns its opaque ground fill with the median terrain surface, leaving the scenery visible above the horizon.

With `boundary = "world"`, terrain and walls touching the left, right, or bottom of the map are framed as if matching cells continue beyond the image. The top remains open sky. With `boundary = "open"`, every map edge is exposed; use it for floating islands, cutaway chunks, and isolated structures.

## Built-in sprites

| Name | Kind | Terraria texture |
| --- | --- | --- |
| `dirt` | Tile | `Tiles_0` |
| `stone` | Tile | `Tiles_1` |
| `grass` | Tile | `Tiles_2` |
| `wood` | Tile | `Tiles_30` |
| `clay` | Tile | `Tiles_40` |
| `living-wood` | Tile | `Tiles_191` |
| `leaf` | Tile | `Tiles_192` |
| `dirt-wall` | Wall | `Wall_2` |
| `stone-wall` | Wall | `Wall_3` |
| `wood-wall` | Wall | `Wall_4` |
| `living-wood-wall` | Wall | `Wall_78` |
| `water` | Liquid | `Liquid_0` |
| `wood-platform` | Object | `Tiles_19` |
| `rope` | Object | `Tiles_213` |
| `torch` | Object | `Tiles_4` |
| `forest-tree` | Object | `Tiles_5`, `Tree_Branches_0`, and `Tree_Tops_0` |

Run `python3 terraria_scene.py list-sprites` to print the same names at the command line.

## Palette

Each palette key is one character. Each value names a built-in sprite or a sprite from the `sprites` table.

```toml
[palette]
G = "grass"
D = "dirt"
S = "stone"
"=" = "wood-platform"
```

A dot and a space always mean an empty cell. The palette cannot redefine them. A dot is safer because editors preserve it at the end of a row.

## Map layers

All present grids must have the same width and height. Use multiline literal strings so that TOML does not interpret a backslash in the shape grid.

| Layer | Accepted sprite kind | Meaning |
| --- | --- | --- |
| `terrain` | Tile | The foreground block. This layer is required. |
| `walls` | Wall | The background wall. |
| `liquids` | Liquid | The liquid that occupies the cell. |
| `objects` | Object | A platform, rope, torch, or object anchor. |
| `shapes` | No palette lookup | The foreground block shape. |

The shape characters have these meanings:

| Character | Shape |
| --- | --- |
| `.` or a space | Full block |
| `/` | A solid triangle that rises to the right |
| `\` | A solid triangle that rises to the left |
| `_` | The bottom half of the block |

Shapes affect occupied terrain cells only. Slope connections account for the solid sides of each shape. A `forest-tree` marker is the bottom trunk cell; place solid terrain directly below it and leave room on both sides for roots. A platform or rope marker draws one cell, so repeat the marker for a run. Platform ends automatically join adjacent solid terrain.

## Custom sprites

Use a custom sprite to reach another owned Terraria texture or an exported PNG with the same base name.

```toml
[sprites.snow]
kind = "tile"
asset = "Tiles_147"
frame_size = 16
stride = 18
autotile = "block"
connect = "solid"

[sprites.cabin]
kind = "object"
asset = "MyCabin"
frame_size = [48, 32]
stride = [48, 32]
autotile = "fixed"
frame = [0, 0]
offset = [-16, -16]
```

Custom sprite fields are:

| Field | Type | Meaning |
| --- | --- | --- |
| `kind` | String | `tile`, `wall`, `liquid`, or `object`. The default is `tile`. |
| `asset` | String | The texture filename without `.xnb` or `.png`. |
| `frame_size` | Integer or two-integer array | The source frame width and height in pixels. |
| `stride` | Integer or two-integer array | The horizontal and vertical distance between source frames. |
| `autotile` | String | `block`, `wall`, `fixed`, `platform`, `rope`, `torch`, or `liquid`. |
| `connect` | String | The group whose adjacent cells connect for `block` and `wall`. |
| `frame` | Two-integer array | The source frame column and row for `fixed`. |
| `brightness` | Number | A positive brightness multiplier. The default is `1.0`. |
| `offset` | Two-integer array | The destination x and y offset in native pixels. |

The `block` and `wall` modes use Terraria's separate foreground-tile and background-wall frame lookups. They select one of three deterministic visual variants. All adjacent walls mesh, including different wall materials, as they do in Terraria. The built-in dirt, stone, grass, wood, clay, and living wood sprites share the `solid` foreground connection group.

## Limits and errors

Version 1 accepts maps up to 240 columns by 135 rows. `validate` rejects a missing palette symbol, a sprite in the wrong layer, an unsupported shape, and grids with different dimensions. Error coordinates use one-based columns and rows.
