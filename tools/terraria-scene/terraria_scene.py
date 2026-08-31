#!/usr/bin/env python3
"""Render agent-authored text maps with textures from an owned Terraria install."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import textwrap
import tomllib
from dataclasses import dataclass, replace
from pathlib import Path
from statistics import median
from typing import Any

from PIL import Image, ImageChops, ImageDraw, ImageEnhance

from asset_catalog import AssetRecord, discover_assets, write_catalog


TILE_SIZE = 16
FORMAT_VERSION = 1
TOOL_DIR = Path(__file__).resolve().parent


class SceneError(Exception):
    """An error that an author can fix in the scene or local setup."""


@dataclass(frozen=True)
class Sprite:
    name: str
    kind: str
    asset: str | None = None
    frame_size: tuple[int, int] = (16, 16)
    stride: tuple[int, int] = (18, 18)
    frame: tuple[int, int] = (0, 0)
    autotile: str = "fixed"
    connect: str | None = None
    brightness: float = 1.0
    offset: tuple[int, int] = (0, 0)


@dataclass(frozen=True)
class Entity:
    name: str
    asset: str
    position: tuple[int, int]
    source: tuple[int, int, int, int] | None = None
    anchor: str = "top-left"
    scale: tuple[float, float] = (1.0, 1.0)
    rotation: float = 0.0
    flip_x: bool = False
    flip_y: bool = False
    opacity: float = 1.0
    brightness: float = 1.0
    tint: tuple[int, int, int, int] = (255, 255, 255, 255)
    z: int = 200


GridRow = str | tuple[str, ...]
Grid = tuple[GridRow, ...]


@dataclass(frozen=True)
class RenderRegion:
    x: int
    y: int
    width: int
    height: int


@dataclass(frozen=True)
class Scene:
    path: Path
    name: str
    seed: int
    scale: int
    boundary: str
    sky: str
    background: str
    background_layers: tuple[str, ...] | None
    palette: dict[str, Sprite]
    layers: dict[str, Grid]
    entities: tuple[Entity, ...]
    horizon: float | None
    width: int
    height: int


BUILTINS: dict[str, Sprite] = {
    "dirt": Sprite("dirt", "tile", "Tiles_0", autotile="block", connect="solid"),
    "stone": Sprite("stone", "tile", "Tiles_1", autotile="block", connect="solid"),
    "grass": Sprite("grass", "tile", "Tiles_2", autotile="block", connect="solid"),
    "wood": Sprite("wood", "tile", "Tiles_30", autotile="block", connect="solid"),
    "clay": Sprite("clay", "tile", "Tiles_40", autotile="block", connect="solid"),
    "living-wood": Sprite(
        "living-wood", "tile", "Tiles_191", autotile="block", connect="solid"
    ),
    "leaf": Sprite("leaf", "tile", "Tiles_192", autotile="block", connect="leaf"),
    "dirt-wall": Sprite(
        "dirt-wall",
        "wall",
        "Wall_2",
        frame_size=(32, 32),
        stride=(36, 36),
        autotile="wall",
        connect="wall",
        brightness=0.56,
        offset=(-8, -8),
    ),
    "stone-wall": Sprite(
        "stone-wall",
        "wall",
        "Wall_3",
        frame_size=(32, 32),
        stride=(36, 36),
        autotile="wall",
        connect="wall",
        brightness=0.52,
        offset=(-8, -8),
    ),
    "wood-wall": Sprite(
        "wood-wall",
        "wall",
        "Wall_4",
        frame_size=(32, 32),
        stride=(36, 36),
        autotile="wall",
        connect="wall",
        brightness=0.58,
        offset=(-8, -8),
    ),
    "living-wood-wall": Sprite(
        "living-wood-wall",
        "wall",
        "Wall_78",
        frame_size=(32, 32),
        stride=(36, 36),
        autotile="wall",
        connect="wall",
        brightness=0.54,
        offset=(-8, -8),
    ),
    "water": Sprite(
        "water",
        "liquid",
        "Liquid_0",
        frame_size=(16, 16),
        stride=(16, 16),
        autotile="liquid",
    ),
    "wood-platform": Sprite(
        "wood-platform", "object", "Tiles_19", autotile="platform"
    ),
    "rope": Sprite("rope", "object", "Tiles_213", autotile="rope"),
    "torch": Sprite(
        "torch",
        "object",
        "Tiles_4",
        frame_size=(20, 20),
        stride=(22, 22),
        autotile="torch",
        offset=(-2, -2),
    ),
    "forest-tree": Sprite("forest-tree", "object", autotile="forest-tree"),
}

BACKGROUND_PRESETS: dict[str, tuple[str, ...]] = {
    "transparent": (),
    "sky": (),
    "forest-day": (
        "Background_7",
        "Background_8",
        "Background_9",
        "Background_10",
        "Background_11",
    ),
}

LAYER_KINDS: dict[str, set[str]] = {
    "terrain": {"tile"},
    "walls": {"wall"},
    "liquids": {"liquid"},
    "objects": {"object"},
}

SHAPE_SIDES: dict[str, frozenset[str]] = {
    ".": frozenset({"up", "down", "left", "right"}),
    " ": frozenset({"up", "down", "left", "right"}),
    "_": frozenset({"down", "left", "right"}),
    "/": frozenset({"down", "right"}),
    "\\": frozenset({"down", "left"}),
}

# Terraria.Framing.WallFrame uses a different atlas layout from foreground tiles.
# Each index is the up/left/right/down occupancy bitmask, followed by three variants.
WALL_FRAME_LOOKUP: tuple[tuple[tuple[int, int], ...], ...] = (
    ((9, 3), (10, 3), (11, 3)),
    ((6, 3), (7, 3), (8, 3)),
    ((12, 0), (12, 1), (12, 2)),
    ((1, 4), (3, 4), (5, 4)),
    ((9, 0), (9, 1), (9, 2)),
    ((0, 4), (2, 4), (4, 4)),
    ((6, 4), (7, 4), (8, 4)),
    ((1, 2), (2, 2), (3, 2)),
    ((6, 0), (7, 0), (8, 0)),
    ((5, 0), (5, 1), (5, 2)),
    ((1, 3), (3, 3), (5, 3)),
    ((4, 0), (4, 1), (4, 2)),
    ((0, 3), (2, 3), (4, 3)),
    ((0, 0), (0, 1), (0, 2)),
    ((1, 0), (2, 0), (3, 0)),
    ((1, 1), (2, 1), (3, 1)),
    ((6, 1), (7, 1), (8, 1)),
    ((6, 2), (7, 2), (8, 2)),
    ((10, 0), (10, 1), (10, 2)),
    ((11, 0), (11, 1), (11, 2)),
)

CENTER_WALL_FRAME_LOOKUP: tuple[tuple[int, ...], ...] = (
    (2, 0, 0),
    (0, 1, 4),
    (0, 3, 0),
)

TREE_TRUNK_SPRITE = Sprite(
    "forest-tree-trunk",
    "object",
    "Tiles_5",
    frame_size=(20, 20),
    stride=(22, 22),
    offset=(-2, -2),
)


def _pair(value: Any, field: str, *, default: tuple[int, int]) -> tuple[int, int]:
    if value is None:
        return default
    if isinstance(value, int) and not isinstance(value, bool):
        pair = (value, value)
    elif isinstance(value, list) and len(value) == 2 and all(
        isinstance(item, int) and not isinstance(item, bool) for item in value
    ):
        pair = (value[0], value[1])
    else:
        raise SceneError(f"{field} must be an integer or an array of two integers")
    if pair[0] <= 0 or pair[1] <= 0:
        raise SceneError(f"{field} values must be positive")
    return pair


def _offset_pair(value: Any, field: str, *, default: tuple[int, int]) -> tuple[int, int]:
    if value is None:
        return default
    if not isinstance(value, list) or len(value) != 2 or not all(
        isinstance(item, int) and not isinstance(item, bool) for item in value
    ):
        raise SceneError(f"{field} must be an array of two integers")
    return value[0], value[1]


def _sprite_from_table(name: str, raw: Any, field: str) -> Sprite:
    if not isinstance(raw, dict):
        raise SceneError(f"{field} must be a TOML table")
    kind = raw.get("kind", "tile")
    if kind not in {"tile", "wall", "liquid", "object"}:
        raise SceneError(f"{field}.kind has unsupported value {kind!r}")
    asset = raw.get("asset")
    if not isinstance(asset, str) or not asset:
        raise SceneError(f"{field}.asset must name an XNB or PNG texture")

    default_size = (32, 32) if kind == "wall" else (16, 16)
    default_stride = (36, 36) if kind == "wall" else (18, 18)
    default_autotile = "wall" if kind == "wall" else "block" if kind == "tile" else "fixed"
    autotile = raw.get("autotile", default_autotile)
    if autotile not in {"block", "wall", "fixed", "platform", "rope", "torch", "liquid"}:
        raise SceneError(f"{field}.autotile has unsupported value {autotile!r}")

    connect = raw.get("connect")
    if connect is not None and not isinstance(connect, str):
        raise SceneError(f"{field}.connect must be a string")
    if connect is None and autotile in {"block", "wall"}:
        connect = kind

    brightness = raw.get("brightness", 1.0)
    if (
        not isinstance(brightness, (int, float))
        or isinstance(brightness, bool)
        or not math.isfinite(float(brightness))
        or brightness <= 0
    ):
        raise SceneError(f"{field}.brightness must be positive")

    return Sprite(
        name=name,
        kind=kind,
        asset=asset,
        frame_size=_pair(raw.get("frame_size"), f"{field}.frame_size", default=default_size),
        stride=_pair(raw.get("stride"), f"{field}.stride", default=default_stride),
        frame=_offset_pair(raw.get("frame"), f"{field}.frame", default=(0, 0)),
        autotile=autotile,
        connect=connect,
        brightness=float(brightness),
        offset=_offset_pair(raw.get("offset"), f"{field}.offset", default=(0, 0)),
    )


def _custom_sprites(data: Any) -> dict[str, Sprite]:
    if data is None:
        return {}
    if not isinstance(data, dict):
        raise SceneError("sprites must be a TOML table")
    return {
        name: _sprite_from_table(name, raw, f"sprites.{name}")
        for name, raw in data.items()
    }


def _number(value: Any, field: str) -> float:
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        raise SceneError(f"{field} must be a number")
    number = float(value)
    if not math.isfinite(number):
        raise SceneError(f"{field} must be finite")
    return number


def _boolean(value: Any, field: str) -> bool:
    if not isinstance(value, bool):
        raise SceneError(f"{field} must be true or false")
    return value


def _number_pair(value: Any, field: str, *, default: tuple[float, float]) -> tuple[float, float]:
    if value is None:
        return default
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        pair = float(value), float(value)
    elif isinstance(value, list) and len(value) == 2:
        pair = _number(value[0], field), _number(value[1], field)
    else:
        raise SceneError(f"{field} must be a number or an array of two numbers")
    if pair[0] <= 0 or pair[1] <= 0:
        raise SceneError(f"{field} values must be positive")
    return pair


def _quad(value: Any, field: str) -> tuple[int, int, int, int]:
    if not isinstance(value, list) or len(value) != 4 or not all(
        isinstance(item, int) and not isinstance(item, bool) for item in value
    ):
        raise SceneError(f"{field} must be an array of four integers")
    return value[0], value[1], value[2], value[3]


def _entities(data: Any) -> tuple[Entity, ...]:
    if data is None:
        return ()
    if not isinstance(data, list):
        raise SceneError("entities must be an array of TOML tables")
    result: list[Entity] = []
    anchors = {
        "top-left",
        "top-center",
        "top-right",
        "center-left",
        "center",
        "center-right",
        "bottom-left",
        "bottom-center",
        "bottom-right",
    }
    allowed_fields = {
        "name",
        "asset",
        "at",
        "units",
        "source",
        "frame_size",
        "stride",
        "frame",
        "anchor",
        "scale",
        "rotation",
        "flip_x",
        "flip_y",
        "opacity",
        "brightness",
        "tint",
        "z",
    }
    for index, raw in enumerate(data, start=1):
        field = f"entities[{index}]"
        if not isinstance(raw, dict):
            raise SceneError(f"{field} must be a TOML table")
        unknown = set(raw) - allowed_fields
        if unknown:
            raise SceneError(f"{field} has unknown fields: {', '.join(sorted(unknown))}")
        asset = raw.get("asset")
        if not isinstance(asset, str) or not asset:
            raise SceneError(f"{field}.asset must name an XNB or PNG texture")
        at = raw.get("at")
        if not isinstance(at, list) or len(at) != 2:
            raise SceneError(f"{field}.at must be an array of two numbers")
        units = raw.get("units", "tiles")
        if units not in {"tiles", "pixels"}:
            raise SceneError(f"{field}.units must be 'tiles' or 'pixels'")
        multiplier = TILE_SIZE if units == "tiles" else 1
        position = (
            round(_number(at[0], f"{field}.at") * multiplier),
            round(_number(at[1], f"{field}.at") * multiplier),
        )

        source = None
        if "source" in raw:
            if "frame_size" in raw or "frame" in raw or "stride" in raw:
                raise SceneError(f"{field} cannot combine source with frame fields")
            source = _quad(raw["source"], f"{field}.source")
            if source[0] < 0 or source[1] < 0 or source[2] <= 0 or source[3] <= 0:
                raise SceneError(f"{field}.source must have non-negative x/y and positive width/height")
        elif "frame_size" in raw or "frame" in raw or "stride" in raw:
            frame_size = _pair(raw.get("frame_size"), f"{field}.frame_size", default=(16, 16))
            stride = _pair(raw.get("stride"), f"{field}.stride", default=frame_size)
            frame = _offset_pair(raw.get("frame"), f"{field}.frame", default=(0, 0))
            if frame[0] < 0 or frame[1] < 0:
                raise SceneError(f"{field}.frame values must be non-negative")
            source = frame[0] * stride[0], frame[1] * stride[1], frame_size[0], frame_size[1]

        anchor = raw.get("anchor", "top-left")
        if anchor not in anchors:
            raise SceneError(f"{field}.anchor has unsupported value {anchor!r}")
        opacity = _number(raw.get("opacity", 1.0), f"{field}.opacity")
        if not 0 <= opacity <= 1:
            raise SceneError(f"{field}.opacity must be from 0 through 1")
        brightness = _number(raw.get("brightness", 1.0), f"{field}.brightness")
        if brightness <= 0:
            raise SceneError(f"{field}.brightness must be positive")
        tint = _quad(raw.get("tint", [255, 255, 255, 255]), f"{field}.tint")
        if not all(0 <= channel <= 255 for channel in tint):
            raise SceneError(f"{field}.tint channels must be from 0 through 255")
        z = raw.get("z", 200)
        if not isinstance(z, int) or isinstance(z, bool):
            raise SceneError(f"{field}.z must be an integer")
        name = raw.get("name", asset)
        if not isinstance(name, str) or not name:
            raise SceneError(f"{field}.name must be a non-empty string")
        result.append(
            Entity(
                name=name,
                asset=asset,
                position=position,
                source=source,
                anchor=anchor,
                scale=_number_pair(raw.get("scale"), f"{field}.scale", default=(1.0, 1.0)),
                rotation=_number(raw.get("rotation", 0.0), f"{field}.rotation"),
                flip_x=_boolean(raw.get("flip_x", False), f"{field}.flip_x"),
                flip_y=_boolean(raw.get("flip_y", False), f"{field}.flip_y"),
                opacity=opacity,
                brightness=brightness,
                tint=tint,
                z=z,
            )
        )
    return tuple(result)


def _grid(raw: Any, layer: str, scene_path: Path, default_encoding: str) -> Grid:
    encoding = default_encoding
    if isinstance(raw, dict):
        unknown = set(raw) - {"file", "encoding"}
        if unknown:
            raise SceneError(f"map.{layer} has unknown fields: {', '.join(sorted(unknown))}")
        file_name = raw.get("file")
        if not isinstance(file_name, str) or not file_name:
            raise SceneError(f"map.{layer}.file must be a path")
        encoding = raw.get("encoding", default_encoding)
        source_path = Path(file_name)
        if not source_path.is_absolute():
            source_path = scene_path.parent / source_path
        try:
            raw = source_path.read_text(encoding="utf-8")
        except OSError as error:
            raise SceneError(f"cannot read map.{layer} file {source_path}: {error}") from error
    if not isinstance(raw, str):
        raise SceneError(f"map.{layer} must be a multiline string or file table")
    if encoding not in {"characters", "tokens"}:
        raise SceneError(f"map.{layer}.encoding must be 'characters' or 'tokens'")
    normalized = textwrap.dedent(raw).strip("\n")
    if not normalized:
        raise SceneError(f"map.{layer} cannot be empty")
    if encoding == "characters":
        if "\t" in normalized:
            raise SceneError(f"map.{layer} cannot contain tabs with character encoding")
        return tuple(normalized.splitlines())
    rows = tuple(
        tuple(sys.intern(token) for token in row.split())
        for row in normalized.splitlines()
    )
    if any(not row for row in rows):
        raise SceneError(f"map.{layer} token rows cannot be empty")
    return rows


def load_scene(path: Path) -> Scene:
    try:
        with path.open("rb") as stream:
            data = tomllib.load(stream)
    except (OSError, tomllib.TOMLDecodeError) as error:
        raise SceneError(f"cannot read {path}: {error}") from error

    if data.get("format") != FORMAT_VERSION:
        raise SceneError(f"format must be {FORMAT_VERSION}")

    canvas = data.get("canvas", {})
    if not isinstance(canvas, dict):
        raise SceneError("canvas must be a TOML table")
    scale = canvas.get("scale", 2)
    if not isinstance(scale, int) or isinstance(scale, bool) or scale <= 0:
        raise SceneError("canvas.scale must be a positive integer")
    boundary = canvas.get("boundary", "world")
    if boundary not in {"world", "open"}:
        raise SceneError("canvas.boundary must be 'world' or 'open'")
    background = canvas.get("background", "forest-day")
    if not isinstance(background, str):
        raise SceneError("canvas.background must be a string")
    sky = canvas.get("sky", "transparent" if background == "transparent" else "Background_0")
    if not isinstance(sky, str) or not sky:
        raise SceneError("canvas.sky must name a texture or be 'transparent'")
    horizon = canvas.get("horizon")
    if horizon is not None:
        horizon = _number(horizon, "canvas.horizon")
    canvas_size = canvas.get("size")
    if canvas_size is not None:
        canvas_size = _pair(canvas_size, "canvas.size", default=(1, 1))
    custom_background = canvas.get("background_layers")
    if custom_background is not None:
        if not isinstance(custom_background, list) or not all(
            isinstance(item, str) and item for item in custom_background
        ):
            raise SceneError("canvas.background_layers must be an array of texture names")
        background_layers = tuple(custom_background)
    else:
        if background not in BACKGROUND_PRESETS:
            choices = ", ".join(sorted(BACKGROUND_PRESETS))
            raise SceneError(f"unknown background {background!r}; choose {choices} or set background_layers")
        background_layers = None

    raw_map = data.get("map", {})
    if not isinstance(raw_map, dict):
        raise SceneError("map must be a TOML table")
    map_encoding = raw_map.get("encoding", "characters")
    if map_encoding not in {"characters", "tokens"}:
        raise SceneError("map.encoding must be 'characters' or 'tokens'")
    unknown_layers = set(raw_map) - {*LAYER_KINDS, "shapes", "encoding"}
    if unknown_layers:
        raise SceneError(f"map has unknown layers: {', '.join(sorted(unknown_layers))}")

    sprite_library = dict(BUILTINS)
    sprite_library.update(_custom_sprites(data.get("sprites")))
    raw_palette = data.get("palette", {})
    if not isinstance(raw_palette, dict):
        raise SceneError("palette must be a TOML table")
    palette: dict[str, Sprite] = {}
    for symbol, sprite_value in raw_palette.items():
        if not isinstance(symbol, str) or not symbol or any(character.isspace() for character in symbol):
            raise SceneError(f"palette key {symbol!r} must be a non-whitespace string")
        if map_encoding == "characters" and len(symbol) != 1:
            raise SceneError(f"palette key {symbol!r} must be one character with character encoding")
        if symbol == ".":
            raise SceneError("palette cannot redefine '.'; it means empty")
        if isinstance(sprite_value, str):
            try:
                palette[symbol] = sprite_library[sprite_value]
            except KeyError as error:
                raise SceneError(f"palette.{symbol} names unknown sprite {sprite_value!r}") from error
        else:
            palette[symbol] = _sprite_from_table(symbol, sprite_value, f"palette.{symbol}")

    layers: dict[str, Grid] = {}
    for layer in (*LAYER_KINDS, "shapes"):
        if layer in raw_map:
            layers[layer] = _grid(raw_map[layer], layer, path, map_encoding)

    if not layers and canvas_size is None:
        raise SceneError("canvas.size is required when map has no layers")
    if layers:
        first_rows = next(iter(layers.values()))
        height = len(first_rows)
        width = len(first_rows[0])
    else:
        assert canvas_size is not None
        width, height = canvas_size
    if width == 0:
        raise SceneError("map rows cannot be empty")
    if canvas_size is not None and canvas_size != (width, height):
        raise SceneError(
            f"canvas.size is {canvas_size[0]}x{canvas_size[1]}; map is {width}x{height}"
        )

    for layer, rows in layers.items():
        if len(rows) != height:
            raise SceneError(f"map.{layer} has {len(rows)} rows; expected {height}")
        for row_number, row in enumerate(rows, start=1):
            if len(row) != width:
                raise SceneError(
                    f"map.{layer} row {row_number} has {len(row)} columns; expected {width}"
                )

    for layer, allowed_kinds in LAYER_KINDS.items():
        for y, row in enumerate(layers.get(layer, ()), start=1):
            for x, symbol in enumerate(row, start=1):
                if symbol in {".", " "}:
                    continue
                sprite = palette.get(symbol)
                if sprite is None:
                    raise SceneError(f"map.{layer} uses undefined symbol {symbol!r} at {x},{y}")
                if sprite.kind not in allowed_kinds:
                    expected = " or ".join(sorted(allowed_kinds))
                    raise SceneError(
                        f"map.{layer} uses {sprite.kind} sprite {sprite.name!r} at {x},{y}; expected {expected}"
                    )

    terrain_rows = layers.get("terrain")
    object_rows = layers.get("objects")
    if terrain_rows is not None and object_rows is not None:
        for y, (terrain_row, object_row) in enumerate(
            zip(terrain_rows, object_rows, strict=True), start=1
        ):
            for x, (terrain_symbol, object_symbol) in enumerate(
                zip(terrain_row, object_row, strict=True), start=1
            ):
                if _empty(terrain_symbol) or _empty(object_symbol):
                    continue
                terrain_sprite = palette[terrain_symbol]
                object_sprite = palette[object_symbol]
                raise SceneError(
                    f"map.objects places {object_sprite.name!r} over "
                    f"map.terrain sprite {terrain_sprite.name!r} at {x},{y}; "
                    "a Terraria cell cannot contain both terrain and an object"
                )

    for y, row in enumerate(layers.get("shapes", ()), start=1):
        for x, shape in enumerate(row, start=1):
            if shape not in {".", " ", "/", "\\", "_"}:
                raise SceneError(f"map.shapes has unsupported shape {shape!r} at {x},{y}")

    name = data.get("name", path.stem)
    if not isinstance(name, str) or not name:
        raise SceneError("name must be a non-empty string")
    seed = data.get("seed", 0)
    if not isinstance(seed, int) or isinstance(seed, bool):
        raise SceneError("seed must be an integer")

    return Scene(
        path=path,
        name=name,
        seed=seed,
        scale=scale,
        boundary=boundary,
        sky=sky,
        background=background,
        background_layers=background_layers,
        palette=palette,
        layers=layers,
        entities=_entities(data.get("entities")),
        horizon=horizon,
        width=width,
        height=height,
    )


def _empty(symbol: str) -> bool:
    return symbol in {".", " "}


def _variant(seed: int, x: int, y: int, name: str, count: int = 3) -> int:
    value = (seed & 0xFFFFFFFF) ^ (x * 0x9E3779B1) ^ (y * 0x85EBCA77)
    for byte in name.encode("utf-8"):
        value = ((value ^ byte) * 0x45D9F3B) & 0xFFFFFFFF
    value ^= value >> 16
    return value % count


def _block_frame(
    up: bool,
    down: bool,
    left: bool,
    right: bool,
    up_left: bool,
    up_right: bool,
    down_left: bool,
    down_right: bool,
    variant: int,
) -> tuple[int, int]:
    """Return the standard Terraria frame column and row for a block."""
    if up and down and left and right:
        if not up_left and not up_right:
            return 6 + variant, 1
        if not down_left and not down_right:
            return 6 + variant, 2
        if not up_left and not down_left:
            return 10, variant
        if not up_right and not down_right:
            return 11, variant
        return 1 + variant, 1
    if not up and down and left and right:
        return 1 + variant, 0
    if up and not down and left and right:
        return 1 + variant, 2
    if up and down and not left and right:
        return 0, variant
    if up and down and left and not right:
        return 4, variant
    if not up and down and not left and right:
        return variant * 2, 3
    if not up and down and left and not right:
        return 1 + variant * 2, 3
    if up and not down and not left and right:
        return variant * 2, 4
    if up and not down and left and not right:
        return 1 + variant * 2, 4
    if up and down and not left and not right:
        return 5, variant
    if not up and not down and left and right:
        return 6 + variant, 4
    if not up and down and not left and not right:
        return 6 + variant, 0
    if up and not down and not left and not right:
        return 6 + variant, 3
    if not up and not down and not left and right:
        return 9, variant
    if not up and not down and left and not right:
        return 12, variant
    return 9 + variant, 3


def _wall_frame(mask: int, x: int, y: int, variant: int) -> tuple[int, int]:
    """Return Terraria's wall-atlas frame for an occupancy bitmask."""
    if not 0 <= mask <= 15:
        raise ValueError("wall mask must be from 0 through 15")
    if mask == 15:
        mask += CENTER_WALL_FRAME_LOOKUP[x % 3][y % 3]
    return WALL_FRAME_LOOKUP[mask][variant % 3]


