#!/usr/bin/env python3
"""Render agent-authored text maps with textures from an owned Terraria install."""

from __future__ import annotations

import argparse
import hashlib
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
class Scene:
    path: Path
    name: str
    seed: int
    scale: int
    background: str
    background_layers: tuple[str, ...] | None
    palette: dict[str, Sprite]
    layers: dict[str, tuple[str, ...]]
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


def _pair(value: Any, field: str, *, default: tuple[int, int]) -> tuple[int, int]:
    if value is None:
        return default
    if isinstance(value, int):
        pair = (value, value)
    elif isinstance(value, list) and len(value) == 2 and all(isinstance(item, int) for item in value):
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
        isinstance(item, int) for item in value
    ):
        raise SceneError(f"{field} must be an array of two integers")
    return value[0], value[1]


def _custom_sprites(data: Any) -> dict[str, Sprite]:
    if data is None:
        return {}
    if not isinstance(data, dict):
        raise SceneError("sprites must be a TOML table")

    result: dict[str, Sprite] = {}
    for name, raw in data.items():
        if not isinstance(raw, dict):
            raise SceneError(f"sprites.{name} must be a TOML table")
        kind = raw.get("kind", "tile")
        if kind not in {"tile", "wall", "liquid", "object"}:
            raise SceneError(f"sprites.{name}.kind has unsupported value {kind!r}")
        asset = raw.get("asset")
        if not isinstance(asset, str) or not asset:
            raise SceneError(f"sprites.{name}.asset must name an XNB or PNG texture")

        default_size = (32, 32) if kind == "wall" else (16, 16)
        default_stride = (36, 36) if kind == "wall" else (18, 18)
        default_autotile = "wall" if kind == "wall" else "block" if kind == "tile" else "fixed"
        autotile = raw.get("autotile", default_autotile)
        if autotile not in {"block", "wall", "fixed", "platform", "rope", "torch", "liquid"}:
            raise SceneError(f"sprites.{name}.autotile has unsupported value {autotile!r}")

        connect = raw.get("connect")
        if connect is not None and not isinstance(connect, str):
            raise SceneError(f"sprites.{name}.connect must be a string")
        if connect is None and autotile in {"block", "wall"}:
            connect = kind

        brightness = raw.get("brightness", 1.0)
        if not isinstance(brightness, (int, float)) or brightness <= 0:
            raise SceneError(f"sprites.{name}.brightness must be positive")

        result[name] = Sprite(
            name=name,
            kind=kind,
            asset=asset,
            frame_size=_pair(raw.get("frame_size"), f"sprites.{name}.frame_size", default=default_size),
            stride=_pair(raw.get("stride"), f"sprites.{name}.stride", default=default_stride),
            frame=_offset_pair(raw.get("frame"), f"sprites.{name}.frame", default=(0, 0)),
            autotile=autotile,
            connect=connect,
            brightness=float(brightness),
            offset=_offset_pair(raw.get("offset"), f"sprites.{name}.offset", default=(0, 0)),
        )
    return result


