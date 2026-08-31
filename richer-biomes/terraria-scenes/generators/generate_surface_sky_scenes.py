#!/usr/bin/env python3
"""Generate six deterministic, region-scale Terraria scene studies.

The generated character maps are intentionally plain text. They can be reviewed,
edited, validated, and rendered again without this script, while this script keeps
the original layouts reproducible.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from math import cos, pi, sin
from pathlib import Path
from typing import Callable, Iterable


ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / "sources"


@dataclass
class Scene:
    slug: str
    name: str
    width: int
    height: int
    seed: int
    boundary: str
    background: str
    horizon: int
    palette: str
    layers: dict[str, list[list[str]]] = field(init=False)

    def __post_init__(self) -> None:
        self.layers = {
            name: [["." for _ in range(self.width)] for _ in range(self.height)]
            for name in ("terrain", "walls", "liquids", "objects")
        }

    def inside(self, x: int, y: int) -> bool:
        return 0 <= x < self.width and 0 <= y < self.height

    def put(self, layer: str, x: int, y: int, token: str) -> None:
        if self.inside(x, y):
            self.layers[layer][y][x] = token

    def solid(self, x: int, y: int) -> bool:
        return self.inside(x, y) and self.layers["terrain"][y][x] != "."

    def place_object(self, x: int, y: int, token: str) -> None:
        if self.inside(x, y) and not self.solid(x, y):
            self.layers["objects"][y][x] = token

    def fill_rect(self, layer: str, x0: int, y0: int, x1: int, y1: int, token: str) -> None:
        for y in range(max(0, y0), min(self.height, y1)):
            for x in range(max(0, x0), min(self.width, x1)):
                self.layers[layer][y][x] = token

    def fill_ellipse(self, layer: str, cx: float, cy: float, rx: float, ry: float, token: str) -> None:
        for y in range(max(0, int(cy - ry - 1)), min(self.height, int(cy + ry + 2))):
            for x in range(max(0, int(cx - rx - 1)), min(self.width, int(cx + rx + 2))):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                    self.layers[layer][y][x] = token

    def carve_ellipse(self, cx: float, cy: float, rx: float, ry: float, wall: str = "s") -> None:
        for y in range(max(0, int(cy - ry - 1)), min(self.height, int(cy + ry + 2))):
            for x in range(max(0, int(cx - rx - 1)), min(self.width, int(cx + rx + 2))):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                    if self.layers["terrain"][y][x] != ".":
                        self.layers["terrain"][y][x] = "."
                        self.layers["walls"][y][x] = wall
                    self.layers["objects"][y][x] = "."

    def carve_tunnel(
        self,
        points: list[tuple[float, float]],
        radius_x: float = 2.2,
        radius_y: float = 2.0,
        wall: str = "s",
    ) -> None:
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            steps = max(1, int(max(abs(x1 - x0), abs(y1 - y0)) * 2))
            for step in range(steps + 1):
                t = step / steps
                self.carve_ellipse(
                    x0 + (x1 - x0) * t,
                    y0 + (y1 - y0) * t,
                    radius_x,
                    radius_y,
                    wall,
                )

    def platform(self, x0: int, x1: int, y: int, token: str = "=") -> None:
        for x in range(x0, x1):
            self.place_object(x, y, token)

    def rope(self, x: int, y0: int, y1: int) -> None:
        for y in range(y0, y1):
            self.place_object(x, y, "|")

    def torches(self, points: Iterable[tuple[int, int]]) -> None:
        for x, y in points:
            self.place_object(x, y, "*")

    def write(self) -> None:
        target = SOURCES / self.slug
        target.mkdir(parents=True, exist_ok=True)
        for layer, rows in self.layers.items():
            text = "\n".join("".join(row) for row in rows) + "\n"
            (target / f"{layer}.map").write_text(text, encoding="utf-8")

        toml = f'''format = 1
name = "{self.name}"
seed = {self.seed}

[canvas]
scale = 1
size = [{self.width}, {self.height}]
boundary = "{self.boundary}"
background = "{self.background}"
horizon = {self.horizon}

{COMMON_PALETTE}
{self.palette}

[map]
terrain = {{ file = "terrain.map" }}
walls = {{ file = "walls.map" }}
liquids = {{ file = "liquids.map" }}
objects = {{ file = "objects.map" }}
'''
        (target / "scene.toml").write_text(toml, encoding="utf-8")


COMMON_PALETTE = '''[palette]
D = "dirt"
S = "stone"
G = "grass"
W = "wood"
L = "living-wood"
d = "dirt-wall"
s = "stone-wall"
w = "wood-wall"
l = "living-wood-wall"
"~" = "water"
"=" = "wood-platform"
"|" = "rope"
"*" = "torch"
T = "forest-tree"
'''


CORRUPTION_PALETTE = '''
[palette.E]
kind = "tile"
asset = "Tiles_25"
frame_size = 16
stride = 18
autotile = "block"
connect = "ebonstone"

[palette.C]
kind = "tile"
asset = "Tiles_23"
frame_size = 16
stride = 18
autotile = "block"
connect = "corrupt-grass"

[palette.X]
kind = "tile"
asset = "Tiles_112"
frame_size = 16
stride = 18
autotile = "block"
connect = "ebonsand"

[palette.x]
kind = "object"
asset = "Tiles_32"
frame_size = 16
stride = 18
autotile = "fixed"
frame = [0, 0]
'''


HALLOW_PALETTE = '''
[palette.P]
kind = "tile"
asset = "Tiles_117"
frame_size = 16
stride = 18
autotile = "block"
connect = "pearlstone"

[palette.H]
kind = "tile"
asset = "Tiles_109"
frame_size = 16
stride = 18
autotile = "block"
connect = "hallow-grass"

[palette.Q]
kind = "tile"
asset = "Tiles_116"
frame_size = 16
stride = 18
autotile = "block"
connect = "pearlsand"

[palette.c]
kind = "object"
asset = "Tiles_129"
frame_size = 16
stride = 18
autotile = "fixed"
frame = [0, 0]
'''


OCEAN_PALETTE = '''
[palette.A]
kind = "tile"
asset = "Tiles_53"
frame_size = 16
stride = 18
autotile = "block"
connect = "sand"

[palette.B]
kind = "tile"
asset = "Tiles_396"
frame_size = 16
stride = 18
autotile = "block"
connect = "sandstone"

[palette.R]
kind = "object"
asset = "Tiles_81"
frame_size = 16
stride = 18
autotile = "fixed"
frame = [0, 0]
'''


SKY_PALETTE = '''
[palette.K]
kind = "tile"
asset = "Tiles_189"
frame_size = 16
stride = 18
autotile = "block"
connect = "cloud"

[palette.U]
kind = "tile"
asset = "Tiles_202"
frame_size = 16
stride = 18
autotile = "block"
connect = "sunplate"
'''


DESERT_PALETTE = '''
[palette.A]
kind = "tile"
asset = "Tiles_53"
frame_size = 16
stride = 18
autotile = "block"
connect = "sand"

[palette.B]
kind = "tile"
asset = "Tiles_396"
frame_size = 16
stride = 18
autotile = "block"
connect = "sandstone"

[palette.N]
kind = "tile"
asset = "Tiles_397"
frame_size = 16
stride = 18
autotile = "block"
connect = "hardened-sand"

[palette.F]
kind = "tile"
asset = "Tiles_404"
frame_size = 16
stride = 18
autotile = "block"
connect = "desert-fossil"
'''


def surface_fill(
    scene: Scene,
    surface: list[int],
    top: str,
    shallow: str,
    deep: str,
    deep_at: int = 14,
) -> None:
    for x, sy in enumerate(surface):
        for y in range(sy, scene.height):
            if y == sy:
                token = top
            elif y < sy + deep_at:
                token = shallow
            else:
                token = deep
            scene.put("terrain", x, y, token)


def corruption_fault() -> Scene:
    w, h = 128, 72
    scene = Scene("corruption-fault", "Corruption fault province", w, h, 2401, "world", "forest-day", 31, CORRUPTION_PALETTE)
    surface = [27 + round(4 * sin(x / 10) + 2 * sin(x / 4.7)) for x in range(w)]
    surface_fill(scene, surface, "C", "D", "E", 9)
    for x in range(0, w, 9):
        if x not in range(52, 77):
            scene.place_object(x, surface[x] - 1, "x")

    # A long fault splits into branches, with chambers and cross routes instead of
    # becoming one sheer, dead-end chasm.
    scene.carve_tunnel([(64, 20), (61, 31), (67, 42), (61, 55), (64, 71)], 4.6, 2.7)
    scene.carve_tunnel([(59, 29), (46, 35), (33, 34), (20, 41), (5, 40)], 2.5, 2.2)
    scene.carve_tunnel([(69, 36), (82, 31), (98, 38), (121, 35)], 2.5, 2.2)
    scene.carve_tunnel([(60, 51), (44, 56), (28, 52), (11, 61)], 2.7, 2.3)
    scene.carve_tunnel([(67, 55), (82, 59), (99, 54), (124, 61)], 2.7, 2.3)
    for chamber in [(35, 34, 7, 5), (93, 38, 8, 5), (28, 53, 7, 5), (102, 55, 8, 5), (63, 48, 7, 5)]:
        scene.carve_ellipse(*chamber)

    for y in (31, 45, 59):
        scene.platform(57, 71, y)
    scene.rope(63, 25, 69)
    scene.torches([(34, 32), (48, 54), (82, 57), (98, 36), (115, 59), (60, 42), (68, 52)])
    return scene


def hallow_crystal_ridge() -> Scene:
    w, h = 128, 72
    scene = Scene("hallow-crystal-ridge", "Hallow crystal ridge", w, h, 2402, "world", "forest-day", 32, HALLOW_PALETTE)
    surface: list[int] = []
    for x in range(w):
        peak = 24 * max(0.0, 1 - abs(x - 65) / 43)
        surface.append(36 - round(peak) + round(2 * sin(x / 5.3)))
    surface_fill(scene, surface, "H", "D", "P", 7)

    # Switchbacks give a no-hook route over the ridge. Interior passages provide
    # a protected alternative when high-altitude enemies make the summit dangerous.
    scene.carve_tunnel([(16, 34), (31, 28), (44, 25), (55, 20), (65, 18)], 2.6, 2.1)
    scene.carve_tunnel([(65, 18), (75, 22), (88, 28), (104, 31), (121, 36)], 2.6, 2.1)
    scene.carve_tunnel([(32, 42), (50, 39), (64, 43), (82, 38), (104, 43)], 2.6, 2.2)
    scene.carve_tunnel([(49, 58), (57, 48), (64, 43), (70, 32), (67, 20)], 2.4, 2.4)
    scene.carve_tunnel([(18, 58), (33, 53), (49, 58), (66, 55), (83, 59), (109, 54)], 2.5, 2.2)
    for chamber in [(30, 42, 8, 5), (64, 43, 9, 6), (98, 43, 8, 5), (49, 58, 7, 5), (86, 58, 8, 5)]:
        scene.carve_ellipse(*chamber)

    for x, y in [(27, 39), (35, 44), (58, 40), (65, 47), (73, 41), (95, 40), (47, 55), (83, 56), (102, 52)]:
        scene.place_object(x, y, "c")
    scene.platform(57, 72, 40)
    scene.platform(44, 56, 56)
    scene.platform(80, 94, 56)
    scene.rope(66, 20, 41)
    scene.rope(50, 47, 59)
    scene.torches([(31, 39), (48, 36), (79, 36), (100, 41), (52, 56), (91, 55)])
    return scene


def stepped_ocean_coast() -> Scene:
    w, h = 144, 72
    scene = Scene("stepped-ocean-coast", "Stepped ocean coast and sea caves", w, h, 2403, "world", "forest-day", 30, OCEAN_PALETTE)
    surface: list[int] = []
    for x in range(w):
        if x < 45:
            sy = 20 + round(3 * sin(x / 8))
        elif x < 65:
            sy = 23 + (x - 45) // 5
        elif x < 87:
            sy = 29 + (x - 65) // 4
        else:
            sy = 48 + round(3 * sin(x / 11)) + (x - 87) // 24
        surface.append(min(55, sy))

    for x, sy in enumerate(surface):
        for y in range(sy, h):
            if x < 50:
                token = "G" if y == sy else ("D" if y < sy + 8 else "S")
            elif y < sy + 6:
                token = "A"
            elif y < sy + 16:
                token = "B"
            else:
                token = "S"
            scene.put("terrain", x, y, token)

    # Flood the open coast and stepped shelf, then carve a chain of dry and flooded
    # sea caves with vertical air pockets.
    sea_level = 28
    for x in range(69, w):
        for y in range(sea_level, surface[x]):
            scene.put("liquids", x, y, "~")
    scene.carve_tunnel([(44, 34), (55, 38), (65, 43), (77, 45), (90, 50)], 3.0, 2.4)
    scene.carve_tunnel([(62, 55), (77, 52), (93, 57), (111, 54), (134, 59)], 2.8, 2.3)
    scene.carve_tunnel([(54, 39), (51, 48), (62, 55)], 2.5, 2.4)
    for chamber in [(49, 35, 8, 5), (77, 44, 9, 5), (92, 56, 9, 5), (119, 55, 10, 6)]:
        scene.carve_ellipse(*chamber)

    # Refill the submerged parts of the carved passages while keeping the first
    # chamber and its chimney as an air pocket.
    for y in range(sea_level, h):
        for x in range(69, w):
            if not scene.solid(x, y) and not (72 <= x <= 82 and 39 <= y <= 47):
                scene.put("liquids", x, y, "~")
    scene.platform(43, 55, 33)
    scene.rope(50, 34, 50)
    scene.torches([(46, 32), (53, 41), (59, 52)])
    for x in (86, 99, 112, 126, 137):
        scene.place_object(x, surface[x] - 1, "R")
    return scene


def sky_islands() -> Scene:
    w, h = 160, 90
    scene = Scene("sky-islands", "Sky continent with interior routes", w, h, 2404, "open", "sky", 80, SKY_PALETTE)

    # One continent spans almost the entire view. The smaller shoulders are still
    # part of the same traversable region, not isolated vanilla-sized loot rocks.
    for x in range(6, 154):
        normalized = abs(x - 80) / 78
        top = 20 + round(5 * sin(x / 14) + 2 * sin(x / 5.7)) + round(7 * normalized)
        bottom = 79 - round(19 * normalized**1.7) + round(3 * sin(x / 9))
        for y in range(max(0, top), min(h, bottom + 1)):
            depth = y - top
            edge = min(y - top, bottom - y)
            if edge <= 2 and y > top + 5:
                token = "K"
            elif depth == 0:
                token = "G"
            elif depth < 9:
                token = "D"
            else:
                token = "S"
            scene.put("terrain", x, y, token)

    # Two wide shoulders and cloud bridges make the silhouette read as an island
    # chain while preserving the scale of one explorable sky province.
    scene.fill_ellipse("terrain", 20, 37, 18, 9, "K")
    scene.fill_ellipse("terrain", 141, 40, 17, 9, "K")
    for x in range(9, 152):
        y = 28 + round(3 * sin(x / 13))
        if scene.solid(x, y + 1):
            scene.put("terrain", x, y, "G")

    # Three long floors, chambers, shafts, and diagonal connectors. There is always
    # more than one way through the island, even if a player avoids the open summit.
    scene.carve_tunnel([(12, 38), (35, 41), (58, 39), (80, 43), (107, 39), (146, 42)], 3.1, 2.5)
    scene.carve_tunnel([(19, 55), (43, 57), (66, 54), (88, 59), (116, 54), (145, 57)], 3.0, 2.5)
    scene.carve_tunnel([(34, 69), (57, 66), (80, 71), (106, 66), (130, 69)], 2.8, 2.4)
    scene.carve_tunnel([(31, 28), (36, 41), (43, 57), (57, 66)], 2.7, 2.5)
    scene.carve_tunnel([(80, 24), (78, 42), (88, 59), (80, 71)], 2.8, 2.6)
    scene.carve_tunnel([(130, 31), (119, 40), (116, 54), (106, 66)], 2.7, 2.5)
    for chamber in [(34, 41, 9, 6), (79, 43, 11, 7), (119, 40, 9, 6), (45, 57, 10, 6), (88, 58, 11, 7), (130, 57, 9, 6), (57, 67, 8, 5), (106, 67, 9, 5)]:
        scene.carve_ellipse(*chamber)

    # Broad sunplate halls are destinations within the island, not single-room huts.
    for x0, y0, x1, y1 in [(24, 47, 45, 55), (68, 49, 91, 57), (112, 46, 135, 54)]:
        for x in range(x0, x1):
            scene.put("terrain", x, y0, "U")
            scene.put("terrain", x, y1 - 1, "U")
        for y in range(y0, y1):
            scene.put("terrain", x0, y, "U")
            scene.put("terrain", x1 - 1, y, "U")
            for x in range(x0 + 1, x1 - 1):
                scene.put("walls", x, y, "w")
                if y not in (y0, y1 - 1):
                    scene.put("terrain", x, y, ".")
                    scene.put("objects", x, y, ".")
        # Two doors per hall remain open in this terrain study.
        for yy in range(y1 - 4, y1 - 1):
            scene.put("terrain", x0, yy, ".")
            scene.put("terrain", x1 - 1, yy, ".")

    for y in (39, 55, 67):
        scene.platform(30, 45, y)
        scene.platform(73, 89, y)
        scene.platform(116, 132, y)
    scene.rope(36, 29, 67)
    scene.rope(80, 25, 72)
    scene.rope(119, 31, 67)
    scene.torches([(29, 51), (40, 51), (73, 53), (86, 53), (117, 50), (130, 50), (55, 65), (104, 65)])
    for x in (19, 49, 101, 140):
        # Find the first solid below the open sky and put a tree above it.
        for y in range(h):
            if scene.solid(x, y):
                scene.place_object(x, y - 1, "T")
                break
    return scene


def rooted_underground() -> Scene:
    w, h = 128, 80
    scene = Scene("rooted-underground", "Rooted underground province", w, h, 2405, "world", "forest-day", 13, "")
    surface = [9 + round(2 * sin(x / 9) + sin(x / 4)) for x in range(w)]
    surface_fill(scene, surface, "G", "D", "S", 22)

    # A living-wood trunk and three roots cross the whole underground region. The
    # spaces beside and inside them form separate, connected traversal layers.
    for y in range(4, 69):
        half = max(2, 6 - y // 18)
        for x in range(64 - half, 65 + half):
            scene.put("terrain", x, y, "L")
    for points in [
        [(62, 32), (48, 40), (35, 51), (13, 63)],
        [(66, 36), (79, 43), (93, 54), (118, 66)],
        [(63, 49), (54, 58), (49, 75)],
        [(65, 50), (75, 62), (82, 78)],
    ]:
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            steps = int(max(abs(x1 - x0), abs(y1 - y0)) * 2)
            for step in range(steps + 1):
                t = step / max(1, steps)
                cx, cy = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
                scene.fill_ellipse("terrain", cx, cy, 2.5, 2.0, "L")

    scene.carve_tunnel([(10, 25), (29, 28), (47, 24), (61, 31), (79, 26), (99, 30), (121, 24)], 2.7, 2.3, "d")
    scene.carve_tunnel([(8, 45), (27, 41), (45, 47), (64, 43), (84, 48), (103, 41), (123, 46)], 2.8, 2.4, "d")
    scene.carve_tunnel([(11, 66), (29, 61), (48, 68), (64, 61), (83, 69), (105, 62), (124, 68)], 2.8, 2.4, "s")
    scene.carve_tunnel([(62, 13), (59, 27), (64, 43), (61, 60), (64, 78)], 2.7, 2.5, "l")
    scene.carve_tunnel([(29, 28), (27, 41), (29, 61)], 2.5, 2.4, "d")
    scene.carve_tunnel([(99, 30), (103, 41), (105, 62)], 2.5, 2.4, "d")
    for chamber in [(29, 28, 8, 5), (94, 29, 9, 5), (42, 46, 9, 6), (82, 47, 10, 6), (28, 63, 9, 5), (64, 61, 11, 7), (105, 63, 9, 5)]:
        scene.carve_ellipse(*chamber, wall="l" if chamber[0] in (42, 64, 82) else "s")

    # A few pools sit in lower chambers, leaving dry ledges around them.
    for cx, cy, rx in [(28, 65, 6), (65, 65, 7), (105, 65, 6)]:
        for x in range(cx - rx, cx + rx + 1):
            for y in range(cy, cy + 2):
                if scene.inside(x, y) and not scene.solid(x, y):
                    scene.put("liquids", x, y, "~")
    scene.platform(53, 75, 29)
    scene.platform(54, 75, 44)
    scene.platform(55, 74, 61)
    scene.rope(63, 12, 76)
    scene.rope(29, 27, 62)
    scene.rope(103, 30, 63)
    scene.torches([(14, 23), (39, 24), (83, 25), (114, 23), (18, 43), (48, 44), (76, 44), (113, 44), (42, 65), (88, 66)])
    for x in (12, 34, 64, 91, 117):
        scene.place_object(x, surface[x] - 1, "T")
    return scene


def underground_desert() -> Scene:
    w, h = 144, 80
    scene = Scene("underground-desert", "Underground desert chamber network", w, h, 2406, "world", "forest-day", 15, DESERT_PALETTE)
    surface = [11 + round(4 * sin(x / 13) + 2 * sin(x / 5.2)) for x in range(w)]
    for x, sy in enumerate(surface):
        for y in range(sy, h):
            depth = y - sy
            if depth < 8:
                token = "A"
            elif depth < 23:
                token = "N"
            else:
                token = "B" if (x // 11 + y // 9) % 4 else "F"
            scene.put("terrain", x, y, token)

    # Three stacked chamber bands, joined by chimneys and sloping passages. This
    # makes the desert a province to navigate rather than a single antlion pit.
    for chamber in [
        (20, 27, 12, 7), (50, 25, 11, 7), (82, 29, 13, 8), (119, 25, 12, 7),
        (32, 48, 14, 8), (68, 47, 13, 8), (104, 49, 15, 9), (132, 46, 9, 7),
        (18, 68, 12, 7), (53, 66, 14, 8), (90, 69, 14, 8), (124, 66, 13, 8),
    ]:
        scene.carve_ellipse(*chamber)
    scene.carve_tunnel([(8, 29), (20, 27), (35, 32), (50, 25), (66, 31), (82, 29), (101, 34), (119, 25), (140, 30)], 2.6, 2.2)
    scene.carve_tunnel([(9, 52), (32, 48), (50, 54), (68, 47), (86, 54), (104, 49), (132, 46), (142, 52)], 2.7, 2.3)
    scene.carve_tunnel([(6, 70), (18, 68), (36, 72), (53, 66), (70, 72), (90, 69), (106, 73), (124, 66), (141, 71)], 2.7, 2.3)
    scene.carve_tunnel([(20, 14), (20, 27), (32, 48), (18, 68)], 2.5, 2.5)
    scene.carve_tunnel([(81, 15), (82, 29), (68, 47), (90, 69)], 2.6, 2.5)
    scene.carve_tunnel([(121, 13), (119, 25), (132, 46), (124, 66)], 2.5, 2.5)

    # Two broad buried galleries use sandstone and wood, with openings at both ends.
    for x0, y0, x1, y1 in [(42, 38, 64, 45), (94, 57, 119, 64)]:
        for x in range(x0, x1):
            scene.put("terrain", x, y0, "B")
            scene.put("terrain", x, y1 - 1, "B")
        for y in range(y0, y1):
            scene.put("terrain", x0, y, "B")
            scene.put("terrain", x1 - 1, y, "B")
            for x in range(x0 + 1, x1 - 1):
                scene.put("terrain", x, y, ".")
                scene.put("walls", x, y, "w")
                scene.put("objects", x, y, ".")
        for yy in range(y1 - 4, y1 - 1):
            scene.put("terrain", x0, yy, ".")
            scene.put("terrain", x1 - 1, yy, ".")

    for y in (27, 48, 68):
        scene.platform(15, 28, y)
        scene.platform(62, 75, y)
        scene.platform(113, 128, y)
    scene.rope(20, 13, 69)
    scene.rope(81, 14, 70)
    scene.rope(121, 12, 68)
    scene.torches([(18, 24), (47, 23), (85, 26), (117, 22), (28, 45), (60, 45), (101, 46), (130, 43), (18, 65), (52, 63), (90, 66), (124, 63)])
    return scene


def main() -> None:
    scenes: list[Callable[[], Scene]] = [
        corruption_fault,
        hallow_crystal_ridge,
        stepped_ocean_coast,
        sky_islands,
        rooted_underground,
        underground_desert,
    ]
    for build in scenes:
        scene = build()
        scene.write()
        print(f"wrote {scene.slug}: {scene.width}x{scene.height}")


if __name__ == "__main__":
    main()