def _platform_frame(left: str, right: str) -> tuple[int, int]:
    """Return the wood-platform frame for platform, solid, or empty neighbors."""
    if left == "platform" and right == "platform":
        return 0, 0
    if left == "platform" and right == "empty":
        return 1, 0
    if left == "empty" and right == "platform":
        return 2, 0
    if left == "solid" and right == "platform":
        return 3, 0
    if left == "platform" and right == "solid":
        return 4, 0
    if left == "solid" and right == "empty":
        return 6, 0
    if left == "empty" and right == "solid":
        return 7, 0
    return 5, 0


def _background_ground_row(image: Image.Image) -> int:
    """Find where a Terraria background turns into its opaque ground fill."""
    alpha = image.getchannel("A")
    required = max(1, int(image.width * 0.95))
    for y in range(image.height):
        histogram = alpha.crop((0, y, image.width, y + 1)).histogram()
        if image.width - histogram[0] >= required:
            return y
    return image.height


def _scene_horizon_pixels(scene: Scene) -> int:
    if scene.horizon is not None:
        return round(scene.horizon * TILE_SIZE)
    terrain = scene.layers.get("terrain")
    if terrain is not None:
        surface_rows = []
        for x in range(scene.width):
            for y, row in enumerate(terrain):
                if not _empty(row[x]):
                    surface_rows.append(y)
                    break
        if surface_rows:
            return int(median(surface_rows) * TILE_SIZE)
    return int(scene.height * TILE_SIZE * 0.66)


