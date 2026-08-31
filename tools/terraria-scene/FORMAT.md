# Terraria scene format

This file defines format version 1 for `terraria_scene.py`.

## Top-level fields

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `format` | Integer | Yes | Must be `1`. |
| `name` | String | No | Scene name. Defaults to the input filename. |
| `seed` | Integer | No | Deterministic frame and object variation seed. Defaults to `0`. |
| `canvas` | Table | Sometimes | Canvas, scale, boundary, and background settings. |
| `palette` | Table | No | Map tokens and their sprite definitions. |
| `sprites` | Table | No | Reusable sprite definitions. |
| `map` | Table | No | Aligned tile, wall, liquid, object, and shape grids. |
| `entities` | Array of tables | No | Pixel-positioned frames from any installed texture. |

Define at least one map layer, or set `canvas.size`. A scene can use maps, entities, or both.

## Canvas fields

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `size` | Two positive integers | Map size | Canvas width and height in Terraria cells. Required when the scene has no map. |
| `scale` | Positive integer | `2` | Nearest-neighbor output scale. It has no fixed maximum. |
| `boundary` | String | `world` | Use `world` for a continuous crop or `open` for exposed edges. |
| `sky` | String | `Background_0` | Base texture. Use an installed asset name or `transparent`. |
| `background` | String | `forest-day` | Preset background layers. Values are `forest-day`, `sky`, and `transparent`. |
| `background_layers` | Array of strings | Preset layers | Installed texture names to draw instead of the preset. |
| `horizon` | Number | Median terrain surface | Background ground line in Terraria cells. |

Custom background layers repeat horizontally. The renderer aligns each texture's opaque ground fill with the horizon.

With `boundary = "world"`, terrain and walls continue through the left, right, and bottom canvas edges. The top remains open. With `boundary = "open"`, all four edges are exposed.

## Use every installed texture

An asset name is its path below `Content/Images`, without `.xnb` or `.png`. Both root and nested assets work:

```text
NPC_1
Tiles_147
Accessories/Acc_HandsOn_1
UI/Bestiary/Icon_Tags_Shadow
```

List matching names:

```bash
python3 terraria_scene.py list-assets 'NPC_*'
python3 terraria_scene.py list-assets 'Accessories/*' --json accessories.json
```

Decode a sheet or one source rectangle before placing it:

```bash
python3 terraria_scene.py inspect-asset NPC_1 --output npc-1.png --scale 4
python3 terraria_scene.py inspect-asset NPC_1 \
  --source 0 0 32 26 --output slime-frame.png --scale 4
```

`verify-assets` decodes every XNB in one process and writes every texture dimension. It is the completeness check for the installed Terraria version:

```bash
python3 terraria_scene.py verify-assets --output terraria-assets.json
```

## Freeform entities

Use an entity for an NPC, item, projectile, gore, tree top, UI texture, custom bitmap, or exact rectangle from any sheet.

```toml
[[entities]]
name = "blue slime"
asset = "NPC_1"
at = [12, 18]
units = "tiles"
source = [0, 0, 32, 26]
anchor = "bottom-center"
scale = 1.5
rotation = 0
flip_x = true
opacity = 1
brightness = 1
tint = [255, 255, 255, 255]
z = 200
```

Entity fields are:

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `name` | String | Asset name | Label used in errors. |
| `asset` | String | Required | Installed or exported texture name. |
| `at` | Two numbers | Required | Anchor position. |
| `units` | String | `tiles` | Position units: `tiles` or `pixels`. |
| `source` | Four integers | Whole texture | Source x, y, width, and height in pixels. |
| `frame_size` | Integer or pair | None | Frame size when using frame coordinates instead of `source`. |
| `stride` | Integer or pair | `frame_size` | Distance between frames. |
| `frame` | Two integers | `[0, 0]` | Frame column and row. |
| `anchor` | String | `top-left` | Horizontal and vertical anchor, such as `center` or `bottom-center`. |
| `scale` | Positive number or pair | `1` | Horizontal and vertical scale. |
| `rotation` | Number | `0` | Counterclockwise rotation in degrees. |
| `flip_x`, `flip_y` | Boolean | `false` | Mirror the frame. |
| `opacity` | Number from 0 through 1 | `1` | Alpha multiplier. |
| `brightness` | Positive number | `1` | Brightness multiplier. |
| `tint` | Four integers from 0 through 255 | White | RGBA color multiplier. |
| `z` | Integer | `200` | Draw order relative to map layers. |