def _grid(raw: Any, layer: str) -> tuple[str, ...]:
    if not isinstance(raw, str):
        raise SceneError(f"map.{layer} must be a multiline string")
    if "\t" in raw:
        raise SceneError(f"map.{layer} cannot contain tab characters")
    normalized = textwrap.dedent(raw).strip("\n")
    if not normalized:
        raise SceneError(f"map.{layer} cannot be empty")
    return tuple(normalized.splitlines())


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
    if not isinstance(scale, int) or not 1 <= scale <= 8:
        raise SceneError("canvas.scale must be an integer from 1 through 8")
    background = canvas.get("background", "forest-day")
    if not isinstance(background, str):
        raise SceneError("canvas.background must be a string")
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

    sprite_library = dict(BUILTINS)
    sprite_library.update(_custom_sprites(data.get("sprites")))
    raw_palette = data.get("palette")
    if not isinstance(raw_palette, dict) or not raw_palette:
        raise SceneError("palette must be a non-empty TOML table")
    palette: dict[str, Sprite] = {}
    for symbol, sprite_name in raw_palette.items():
        if not isinstance(symbol, str) or len(symbol) != 1:
            raise SceneError(f"palette key {symbol!r} must be one character")
        if symbol in {".", " "}:
            raise SceneError("palette cannot redefine '.' or a space; both mean empty")
        if not isinstance(sprite_name, str):
            raise SceneError(f"palette.{symbol} must name a sprite")
        try:
            palette[symbol] = sprite_library[sprite_name]
        except KeyError as error:
            raise SceneError(f"palette.{symbol} names unknown sprite {sprite_name!r}") from error

    raw_map = data.get("map")
    if not isinstance(raw_map, dict):
        raise SceneError("map must be a TOML table")
    if "terrain" not in raw_map:
        raise SceneError("map.terrain is required")
    layers: dict[str, tuple[str, ...]] = {"terrain": _grid(raw_map["terrain"], "terrain")}
    height = len(layers["terrain"])
    width = len(layers["terrain"][0])
    if width == 0:
        raise SceneError("map.terrain rows cannot be empty")
    if width > 240 or height > 135:
        raise SceneError("the map cannot exceed 240 columns by 135 rows")

    for layer in (*LAYER_KINDS.keys(), "shapes"):
        if layer == "terrain" or layer not in raw_map:
            continue
        layers[layer] = _grid(raw_map[layer], layer)

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

    for y, row in enumerate(layers.get("shapes", ()), start=1):
        for x, shape in enumerate(row, start=1):
            if shape not in {".", " ", "/", "\\", "_"}:
                raise SceneError(f"map.shapes has unsupported shape {shape!r} at {x},{y}")

    name = data.get("name", path.stem)
    if not isinstance(name, str) or not name:
        raise SceneError("name must be a non-empty string")
    seed = data.get("seed", 0)
    if not isinstance(seed, int):
        raise SceneError("seed must be an integer")

    return Scene(
        path=path,
        name=name,
        seed=seed,
        scale=scale,
        background=background,
        background_layers=background_layers,
        palette=palette,
        layers=layers,
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
        if candidate.is_dir() and any(candidate.glob("Tiles_0.*")):
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
    def __init__(self, scene: Scene, assets: AssetStore):
        self.scene = scene
        self.assets = assets
        self.native_size = scene.width * TILE_SIZE, scene.height * TILE_SIZE
        self.frame_cache: dict[tuple[Any, ...], Image.Image] = {}

    def render(self, *, grid: bool = False) -> Image.Image:
        image = self._background()
        self._render_autotiled_layer(image, "walls")
        self._render_liquids(image)
        self._render_autotiled_layer(image, "terrain")
        self._render_objects(image)
        if grid:
            self._draw_grid(image)
        if self.scene.scale != 1:
            image = image.resize(
                (image.width * self.scene.scale, image.height * self.scene.scale),
                Image.Resampling.NEAREST,
            )
        return image

    def _background(self) -> Image.Image:
        sky = self.assets.load("Background_0").resize(self.native_size, Image.Resampling.BILINEAR)
        layers = self.scene.background_layers
        if layers is None:
            layers = BACKGROUND_PRESETS[self.scene.background]
        if not layers:
            return sky

        surface_rows = []
        for x in range(self.scene.width):
            for y, row in enumerate(self.scene.layers["terrain"]):
                if not _empty(row[x]):
                    surface_rows.append(y)
                    break
        horizon = int(median(surface_rows) * TILE_SIZE) if surface_rows else int(self.native_size[1] * 0.66)
        count = len(layers)
        for index, name in enumerate(layers):
            layer = self.assets.load(name)
            tiled = Image.new("RGBA", self.native_size, (0, 0, 0, 0))
            bottom_offset = int((count - index - 1) * 10 + 8)
            y = horizon + bottom_offset - layer.height
            start_x = -(_variant(self.scene.seed, index, 0, name, max(1, layer.width)) // 3)
            for x in range(start_x, self.native_size[0], layer.width):
                tiled.alpha_composite(layer, (x, y))
            sky.alpha_composite(tiled)
        return sky

    def _sprite_at(self, layer: str, x: int, y: int) -> Sprite | None:
        rows = self.scene.layers.get(layer)
        if rows is None or not (0 <= x < self.scene.width and 0 <= y < self.scene.height):
            return None
        symbol = rows[y][x]
        return None if _empty(symbol) else self.scene.palette[symbol]

    def _connected(self, layer: str, x: int, y: int, group: str | None) -> bool:
        sprite = self._sprite_at(layer, x, y)
        return sprite is not None and sprite.connect == group

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

    def _render_autotiled_layer(self, image: Image.Image, layer: str) -> None:
        rows = self.scene.layers.get(layer)
        if rows is None:
            return
        for y, row in enumerate(rows):
            for x, symbol in enumerate(row):
                if _empty(symbol):
                    continue
                sprite = self.scene.palette[symbol]
                variant = _variant(self.scene.seed, x, y, sprite.name)
                if sprite.autotile in {"block", "wall"}:
                    group = sprite.connect
                    frame_column, frame_row = _block_frame(
                        self._connected(layer, x, y - 1, group),
                        self._connected(layer, x, y + 1, group),
                        self._connected(layer, x - 1, y, group),
                        self._connected(layer, x + 1, y, group),
                        self._connected(layer, x - 1, y - 1, group),
                        self._connected(layer, x + 1, y - 1, group),
                        self._connected(layer, x - 1, y + 1, group),
                        self._connected(layer, x + 1, y + 1, group),
                        variant,
                    )
                else:
                    frame_column, frame_row = sprite.frame
                frame = self._frame(sprite, frame_column, frame_row)
                if layer == "terrain":
                    frame = self._shape(frame, x, y)
                image.alpha_composite(
                    frame,
                    (x * TILE_SIZE + sprite.offset[0], y * TILE_SIZE + sprite.offset[1]),
                )

    def _shape(self, frame: Image.Image, x: int, y: int) -> Image.Image:
        shapes = self.scene.layers.get("shapes")
        if shapes is None:
            return frame
        shape = shapes[y][x]
        if shape in {".", " "}:
            return frame
        mask = Image.new("L", frame.size, 0)
        pixels = mask.load()
        width, height = frame.size
        for py in range(height):
            for px in range(width):
                keep = (
                    (shape == "/" and py >= height - 1 - px)
                    or (shape == "\\" and py >= px)
                    or (shape == "_" and py >= height // 2)
                )
                if keep:
                    pixels[px, py] = 255
        shaped = frame.copy()
        shaped.putalpha(ImageChops.multiply(frame.getchannel("A"), mask))
        return shaped

    def _render_liquids(self, image: Image.Image) -> None:
        rows = self.scene.layers.get("liquids")
        if rows is None:
            return
        for y, row in enumerate(rows):
            for x, symbol in enumerate(row):
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
                image.alpha_composite(frame, (x * TILE_SIZE, y * TILE_SIZE))

    def _render_objects(self, image: Image.Image) -> None:
        rows = self.scene.layers.get("objects")
        if rows is None:
            return
        objects: list[tuple[int, int, Sprite]] = []
        for y, row in enumerate(rows):
            for x, symbol in enumerate(row):
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
                frame = self._frame(sprite, variant, 0)
            elif sprite.autotile == "rope":
                frame = self._frame(sprite, 5, variant)
            elif sprite.autotile == "torch":
                frame = self._frame(sprite, variant, 0)
            else:
                frame = self._frame(sprite, *sprite.frame)
            image.alpha_composite(
                frame,
                (x * TILE_SIZE + sprite.offset[0], y * TILE_SIZE + sprite.offset[1]),
            )

    @staticmethod
    def _torch_glow(image: Image.Image, x: int, y: int) -> None:
        glow = Image.new("RGBA", image.size, (0, 0, 0, 0))
        draw = ImageDraw.Draw(glow)
        center_x = x * TILE_SIZE + 8
        center_y = y * TILE_SIZE + 7
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
        trunk_sheet = self.assets.load("Tiles_5")
        branches = self.assets.load("Tree_Branches_0")
        tops = self.assets.load("Tree_Tops_0")

        branch_y = (top_y + height // 2) * TILE_SIZE - 10
        branch = branches.crop((0, variant * 42, min(84, branches.width), variant * 42 + 42))
        image.alpha_composite(branch, (x * TILE_SIZE + 8 - branch.width // 2, branch_y))

        for trunk_y in range(top_y, base_y + 1):
            trunk_variant = _variant(self.scene.seed, x, trunk_y, "tree-trunk")
            left = trunk_variant * 22
            top = trunk_variant * 22
            trunk = trunk_sheet.crop((left, top, left + 20, top + 20))
            image.alpha_composite(trunk, (x * TILE_SIZE - 2, trunk_y * TILE_SIZE - 2))

        top_left = variant * 82
        crown = tops.crop((top_left, 0, min(top_left + 82, tops.width), min(82, tops.height)))
        image.alpha_composite(
            crown,
            (x * TILE_SIZE + 8 - crown.width // 2, top_y * TILE_SIZE - crown.height + 22),
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
) -> Image.Image:
    scene = load_scene(scene_path)
    if scale is not None:
        if not 1 <= scale <= 8:
            raise SceneError("--scale must be from 1 through 8")
        scene = replace(scene, scale=scale)
    if seed is not None:
        scene = replace(scene, seed=seed)
    image = Renderer(scene, AssetStore(assets_path)).render(grid=grid)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path, format="PNG", optimize=False)
    return image


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
    render.add_argument("--scale", type=int, help="nearest-neighbor output scale, from 1 through 8")
    render.add_argument("--seed", type=int, help="override the scene's texture-variation seed")
    render.add_argument("--grid", action="store_true", help="draw the 16 px tile grid")

    validate = subparsers.add_parser("validate", help="validate a scene without loading textures")
    validate.add_argument("scene", type=Path, help="input .toml scene")

    subparsers.add_parser("list-sprites", help="list built-in sprite names")
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
        output = args.output or args.scene.with_suffix(".png")
        image = render_scene(
            args.scene,
            output,
            assets_path=args.assets,
            scale=args.scale,
            seed=args.seed,
            grid=args.grid,
        )
        print(f"rendered {output} ({image.width}x{image.height})")
        return 0
    except SceneError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