def _apply_shape(frame: Image.Image, shape: str) -> Image.Image:
    """Mask a full tile to Terraria's two-pixel stair-step slope geometry."""
    if shape in {".", " "}:
        return frame
    mask = Image.new("L", frame.size, 0)
    pixels = mask.load()
    width, height = frame.size
    for py in range(height):
        for px in range(width):
            keep = (
                (shape == "/" and py >= height - 2 - 2 * (px // 2))
                or (shape == "\\" and py >= 2 * (px // 2))
                or (shape == "_" and py >= height // 2)
            )
            if keep:
                pixels[px, py] = 255
    shaped = frame.copy()
    shaped.putalpha(ImageChops.multiply(frame.getchannel("A"), mask))
    return shaped


class AssetStore:
    def __init__(self, assets: Path | None):
        self.root, self.images = self._find_assets(assets)
        cache_base = Path(os.environ.get("XDG_CACHE_HOME", Path.home() / ".cache"))
        root_key = hashlib.sha1(str(self.root).encode("utf-8")).hexdigest()[:12]
        self.cache = cache_base / "terraria-scene" / root_key
        self.cache.mkdir(parents=True, exist_ok=True)
        self.loaded: dict[str, Image.Image] = {}

    @staticmethod
    def _normalize(candidate: Path) -> tuple[Path, Path] | None:
        candidate = candidate.expanduser().resolve()
        if (candidate / "Content" / "Images").is_dir():
            return candidate, candidate / "Content" / "Images"
        if candidate.name == "Images" and candidate.is_dir():
            root = candidate.parent.parent
            return root, candidate
        if candidate.is_dir() and any(
            path.suffix.lower() in {".png", ".xnb"} for path in candidate.rglob("*")
        ):
            return candidate, candidate
        return None

    @classmethod
    def _find_assets(cls, explicit: Path | None) -> tuple[Path, Path]:
        candidates: list[Path] = []
        if explicit is not None:
            candidates.append(explicit)
        env_path = os.environ.get("TERRARIA_PATH")
        if env_path:
            candidates.append(Path(env_path))
        candidates.extend(
            [
                Path.home() / ".local/share/Steam/steamapps/common/Terraria",
                Path.home() / ".steam/steam/steamapps/common/Terraria",
            ]
        )
        for candidate in candidates:
            normalized = cls._normalize(candidate)
            if normalized is not None:
                return normalized
        searched = "\n  ".join(str(path.expanduser()) for path in candidates)
        raise SceneError(
            "Terraria assets were not found. Pass --assets TERRARIA_DIR, set TERRARIA_PATH, "
            f"or provide a directory of exported PNG textures. Searched:\n  {searched}"
        )

    def load(self, asset: str) -> Image.Image:
        if asset in self.loaded:
            return self.loaded[asset]
        png = self.images / f"{asset}.png"
        xnb = self.images / f"{asset}.xnb"
        if png.is_file():
            image = Image.open(png).convert("RGBA")
        elif xnb.is_file():
            cached = self.cache / f"{asset}.png"
            if not cached.is_file() or cached.stat().st_mtime < xnb.stat().st_mtime:
                self._extract_xnb(xnb, cached)
            image = Image.open(cached).convert("RGBA")
        else:
            raise SceneError(f"texture {asset!r} was not found in {self.images}")
        self.loaded[asset] = image
        return image

    def _decoder(self) -> Path:
        decoder_dir = self.cache / "decoder"
        decoder = decoder_dir / "XnbDecode.dll"
        sources = [TOOL_DIR / "xnbdecode/XnbDecode.csproj", TOOL_DIR / "xnbdecode/Program.cs"]
        if decoder.is_file() and decoder.stat().st_mtime >= max(path.stat().st_mtime for path in sources):
            return decoder
        if shutil.which("dotnet") is None:
            raise SceneError("dotnet is required once to unpack the installed game's compressed XNB textures")
        decoder_dir.mkdir(parents=True, exist_ok=True)
        intermediate = self.cache / "decoder-obj"
        command = [
            "dotnet",
            "build",
            str(sources[0]),
            "--configuration",
            "Release",
            "--nologo",
            "--verbosity",
            "quiet",
            "--output",
            str(decoder_dir),
            f"-p:BaseIntermediateOutputPath={intermediate}/",
        ]
        result = subprocess.run(command, text=True, capture_output=True)
        if result.returncode != 0:
            details = (result.stderr or result.stdout).strip()
            raise SceneError(f"cannot build the XNB decoder:\n{details}")
        return decoder

    def _extract_xnb(self, xnb: Path, destination: Path) -> None:
        fna = self.root / "FNA.dll"
        if not fna.is_file():
            raise SceneError(
                f"{xnb.name} is compressed, but {fna} is missing. Export the owned XNB as "
                f"{xnb.stem}.png and pass its directory with --assets."
            )
        decoder = self._decoder()
        destination.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="extract-", dir=destination.parent) as temp_dir:
            body = Path(temp_dir) / "texture.bin"
            result = subprocess.run(
                ["dotnet", str(decoder), str(fna), str(xnb), str(body)],
                text=True,
                capture_output=True,
            )
            if result.returncode != 0:
                raise SceneError((result.stderr or result.stdout).strip())
            image = _read_xnb_texture_body(body.read_bytes(), xnb.name)
            temp_png = Path(temp_dir) / "texture.png"
            image.save(temp_png, format="PNG", optimize=False)
            temp_png.replace(destination)

    def scan_xnb_dimensions(self) -> dict[str, tuple[int, int]]:
        xnb_files = tuple(self.images.rglob("*.xnb"))
        if not xnb_files:
            return {}
        fna = self.root / "FNA.dll"
        if not fna.is_file():
            raise SceneError(
                f"cannot verify XNB assets because {fna} is missing; "
                "pass the complete Terraria directory"
            )
        decoder = self._decoder()
        with tempfile.TemporaryDirectory(prefix="scan-", dir=self.cache) as temp_dir:
            output = Path(temp_dir) / "textures.tsv"
            result = subprocess.run(
                [
                    "dotnet",
                    str(decoder),
                    "--scan",
                    str(fna),
                    str(self.images),
                    str(output),
                ],
                text=True,
                capture_output=True,
            )
            if result.returncode != 0:
                raise SceneError((result.stderr or result.stdout).strip())
            dimensions: dict[str, tuple[int, int]] = {}
            for line in output.read_text(encoding="utf-8").splitlines():
                name, width, height = line.split("\t")
                dimensions[name] = int(width), int(height)
            return dimensions


def _read_7bit(data: memoryview, position: int) -> tuple[int, int]:
    value = 0
    shift = 0
    while True:
        if position >= len(data) or shift > 28:
            raise SceneError("invalid 7-bit integer in XNB texture")
        byte = data[position]
        position += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            return value, position
        shift += 7


def _read_xnb_texture_body(raw: bytes, name: str) -> Image.Image:
    data = memoryview(raw)
    position = 0
    reader_count, position = _read_7bit(data, position)
    readers: list[str] = []
    for _ in range(reader_count):
        length, position = _read_7bit(data, position)
        end = position + length
        if end + 4 > len(data):
            raise SceneError(f"{name} has a truncated type-reader table")
        readers.append(bytes(data[position:end]).decode("utf-8", errors="replace"))
        position = end + 4
    if not any("Texture2DReader" in reader for reader in readers):
        raise SceneError(f"{name} does not contain a Texture2D")

    _, position = _read_7bit(data, position)  # Shared-resource count.
    root_reader, position = _read_7bit(data, position)
    if root_reader == 0:
        raise SceneError(f"{name} has no root texture")
    if position + 20 > len(data):
        raise SceneError(f"{name} has a truncated Texture2D header")
    surface_format, width, height, mip_count = struct.unpack_from("<4i", data, position)
    position += 16
    level_size = struct.unpack_from("<i", data, position)[0]
    position += 4
    expected_size = width * height * 4
    if width <= 0 or height <= 0 or mip_count <= 0:
        raise SceneError(f"{name} has invalid Texture2D dimensions")
    if surface_format not in {0, 20} or level_size != expected_size:
        raise SceneError(
            f"{name} uses unsupported surface format {surface_format} or compressed pixel data"
        )
    end = position + level_size
    if end > len(data):
        raise SceneError(f"{name} has truncated pixel data")
    pixels = bytes(data[position:end])
    image = Image.frombytes("RGBA", (width, height), pixels)
    if surface_format == 20:
        red, green, blue, alpha = image.split()
        image = Image.merge("RGBA", (blue, green, red, alpha))
    return image


class Renderer:
    def __init__(
        self,
        scene: Scene,
        assets: AssetStore,
        region: RenderRegion | None = None,
        *,
        horizon_pixels: int | None = None,
        frame_cache: dict[tuple[Any, ...], Image.Image] | None = None,
        entity_cache: dict[Entity, Image.Image] | None = None,
    ):
        self.scene = scene
        self.assets = assets
        self.region = region or RenderRegion(0, 0, scene.width, scene.height)
        if (
            self.region.x < 0
            or self.region.y < 0
            or self.region.width <= 0
            or self.region.height <= 0
            or self.region.x + self.region.width > scene.width
            or self.region.y + self.region.height > scene.height
        ):
            raise SceneError(
                f"render region {self.region.x},{self.region.y},{self.region.width},{self.region.height} "
                f"falls outside the {scene.width}x{scene.height} canvas"
            )
        self.scene_native_size = scene.width * TILE_SIZE, scene.height * TILE_SIZE
        self.origin = self.region.x * TILE_SIZE, self.region.y * TILE_SIZE
        self.native_size = self.region.width * TILE_SIZE, self.region.height * TILE_SIZE
        self.horizon_pixels = (
            horizon_pixels if horizon_pixels is not None else _scene_horizon_pixels(scene)
        )
        self.frame_cache = frame_cache if frame_cache is not None else {}
        self.entity_cache = entity_cache if entity_cache is not None else {}

    def render(self, *, grid: bool = False) -> Image.Image:
        image = self._background()
        self._render_entities(image, None, -300)
        self._render_walls(image)
        self._render_entities(image, -300, -200)
        self._render_liquids(image)
        self._render_entities(image, -200, 0)
        self._render_terrain(image)
        self._render_entities(image, 0, 100)
        self._render_objects(image)
        self._render_entities(image, 100, None)
        if grid:
            self._draw_grid(image)
        if self.scene.scale != 1:
            image = image.resize(
                (image.width * self.scene.scale, image.height * self.scene.scale),
                Image.Resampling.NEAREST,
            )
        return image

    def _local(self, x: int, y: int) -> tuple[int, int]:
        return x - self.origin[0], y - self.origin[1]

    def _cell_ranges(self, margin: int = 0) -> tuple[range, range]:
        min_x = max(0, self.region.x - margin)
        min_y = max(0, self.region.y - margin)
        max_x = min(self.scene.width, self.region.x + self.region.width + margin)
        max_y = min(self.scene.height, self.region.y + self.region.height + margin)
        return range(min_x, max_x), range(min_y, max_y)

    def _layer_margin(self, layer: str, minimum: int = 0) -> int:
        margin = minimum
        for sprite in self.scene.palette.values():
            if sprite.kind not in LAYER_KINDS.get(layer, set()):
                continue
            horizontal = max(
                -sprite.offset[0],
                sprite.offset[0] + sprite.frame_size[0] - TILE_SIZE,
                0,
            )
            vertical = max(
                -sprite.offset[1],
                sprite.offset[1] + sprite.frame_size[1] - TILE_SIZE,
                0,
            )
            margin = max(margin, (max(horizontal, vertical) + TILE_SIZE - 1) // TILE_SIZE)
        return margin

    def _background(self) -> Image.Image:
        if self.scene.sky == "transparent":
            sky = Image.new("RGBA", self.native_size, (0, 0, 0, 0))
        else:
            sky_asset = self.assets.load(self.scene.sky)
            if self.scene.sky == "Background_0":
                source_column = sky_asset.crop((0, 0, 1, sky_asset.height))
                source_top = self.origin[1] * sky_asset.height / self.scene_native_size[1]
                source_bottom = (
                    self.origin[1] + self.native_size[1]
                ) * sky_asset.height / self.scene_native_size[1]
                sky_column = source_column.transform(
                    (1, self.native_size[1]),
                    Image.Transform.EXTENT,
                    (0, source_top, 1, source_bottom),
                    Image.Resampling.BILINEAR,
                )
                sky = sky_column.resize(self.native_size, Image.Resampling.NEAREST)
            else:
                sky = Image.new("RGBA", self.native_size, (0, 0, 0, 0))
                start_x = (self.origin[0] // sky_asset.width) * sky_asset.width
                start_y = (self.origin[1] // sky_asset.height) * sky_asset.height
                for y in range(start_y, self.origin[1] + self.native_size[1], sky_asset.height):
                    for x in range(start_x, self.origin[0] + self.native_size[0], sky_asset.width):
                        sky.alpha_composite(sky_asset, self._local(x, y))
        layers = self.scene.background_layers
        if layers is None:
            layers = BACKGROUND_PRESETS[self.scene.background]
        if not layers:
            return sky

        horizon = self.horizon_pixels
        count = len(layers)
        for index, name in enumerate(layers):
            layer = self.assets.load(name)
            tiled = Image.new("RGBA", self.native_size, (0, 0, 0, 0))
            ground_offset = (count - index - 1) * 8 + 4
            y = horizon + ground_offset - _background_ground_row(layer)
            start_x = -(_variant(self.scene.seed, index, 0, name, max(1, layer.width)) // 3)
            first_x = start_x + ((self.origin[0] - start_x) // layer.width) * layer.width
            for x in range(first_x, self.origin[0] + self.native_size[0], layer.width):
                tiled.alpha_composite(layer, self._local(x, y))
            sky.alpha_composite(tiled)
        return sky

    def _sprite_at(self, layer: str, x: int, y: int) -> Sprite | None:
        rows = self.scene.layers.get(layer)
        if rows is None or not (0 <= x < self.scene.width and 0 <= y < self.scene.height):
            return None
        symbol = rows[y][x]
        return None if _empty(symbol) else self.scene.palette[symbol]

    def _continues_beyond_viewport(self, x: int, y: int) -> bool:
        if self.scene.boundary != "world" or y < 0:
            return False
        return x < 0 or x >= self.scene.width or y >= self.scene.height

    def _shape_at(self, x: int, y: int) -> str:
        shapes = self.scene.layers.get("shapes")
        if shapes is None or not (0 <= x < self.scene.width and 0 <= y < self.scene.height):
            return "."
        return shapes[y][x]

    def _terrain_connected(
        self,
        x: int,
        y: int,
        dx: int,
        dy: int,
        group: str | None,
    ) -> bool:
        current_sides = SHAPE_SIDES[self._shape_at(x, y)]
        neighbor_sides = SHAPE_SIDES[self._shape_at(x + dx, y + dy)]
        required_current = set()
        required_neighbor = set()
        if dx < 0:
            required_current.add("left")
            required_neighbor.add("right")
        elif dx > 0:
            required_current.add("right")
            required_neighbor.add("left")
        if dy < 0:
            required_current.add("up")
            required_neighbor.add("down")
        elif dy > 0:
            required_current.add("down")
            required_neighbor.add("up")
        if not required_current.issubset(current_sides):
            return False

        neighbor_x = x + dx
        neighbor_y = y + dy
        sprite = self._sprite_at("terrain", neighbor_x, neighbor_y)
        if sprite is None:
            return self._continues_beyond_viewport(neighbor_x, neighbor_y)
        return sprite.connect == group and required_neighbor.issubset(neighbor_sides)

    def _wall_present(self, x: int, y: int) -> bool:
        return self._sprite_at("walls", x, y) is not None or self._continues_beyond_viewport(x, y)

    def _frame(self, sprite: Sprite, column: int, row: int) -> Image.Image:
        key = (sprite.asset, column, row, sprite.frame_size, sprite.stride, sprite.brightness)
        if key in self.frame_cache:
            return self.frame_cache[key]
        if sprite.asset is None:
            raise SceneError(f"sprite {sprite.name!r} has no texture")
        sheet = self.assets.load(sprite.asset)
        left = column * sprite.stride[0]
        top = row * sprite.stride[1]
        right = left + sprite.frame_size[0]
        bottom = top + sprite.frame_size[1]
        if left < 0 or top < 0 or right > sheet.width or bottom > sheet.height:
            raise SceneError(
                f"sprite {sprite.name!r} frame {column},{row} falls outside "
                f"{sprite.asset} ({sheet.width}x{sheet.height})"
            )
        frame = sheet.crop((left, top, right, bottom))
        if sprite.brightness != 1.0:
            frame = ImageEnhance.Brightness(frame).enhance(sprite.brightness)
        self.frame_cache[key] = frame
        return frame

    def _render_walls(self, image: Image.Image) -> None:
        rows = self.scene.layers.get("walls")
        if rows is None:
            return
        x_range, y_range = self._cell_ranges(self._layer_margin("walls", 1))
        for y in y_range:
            row = rows[y]
            for x in x_range:
                symbol = row[x]
                if _empty(symbol):
                    continue
                sprite = self.scene.palette[symbol]
                variant = _variant(self.scene.seed, x, y, sprite.name)
                if sprite.autotile == "wall":
                    mask = (
                        int(self._wall_present(x, y - 1))
                        | int(self._wall_present(x - 1, y)) << 1
                        | int(self._wall_present(x + 1, y)) << 2
                        | int(self._wall_present(x, y + 1)) << 3
                    )
                    frame_column, frame_row = _wall_frame(mask, x, y, variant)
                else:
                    frame_column, frame_row = sprite.frame
                frame = self._frame(sprite, frame_column, frame_row)
                image.alpha_composite(
                    frame,
                    self._local(
                        x * TILE_SIZE + sprite.offset[0],
                        y * TILE_SIZE + sprite.offset[1],
                    ),
                )

    def _render_terrain(self, image: Image.Image) -> None:
        rows = self.scene.layers.get("terrain")
        if rows is None:
            return
        x_range, y_range = self._cell_ranges(self._layer_margin("terrain"))
        for y in y_range:
            row = rows[y]
            for x in x_range:
                symbol = row[x]
                if _empty(symbol):
                    continue
                sprite = self.scene.palette[symbol]
                variant = _variant(self.scene.seed, x, y, sprite.name)
                if sprite.autotile == "block":
                    group = sprite.connect
                    frame_column, frame_row = _block_frame(
                        self._terrain_connected(x, y, 0, -1, group),
                        self._terrain_connected(x, y, 0, 1, group),
                        self._terrain_connected(x, y, -1, 0, group),
                        self._terrain_connected(x, y, 1, 0, group),
                        self._terrain_connected(x, y, -1, -1, group),
                        self._terrain_connected(x, y, 1, -1, group),
                        self._terrain_connected(x, y, -1, 1, group),
                        self._terrain_connected(x, y, 1, 1, group),
                        variant,
                    )
                else:
                    frame_column, frame_row = sprite.frame
                frame = _apply_shape(
                    self._frame(sprite, frame_column, frame_row),
                    self._shape_at(x, y),
                )
                image.alpha_composite(
                    frame,
                    self._local(
                        x * TILE_SIZE + sprite.offset[0],
                        y * TILE_SIZE + sprite.offset[1],
                    ),
                )

    def _render_liquids(self, image: Image.Image) -> None:
        rows = self.scene.layers.get("liquids")
        if rows is None:
            return
        x_range, y_range = self._cell_ranges(self._layer_margin("liquids"))
        for y in y_range:
            row = rows[y]
            for x in x_range:
                symbol = row[x]
                if _empty(symbol):
                    continue
                sprite = self.scene.palette[symbol]
                frame = self._frame(sprite, *sprite.frame).copy()
                if y > 0 and not _empty(rows[y - 1][x]):
                    pixels = frame.load()
                    replacement_y = min(4, frame.height - 1)
                    for py in range(min(3, frame.height)):
                        for px in range(frame.width):
                            pixels[px, py] = pixels[px, replacement_y]
                image.alpha_composite(frame, self._local(x * TILE_SIZE, y * TILE_SIZE))

    def _render_objects(self, image: Image.Image) -> None:
        rows = self.scene.layers.get("objects")
        if rows is None:
            return
        objects: list[tuple[int, int, Sprite]] = []
        x_range, y_range = self._cell_ranges(self._layer_margin("objects", 12))
        for y in y_range:
            row = rows[y]
            for x in x_range:
                symbol = row[x]
                if not _empty(symbol):
                    objects.append((x, y, self.scene.palette[symbol]))

        for x, y, sprite in objects:
            if sprite.autotile == "torch":
                self._torch_glow(image, x, y)
        for x, y, sprite in objects:
            variant = _variant(self.scene.seed, x, y, sprite.name)
            if sprite.autotile == "forest-tree":
                self._forest_tree(image, x, y)
                continue
            if sprite.autotile == "platform":
                frame = self._frame(
                    sprite,
                    *_platform_frame(
                        self._platform_neighbor(x - 1, y),
                        self._platform_neighbor(x + 1, y),
                    ),
                )
            elif sprite.autotile == "rope":
                frame = self._frame(sprite, 5, variant)
            elif sprite.autotile == "torch":
                frame = self._frame(sprite, variant, 0)
            else:
                frame = self._frame(sprite, *sprite.frame)
            image.alpha_composite(
                frame,
                self._local(
                    x * TILE_SIZE + sprite.offset[0],
                    y * TILE_SIZE + sprite.offset[1],
                ),
            )

    def _platform_neighbor(self, x: int, y: int) -> str:
        sprite = self._sprite_at("objects", x, y)
        if sprite is not None and sprite.autotile == "platform":
            return "platform"
        if self._sprite_at("terrain", x, y) is not None:
            return "solid"
        return "empty"

    def _entity_image(self, entity: Entity) -> Image.Image:
        cached = self.entity_cache.get(entity)
        if cached is not None:
            return cached
        sheet = self.assets.load(entity.asset)
        if entity.source is None:
            frame = sheet.copy()
        else:
            left, top, width, height = entity.source
            if left + width > sheet.width or top + height > sheet.height:
                raise SceneError(
                    f"entity {entity.name!r} source {entity.source} falls outside "
                    f"{entity.asset} ({sheet.width}x{sheet.height})"
                )
            frame = sheet.crop((left, top, left + width, top + height))
        if entity.brightness != 1.0:
            frame = ImageEnhance.Brightness(frame).enhance(entity.brightness)
        if entity.tint != (255, 255, 255, 255):
            frame = ImageChops.multiply(frame, Image.new("RGBA", frame.size, entity.tint))
        if entity.opacity != 1.0:
            alpha = frame.getchannel("A").point(lambda value: round(value * entity.opacity))
            frame.putalpha(alpha)
        if entity.flip_x:
            frame = frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        if entity.flip_y:
            frame = frame.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        if entity.scale != (1.0, 1.0):
            size = (
                max(1, round(frame.width * entity.scale[0])),
                max(1, round(frame.height * entity.scale[1])),
            )
            frame = frame.resize(size, Image.Resampling.NEAREST)
        if entity.rotation % 360:
            frame = frame.rotate(entity.rotation, Image.Resampling.NEAREST, expand=True)
        self.entity_cache[entity] = frame
        return frame

    def _render_entities(
        self,
        image: Image.Image,
        minimum_z: int | None,
        maximum_z: int | None,
    ) -> None:
        anchor_factors = {
            "top-left": (0.0, 0.0),
            "top-center": (0.5, 0.0),
            "top-right": (1.0, 0.0),
            "center-left": (0.0, 0.5),
            "center": (0.5, 0.5),
            "center-right": (1.0, 0.5),
            "bottom-left": (0.0, 1.0),
            "bottom-center": (0.5, 1.0),
            "bottom-right": (1.0, 1.0),
        }
        for entity in sorted(self.scene.entities, key=lambda item: item.z):
            if minimum_z is not None and entity.z < minimum_z:
                continue
            if maximum_z is not None and entity.z >= maximum_z:
                continue
            frame = self._entity_image(entity)
            factor_x, factor_y = anchor_factors[entity.anchor]
            global_x = entity.position[0] - round(frame.width * factor_x)
            global_y = entity.position[1] - round(frame.height * factor_y)
            local_x, local_y = self._local(global_x, global_y)
            if (
                local_x >= image.width
                or local_y >= image.height
                or local_x + frame.width <= 0
                or local_y + frame.height <= 0
            ):
                continue
            image.alpha_composite(frame, (local_x, local_y))

    def _torch_glow(self, image: Image.Image, x: int, y: int) -> None:
        glow = Image.new("RGBA", image.size, (0, 0, 0, 0))
        draw = ImageDraw.Draw(glow)
        center_x, center_y = self._local(x * TILE_SIZE + 8, y * TILE_SIZE + 7)
        for radius, alpha in ((42, 12), (30, 16), (20, 24), (10, 34)):
            draw.ellipse(
                (
                    center_x - radius,
                    center_y - radius,
                    center_x + radius,
                    center_y + radius,
                ),
                fill=(255, 156, 52, alpha),
            )
        image.alpha_composite(glow)

    def _forest_tree(self, image: Image.Image, x: int, base_y: int) -> None:
        variant = _variant(self.scene.seed, x, base_y, "forest-tree")
        height = 4 + _variant(self.scene.seed + 17, x, base_y, "tree-height", 3)
        top_y = base_y - height + 1
        branches = self.assets.load("Tree_Branches_0")
        tops = self.assets.load("Tree_Tops_0")

        branch_y = (top_y + height // 2) * TILE_SIZE - 10
        branch = branches.crop((0, variant * 42, min(84, branches.width), variant * 42 + 42))
        image.alpha_composite(
            branch,
            self._local(x * TILE_SIZE + 8 - branch.width // 2, branch_y),
        )

        left_support = self._sprite_at("terrain", x - 1, base_y + 1) is not None
        right_support = self._sprite_at("terrain", x + 1, base_y + 1) is not None
        if self._continues_beyond_viewport(x - 1, base_y + 1):
            left_support = True
        if self._continues_beyond_viewport(x + 1, base_y + 1):
            right_support = True

        if left_support and right_support:
            root_style = _variant(self.scene.seed + 31, x, base_y, "tree-root")
        elif right_support:
            root_style = 1
        elif left_support:
            root_style = 2
        else:
            root_style = 3

        trunk_end = base_y + 1 if root_style == 3 else base_y
        for trunk_y in range(top_y, trunk_end):
            trunk_variant = _variant(self.scene.seed, x, trunk_y, "tree-trunk")
            trunk = self._frame(TREE_TRUNK_SPRITE, 0, trunk_variant)
            image.alpha_composite(
                trunk,
                self._local(x * TILE_SIZE - 2, trunk_y * TILE_SIZE - 2),
            )

        if root_style != 3:
            root_row = 6 + _variant(self.scene.seed, x, base_y, "tree-root-center")
            center_columns = {0: 4, 1: 0, 2: 3}
            if root_style in {0, 2}:
                left_root = self._frame(
                    TREE_TRUNK_SPRITE,
                    2,
                    6 + _variant(self.scene.seed, x - 1, base_y, "tree-root-left"),
                )
                image.alpha_composite(
                    left_root,
                    self._local((x - 1) * TILE_SIZE - 2, base_y * TILE_SIZE - 2),
                )
            if root_style in {0, 1}:
                right_root = self._frame(
                    TREE_TRUNK_SPRITE,
                    1,
                    6 + _variant(self.scene.seed, x + 1, base_y, "tree-root-right"),
                )
                image.alpha_composite(
                    right_root,
                    self._local((x + 1) * TILE_SIZE - 2, base_y * TILE_SIZE - 2),
                )
            center_root = self._frame(TREE_TRUNK_SPRITE, center_columns[root_style], root_row)
            image.alpha_composite(
                center_root,
                self._local(x * TILE_SIZE - 2, base_y * TILE_SIZE - 2),
            )

        top_left = variant * 82
        crown = tops.crop((top_left, 0, min(top_left + 82, tops.width), min(82, tops.height)))
        image.alpha_composite(
            crown,
            self._local(
                x * TILE_SIZE + 8 - crown.width // 2,
                top_y * TILE_SIZE - crown.height + 22,
            ),
        )

    def _draw_grid(self, image: Image.Image) -> None:
        draw = ImageDraw.Draw(image)
        color = (255, 255, 255, 42)
        for x in range(0, image.width + 1, TILE_SIZE):
            draw.line((x, 0, x, image.height), fill=color)
        for y in range(0, image.height + 1, TILE_SIZE):
            draw.line((0, y, image.width, y), fill=color)


def render_scene(
    scene_path: Path,
    output_path: Path,
    *,
    assets_path: Path | None = None,
    scale: int | None = None,
    seed: int | None = None,
    grid: bool = False,
    region: RenderRegion | None = None,
) -> Image.Image:
    scene = load_scene(scene_path)
    if scale is not None:
        if scale <= 0:
            raise SceneError("--scale must be a positive integer")
        scene = replace(scene, scale=scale)
    if seed is not None:
        scene = replace(scene, seed=seed)
    image = Renderer(scene, AssetStore(assets_path), region).render(grid=grid)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path, format="PNG", optimize=False)
    return image


def render_scene_tiles(
    scene_path: Path,
    output_dir: Path,
    *,
    assets_path: Path | None = None,
    tile_size: tuple[int, int] = (128, 128),
    scale: int | None = None,
    seed: int | None = None,
    grid: bool = False,
) -> dict[str, Any]:
    if tile_size[0] <= 0 or tile_size[1] <= 0:
        raise SceneError("--tile-size values must be positive")
    scene = load_scene(scene_path)
    if scale is not None:
        if scale <= 0:
            raise SceneError("--scale must be a positive integer")
        scene = replace(scene, scale=scale)
    if seed is not None:
        scene = replace(scene, seed=seed)
    assets = AssetStore(assets_path)
    horizon_pixels = _scene_horizon_pixels(scene)
    frame_cache: dict[tuple[Any, ...], Image.Image] = {}
    entity_cache: dict[Entity, Image.Image] = {}
    output_dir.mkdir(parents=True, exist_ok=True)
    tiles = []
    for y in range(0, scene.height, tile_size[1]):
        for x in range(0, scene.width, tile_size[0]):
            region = RenderRegion(
                x=x,
                y=y,
                width=min(tile_size[0], scene.width - x),
                height=min(tile_size[1], scene.height - y),
            )
            file_name = f"x{x}_y{y}.png"
            image = Renderer(
                scene,
                assets,
                region,
                horizon_pixels=horizon_pixels,
                frame_cache=frame_cache,
                entity_cache=entity_cache,
            ).render(grid=grid)
            image.save(output_dir / file_name, format="PNG", optimize=False)
            tiles.append(
                {
                    "file": file_name,
                    "region": [region.x, region.y, region.width, region.height],
                    "pixel_origin": [
                        region.x * TILE_SIZE * scene.scale,
                        region.y * TILE_SIZE * scene.scale,
                    ],
                    "pixels": [image.width, image.height],
                }
            )
    manifest = {
        "format": 1,
        "scene": scene.name,
        "canvas_tiles": [scene.width, scene.height],
        "canvas_pixels": [
            scene.width * TILE_SIZE * scene.scale,
            scene.height * TILE_SIZE * scene.scale,
        ],
        "tile_size": [tile_size[0], tile_size[1]],
        "scale": scene.scale,
        "tiles": tiles,
    }
    (output_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    return manifest


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="terraria_scene.py",
        description="Render a TOML text map with textures from an owned Terraria install.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    render = subparsers.add_parser("render", help="render a scene to a PNG")
    render.add_argument("scene", type=Path, help="input .toml scene")
    render.add_argument("--output", "-o", type=Path, help="output PNG")
    render.add_argument("--assets", type=Path, help="Terraria directory or exported PNG directory")
    render.add_argument("--scale", type=int, help="positive nearest-neighbor output scale")
    render.add_argument("--seed", type=int, help="override the scene's texture-variation seed")
    render.add_argument("--grid", action="store_true", help="draw the 16 px tile grid")
    render.add_argument(
        "--region",
        type=int,
        nargs=4,
        metavar=("X", "Y", "WIDTH", "HEIGHT"),
        help="render one tile-coordinate region of the canvas",
    )

    render_tiles = subparsers.add_parser(
        "render-tiles",
        help="render an arbitrarily large scene as independently usable PNG tiles",
    )
    render_tiles.add_argument("scene", type=Path, help="input .toml scene")
    render_tiles.add_argument("--output", "-o", type=Path, required=True, help="output directory")
    render_tiles.add_argument(
        "--assets",
        type=Path,
        help="Terraria directory or exported PNG directory",
    )
    render_tiles.add_argument(
        "--tile-size",
        type=int,
        nargs=2,
        default=(128, 128),
        metavar=("WIDTH", "HEIGHT"),
        help="maximum output tile size in Terraria cells",
    )
    render_tiles.add_argument("--scale", type=int, help="positive nearest-neighbor output scale")
    render_tiles.add_argument("--seed", type=int, help="override the variation seed")
    render_tiles.add_argument("--grid", action="store_true", help="draw the 16 px tile grid")

    validate = subparsers.add_parser("validate", help="validate a scene without loading textures")
    validate.add_argument("scene", type=Path, help="input .toml scene")

    subparsers.add_parser("list-sprites", help="list built-in sprite names")

    list_assets = subparsers.add_parser(
        "list-assets",
        help="list every addressable texture in the installed game",
    )
    list_assets.add_argument("pattern", nargs="?", default="*", help="case-insensitive glob")
    list_assets.add_argument("--assets", type=Path, help="Terraria or Images directory")
    list_assets.add_argument("--json", type=Path, help="write the matching catalog as JSON")

    verify_assets = subparsers.add_parser(
        "verify-assets",
        help="decode every installed texture and write a catalog with dimensions",
    )
    verify_assets.add_argument("--assets", type=Path, help="complete Terraria directory")
    verify_assets.add_argument("--output", "-o", type=Path, required=True, help="output JSON catalog")

    inspect_asset = subparsers.add_parser(
        "inspect-asset",
        help="decode an asset, report its size, and optionally export a source rectangle",
    )
    inspect_asset.add_argument("asset", help="asset name relative to Content/Images")
    inspect_asset.add_argument("--assets", type=Path, help="Terraria or Images directory")
    inspect_asset.add_argument("--output", "-o", type=Path, help="output PNG")
    inspect_asset.add_argument(
        "--source",
        type=int,
        nargs=4,
        metavar=("X", "Y", "WIDTH", "HEIGHT"),
        help="crop this source rectangle",
    )
    inspect_asset.add_argument("--scale", type=int, default=1, help="positive nearest-neighbor scale")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    try:
        if args.command == "validate":
            scene = load_scene(args.scene)
            print(f"ok: {scene.name} ({scene.width}x{scene.height} tiles)")
            return 0
        if args.command == "list-sprites":
            for name, sprite in sorted(BUILTINS.items()):
                print(f"{name:18} {sprite.kind}")
            return 0
        if args.command == "list-assets":
            store = AssetStore(args.assets)
            records = discover_assets(store.images, args.pattern)
            if args.json:
                write_catalog(records, args.json)
                print(f"wrote {args.json} ({len(records)} assets)")
            else:
                for record in records:
                    print(f"{record.name}\t{record.category}\t{record.format}")
                print(f"{len(records)} assets")
            return 0
        if args.command == "verify-assets":
            store = AssetStore(args.assets)
            xnb_dimensions = store.scan_xnb_dimensions()
            verified = []
            for record in discover_assets(store.images):
                if record.format == "xnb":
                    try:
                        width, height = xnb_dimensions[record.name]
                    except KeyError as error:
                        raise SceneError(f"asset scan omitted {record.name}") from error
                else:
                    with Image.open(store.images / f"{record.name}.png") as image:
                        width, height = image.size
                verified.append(
                    AssetRecord(
                        name=record.name,
                        category=record.category,
                        format=record.format,
                        width=width,
                        height=height,
                    )
                )
            write_catalog(tuple(verified), args.output)
            print(f"verified {len(verified)} textures; wrote {args.output}")
            return 0
        if args.command == "inspect-asset":
            if args.scale <= 0:
                raise SceneError("--scale must be a positive integer")
            image = AssetStore(args.assets).load(args.asset)
            source_size = image.size
            if args.source:
                left, top, width, height = args.source
                if left < 0 or top < 0 or width <= 0 or height <= 0:
                    raise SceneError("--source must have non-negative x/y and positive width/height")
                if left + width > image.width or top + height > image.height:
                    raise SceneError(f"--source falls outside {args.asset} ({image.width}x{image.height})")
                image = image.crop((left, top, left + width, top + height))
            if args.scale != 1:
                image = image.resize(
                    (image.width * args.scale, image.height * args.scale),
                    Image.Resampling.NEAREST,
                )
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                image.save(args.output, format="PNG", optimize=False)
            print(
                f"{args.asset}: {source_size[0]}x{source_size[1]} source, "
                f"{image.width}x{image.height} output"
            )
            return 0
        if args.command == "render-tiles":
            manifest = render_scene_tiles(
                args.scene,
                args.output,
                assets_path=args.assets,
                tile_size=tuple(args.tile_size),
                scale=args.scale,
                seed=args.seed,
                grid=args.grid,
            )
            print(f"rendered {len(manifest['tiles'])} tiles to {args.output}")
            return 0
        output = args.output or args.scene.with_suffix(".png")
        region = RenderRegion(*args.region) if args.region else None
        image = render_scene(
            args.scene,
            output,
            assets_path=args.assets,
            scale=args.scale,
            seed=args.seed,
            grid=args.grid,
            region=region,
        )
        print(f"rendered {output} ({image.width}x{image.height})")
        return 0
    except SceneError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