Draw order uses these boundaries:

| Entity `z` | Position |
| --- | --- |
| Less than `-300` | Behind walls |
| `-300` through `-201` | Between walls and liquids |
| `-200` through `-1` | Between liquids and terrain |
| `0` through `99` | Between terrain and objects |
| `100` or greater | In front of map objects |

Entity transforms use nearest-neighbor sampling to preserve Terraria pixels.

## Palette

A palette value can name a built-in or reusable sprite:

```toml
[palette]
G = "grass"
D = "dirt"
"=" = "wood-platform"
```

A palette value can also define any installed tile sheet inline:

```toml
[palette.crystal]
kind = "tile"
asset = "Tiles_164"
frame_size = 16
stride = 18
autotile = "fixed"
frame = [0, 0]
```

Use `autotile = "block"` only when the texture follows Terraria's standard block atlas. Use `fixed` with an explicit frame for frame-important tiles and multi-tile objects.

## Character and token maps

Character maps are compact. Each palette key is one Unicode character:

```toml
[map]
terrain = '''
...GG...
GGGDDGGG
DDDDDDDD
'''
```

A dot or a space means empty.

Token maps use named, whitespace-separated cells. They remove the one-character palette restriction:

```toml
[map]
encoding = "tokens"
terrain = '''
.     .     grass grass .
stone stone dirt  dirt  stone
'''
```

All present layers must have the same number of rows and cells. The available layers are:

| Layer | Accepted sprite kind | Meaning |
| --- | --- | --- |
| `terrain` | `tile` | Foreground blocks. |
| `walls` | `wall` | Background walls. |
| `liquids` | `liquid` | Liquid cells. |
| `objects` | `object` | Platforms, ropes, torches, trees, or fixed frames. |
| `shapes` | No palette lookup | Foreground block shape. |

A cell cannot contain both `terrain` and `objects`. Terraria stores one foreground tile per cell. Use `entities` for artwork that overlays foreground terrain.

The shape tokens are `.`, `/`, `\`, and `_`. A dot is a full block. The slash tokens are Terraria slopes. An underscore is a bottom half block.

Store a large layer in a separate UTF-8 file:

```toml
[map]
encoding = "tokens"
terrain = { file = "terrain.map", encoding = "tokens" }
walls = { file = "walls.map", encoding = "tokens" }
```

Paths are relative to the scene file unless they are absolute.

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

Run `python3 terraria_scene.py list-sprites` to print the built-ins.

## Reusable sprite fields

Definitions below `[sprites.NAME]` and inline palette definitions use the same fields:

| Field | Type | Meaning |
| --- | --- | --- |
| `kind` | String | `tile`, `wall`, `liquid`, or `object`. Defaults to `tile`. |
| `asset` | String | Texture name. |
| `frame_size` | Positive integer or pair | Source frame size. |
| `stride` | Positive integer or pair | Distance between frames. |
| `autotile` | String | `block`, `wall`, `fixed`, `platform`, `rope`, `torch`, or `liquid`. |
| `connect` | String | Foreground connection group. |
| `frame` | Two integers | Fixed frame column and row. |
| `brightness` | Positive number | Brightness multiplier. |
| `offset` | Two integers | Destination offset in native pixels. |

Walls use Terraria's wall lookup and mesh across wall materials. Standard block sprites use the foreground block lookup. Platforms choose frames from their neighboring platforms and solid terrain.

## Large scenes

The format has no fixed width, height, palette, entity-count, or scale maximum. A single PNG still needs enough memory and must fit the PNG implementation on the host.

Render one region by tile coordinates:

```bash
python3 terraria_scene.py render world.toml \
  --region 4000 1200 160 90 --output region.png
```

Render the entire logical canvas without holding one full-world image in memory:

```bash
python3 terraria_scene.py render-tiles world.toml \
  --tile-size 160 90 --output world-tiles
```

`render-tiles` writes independently usable PNG files and `manifest.json`. Neighbor framing, backgrounds, trees, glows, and entities remain continuous across tile boundaries.

`validate` rejects malformed values, undefined map tokens, wrong sprite kinds, overlapping terrain and objects, invalid source rectangles, and grids with different dimensions. Error coordinates use one-based columns and rows.
