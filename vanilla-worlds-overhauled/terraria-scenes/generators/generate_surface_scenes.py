#!/usr/bin/env python3
"""Generate the six large surface-region Terraria scene studies."""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Iterable


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "sources"


@dataclass
class Scene:
    slug: str
    name: str
    width: int
    height: int
    seed: int
    horizon: int
    background: str = "forest-day"
    terrain: list[list[str]] = field(init=False)
    walls: list[list[str]] = field(init=False)
    liquids: list[list[str]] = field(init=False)
    objects: list[list[str]] = field(init=False)
    entities: list[str] = field(default_factory=list)

    def __post_init__(self) -> None:
        self.terrain = self.grid()
        self.walls = self.grid()
        self.liquids = self.grid()
        self.objects = self.grid()

    def grid(self) -> list[list[str]]:
        return [["." for _ in range(self.width)] for _ in range(self.height)]

    def inside(self, x: int, y: int) -> bool:
        return 0 <= x < self.width and 0 <= y < self.height

    def set_terrain(self, x: int, y: int, token: str) -> None:
        if self.inside(x, y):
            self.terrain[y][x] = token
            self.objects[y][x] = "."
            self.liquids[y][x] = "."

    def carve(self, x: int, y: int, wall: str | None = None) -> None:
        if self.inside(x, y):
            self.terrain[y][x] = "."
            if wall is not None:
                self.walls[y][x] = wall

    def carve_circle(self, cx: int, cy: int, rx: int, ry: int, wall: str | None = None) -> None:
        for y in range(cy - ry, cy + ry + 1):
            for x in range(cx - rx, cx + rx + 1):
                if ((x - cx) / max(rx, 1)) ** 2 + ((y - cy) / max(ry, 1)) ** 2 <= 1:
                    self.carve(x, y, wall)

    def carve_rect(self, x0: int, y0: int, x1: int, y1: int, wall: str | None = None) -> None:
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.carve(x, y, wall)

    def carve_path(self, points: Iterable[tuple[int, int]], radius: int, wall: str | None = None) -> None:
        pairs = list(points)
        for (x0, y0), (x1, y1) in zip(pairs, pairs[1:]):
            steps = max(abs(x1 - x0), abs(y1 - y0), 1)
            for step in range(steps + 1):
                t = step / steps
                x = round(x0 + (x1 - x0) * t)
                y = round(y0 + (y1 - y0) * t)
                self.carve_circle(x, y, radius, radius, wall)

    def place_object(self, x: int, y: int, token: str) -> None:
        if self.inside(x, y) and self.terrain[y][x] == ".":
            self.objects[y][x] = token
            self.liquids[y][x] = "."

    def platform(self, x0: int, x1: int, y: int) -> None:
        for x in range(x0, x1 + 1):
            self.place_object(x, y, "=")

    def rope(self, x: int, y0: int, y1: int) -> None:
        for y in range(y0, y1 + 1):
            self.place_object(x, y, "|")

    def water(self, x0: int, y0: int, x1: int, y1: int) -> None:
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if self.inside(x, y) and self.terrain[y][x] == "." and self.objects[y][x] == ".":
                    self.liquids[y][x] = "~"

    def fill_from_surface(
        self,
        surface: list[int],
        material: Callable[[int, int, int], str],
    ) -> None:
        for x, top in enumerate(surface):
            for y in range(top, self.height):
                self.set_terrain(x, y, material(x, y, top))

    def skin_surface(self, token: str, surface: list[int]) -> None:
        for x, top in enumerate(surface):
            self.set_terrain(x, top, token)


COMMON_PALETTE = '''
[palette]
G = "grass"
D = "dirt"
S = "stone"
L = "living-wood"
F = "leaf"
W = "wood"
d = "dirt-wall"
s = "stone-wall"
l = "living-wood-wall"
w = "wood-wall"
"~" = "water"
"=" = "wood-platform"
"|" = "rope"
"*" = "torch"
T = "forest-tree"

[palette.A]
kind = "tile"
asset = "Tiles_53"
frame_size = 16
stride = 18
autotile = "block"
connect = "sand"

[palette.H]
kind = "tile"
asset = "Tiles_396"
frame_size = 16
stride = 18
autotile = "block"
connect = "sandstone"

[palette.N]
kind = "tile"
asset = "Tiles_147"
frame_size = 16
stride = 18
autotile = "block"
connect = "snow"

[palette.I]
kind = "tile"
asset = "Tiles_161"
frame_size = 16
stride = 18
autotile = "block"
connect = "ice"

[palette.M]
kind = "tile"
asset = "Tiles_59"
frame_size = 16
stride = 18
autotile = "block"
connect = "mud"

[palette.J]
kind = "tile"
asset = "Tiles_60"
frame_size = 16
stride = 18
autotile = "block"
connect = "jungle-grass"

[palette.O]
kind = "tile"
asset = "Tiles_6"
frame_size = 16
stride = 18
autotile = "block"
connect = "iron-ore"

[palette.C]
kind = "tile"
asset = "Tiles_7"
frame_size = 16
stride = 18
autotile = "block"
connect = "copper-ore"
'''


def map_text(grid: list[list[str]]) -> str:
    return "\n".join("".join(row) for row in grid)


def write_scene(scene: Scene) -> None:
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    entities = "\n\n".join(scene.entities)
    if entities:
        entities = "\n\n" + entities
    source = f'''format = 1
name = "{scene.name}"
seed = {scene.seed}

[canvas]
size = [{scene.width}, {scene.height}]
scale = 1
boundary = "world"
background = "{scene.background}"
horizon = {scene.horizon}
{COMMON_PALETTE}

[map]
terrain = \'\'\'
{map_text(scene.terrain)}
\'\'\'

walls = \'\'\'
{map_text(scene.walls)}
\'\'\'

liquids = \'\'\'
{map_text(scene.liquids)}
\'\'\'

objects = \'\'\'
{map_text(scene.objects)}
\'\'\'
{entities}
'''
    (SOURCE_DIR / f"{scene.slug}.toml").write_text(source.rstrip() + "\n", encoding="utf-8")


def make_vertical_forest() -> Scene:
    scene = Scene("vertical-forest", "Vertical forest region", 112, 64, 4101, 24)
    surface = [22 + round(5 * math.sin(x / 9) + 2 * math.sin(x / 3.8)) for x in range(scene.width)]
    scene.fill_from_surface(surface, lambda _x, y, top: "D" if y < top + 10 else "S")
    scene.skin_surface("G", surface)

    # A ravine links the surface to the root-cave band.
    for y in range(17, 46):
        half = 2 + max(0, (y - 20) // 10)
        for x in range(53 - half, 54 + half):
            scene.carve(x, y, "d" if y < 35 else "s")
    scene.carve_path([(5, 39), (25, 35), (43, 43), (63, 38), (83, 46), (107, 40)], 3, "s")
    for chamber in [(16, 48, 9, 6), (40, 51, 8, 5), (69, 48, 10, 6), (96, 51, 9, 5)]:
        scene.carve_circle(*chamber, "s")

    # Three hollow living trees create a canopy, trunk, ground, and root route.
    for cx, top, bottom in [(18, 4, 39), (69, 2, 41), (96, 7, 38)]:
        for y in range(top + 6, bottom):
            for x in range(cx - 3, cx + 4):
                if x in (cx - 3, cx + 3):
                    scene.set_terrain(x, y, "L")
                else:
                    scene.carve(x, y, "l")
        for x in range(cx - 13, cx + 14):
            for y in range(top, top + 9):
                if scene.inside(x, y) and ((x - cx) / 13) ** 2 + ((y - top - 5) / 5) ** 2 <= 1:
                    if abs(x - cx) > 3 or y < top + 5:
                        scene.set_terrain(x, y, "F")
        scene.platform(cx - 2, cx + 2, top + 12)
        scene.platform(cx - 2, cx + 2, top + 21)
        scene.rope(cx, top + 13, bottom - 2)

    # Canopy bridges and lower ledges supply two alternatives to the forest floor.
    scene.platform(21, 66, 14)
    scene.platform(72, 93, 17)
    scene.platform(8, 46, 30)
    scene.platform(60, 106, 31)
    scene.rope(37, 15, 29)
    scene.rope(82, 18, 30)
    for x in (7, 31, 47, 77, 106):
        y = surface[x] - 1
        scene.place_object(x, y, "T")
    for x, y in [(10, 38), (32, 36), (49, 44), (60, 37), (78, 45), (103, 40), (18, 25), (69, 24), (96, 27)]:
        scene.place_object(x, y, "*")
    scene.water(45, 43, 51, 46)
    return scene


def make_mountain() -> Scene:
    scene = Scene("cross-biome-mountain", "Sky-reaching cross-biome mountain", 128, 80, 4102, 62)
    surface: list[int] = []
    for x in range(scene.width):
        distance = abs(x - 64) / 58
        if distance <= 1:
            top = 4 + round(57 * distance ** 0.78)
        else:
            top = 62 + round(2 * math.sin(x / 5))
        surface.append(min(65, top))

    def mountain_material(x: int, y: int, top: int) -> str:
        if top < 22 and y < top + 5:
            return "N" if (x + y) % 4 else "I"
        if x < 43 and y < top + 4:
            return "D"
        if x > 87 and y < top + 5:
            return "M"
        return "S"

    scene.fill_from_surface(surface, mountain_material)
    for x, top in enumerate(surface):
        if top >= 30:
            scene.set_terrain(x, top, "G" if x < 87 else "J")
        elif top < 22:
            scene.set_terrain(x, top, "N")

    route = [(5, 62), (25, 55), (18, 46), (43, 39), (36, 30), (62, 18), (68, 12),
             (83, 27), (75, 38), (101, 48), (121, 61)]
    scene.carve_path(route, 3, "s")
    for cx, cy, rx, ry in [(25, 54, 8, 5), (42, 38, 7, 5), (62, 19, 8, 6),
                           (78, 34, 8, 5), (101, 48, 8, 5), (64, 57, 12, 6)]:
        scene.carve_circle(cx, cy, rx, ry, "s")
    # A central chimney is the fast, risky option through the high interior.
    scene.carve_rect(61, 20, 66, 58, "s")
    scene.rope(64, 20, 57)
    for x0, x1, y in [(19, 30, 55), (35, 48, 39), (55, 69, 21), (74, 88, 35),
                      (94, 108, 49), (56, 72, 58)]:
        scene.platform(x0, x1, y)
    for x, y in [(14, 59), (28, 51), (40, 37), (59, 18), (79, 31), (98, 45), (113, 57), (64, 44)]:
        scene.place_object(x, y, "*")
    for x in (7, 118):
        scene.place_object(x, surface[x] - 1, "T")
    scene.entities.extend([
        '''[[entities]]
name = "harpy above the summit"
asset = "NPC_48"
at = [76, 6]
units = "tiles"
source = [0, 0, 100, 86]
anchor = "center"
scale = 0.65
flip_x = true
z = 220''',
        '''[[entities]]
name = "harpy over the eastern face"
asset = "NPC_48"
at = [103, 17]
units = "tiles"
source = [0, 86, 100, 86]
anchor = "center"
scale = 0.55
z = 220''',
    ])
    return scene


def make_surface_mine() -> Scene:
    scene = Scene("surface-mine", "Regional surface mine and workings", 112, 64, 4103, 19)
    surface = [18 + round(2 * math.sin(x / 11)) for x in range(scene.width)]
    scene.fill_from_surface(surface, lambda _x, y, top: "D" if y < top + 8 else "S")
    scene.skin_surface("G", surface)

    # Open a broad stepped quarry, then link it to rooms and shafts on both sides.
    for y in range(16, 52):
        inset = max(0, (y - 16) // 3)
        for x in range(12 + inset, 101 - inset):
            scene.carve(x, y, "d" if y < 28 else "s")
    scene.carve_path([(14, 29), (5, 31), (3, 39)], 3, "s")
    scene.carve_path([(97, 31), (106, 34), (109, 43)], 3, "s")
    scene.carve_path([(55, 48), (55, 61)], 3, "s")
    for chamber in [(6, 42, 7, 5), (105, 47, 7, 5), (39, 57, 9, 4), (73, 57, 10, 4)]:
        scene.carve_circle(*chamber, "s")

    terraces = [(16, 43, 24), (56, 94, 24), (22, 48, 32), (61, 89, 32),
                (29, 51, 40), (60, 81, 40), (39, 73, 48)]
    for x0, x1, y in terraces:
        scene.platform(x0, x1, y)
    for x, y0, y1 in [(47, 24, 39), (63, 32, 47), (55, 49, 61), (9, 29, 41), (102, 33, 46)]:
        scene.rope(x, y0, y1)

    # Timber frames break the quarry into rooms without sealing any route.
    for x in (25, 50, 76, 91):
        for y in range(20, min(47, 20 + abs(x - 56) // 2)):
            if scene.terrain[y][x] == "." and y % 8 not in (0, 1):
                scene.set_terrain(x, y, "W")
    # A small office marks the mine entrance; the mine itself occupies most of the region.
    for x in range(1, 11):
        scene.set_terrain(x, 14, "W")
        scene.set_terrain(x, 8, "W")
    for y in range(9, 14):
        scene.set_terrain(1, y, "W")
        scene.set_terrain(10, y, "W")
        for x in range(2, 10):
            scene.walls[y][x] = "w"

    # Sparse ore pockets reward exploration without replacing normal mining.
    for cx, cy, token in [(4, 46, "C"), (108, 49, "O"), (36, 60, "C"), (77, 60, "O")]:
        for dx, dy in [(0, 0), (1, 0), (0, 1), (-1, 1)]:
            if scene.inside(cx + dx, cy + dy) and scene.terrain[cy + dy][cx + dx] != ".":
                scene.set_terrain(cx + dx, cy + dy, token)
    for x, y in [(18, 23), (42, 31), (67, 39), (52, 47), (8, 41), (104, 46), (40, 56), (76, 56), (5, 12)]:
        scene.place_object(x, y, "*")
    scene.water(67, 49, 78, 52)
    return scene


def make_desert() -> Scene:
    scene = Scene("desert-mesas", "Desert mesas and slot-cave region", 112, 64, 4104, 31, "sky")
    surface = [31 + round(2 * math.sin(x / 8)) for x in range(scene.width)]
    # Three mesas have long flat crowns and broad enough interiors for caves.
    for x in range(scene.width):
        if 8 <= x <= 33:
            surface[x] = 11 + (0 if 12 <= x <= 29 else abs(x - (12 if x < 12 else 29)))
        elif 48 <= x <= 79:
            surface[x] = 7 + (0 if 54 <= x <= 73 else abs(x - (54 if x < 54 else 73)))
        elif 90 <= x <= 107:
            surface[x] = 15 + (0 if 94 <= x <= 103 else abs(x - (94 if x < 94 else 103)))
    scene.fill_from_surface(surface, lambda _x, y, top: "A" if y < top + 7 else "H")

    # Arches and slot passages make each mesa an explorable volume.
    scene.carve_circle(21, 24, 9, 8, "s")
    scene.carve_rect(18, 19, 24, 33, "s")
    scene.carve_circle(63, 23, 12, 8, "s")
    scene.carve_path([(51, 31), (59, 25), (69, 28), (78, 34)], 3, "s")
    scene.carve_circle(98, 29, 7, 6, "s")
    scene.carve_path([(4, 43), (28, 39), (44, 47), (67, 40), (86, 48), (108, 41)], 3, "s")
    for chamber in [(28, 50, 8, 5), (55, 53, 9, 5), (86, 54, 10, 5)]:
        scene.carve_circle(*chamber, "s")

    # The oasis drains into the same cave system.
    scene.carve_rect(36, 30, 45, 37, "d")
    scene.water(36, 33, 45, 37)
    for x0, x1, y in [(13, 29, 27), (51, 75, 29), (92, 104, 31), (8, 32, 42),
                      (42, 68, 47), (77, 103, 49)]:
        scene.platform(x0, x1, y)
    for x, y0, y1 in [(21, 28, 41), (63, 30, 46), (98, 32, 48), (40, 30, 43)]:
        scene.rope(x, y0, y1)
    for x, y in [(14, 25), (29, 41), (53, 28), (74, 29), (95, 30), (25, 49), (56, 52), (86, 53)]:
        scene.place_object(x, y, "*")
    return scene


def make_snow() -> Scene:
    scene = Scene("snow-glacial-valley", "Glacial valley and ice chimneys", 112, 64, 4105, 25, "sky")
    surface: list[int] = []
    for x in range(scene.width):
        valley = 13 + round(21 * math.exp(-((x - 56) / 23) ** 2))
        ridge_noise = round(2 * math.sin(x / 4.5))
        surface.append(valley + ridge_noise)
    scene.fill_from_surface(surface, lambda _x, y, top: "N" if y < top + 7 else ("I" if y < 48 else "S"))

    # Two crevasses and a lower glacier tunnel offer vertical and horizontal routes.
    scene.carve_path([(25, 16), (28, 28), (24, 43), (32, 55)], 3, "s")
    scene.carve_path([(87, 15), (84, 30), (90, 43), (82, 55)], 3, "s")
    scene.carve_path([(3, 47), (25, 44), (48, 51), (68, 45), (89, 52), (109, 46)], 4, "s")
    for chamber in [(15, 51, 8, 5), (43, 54, 9, 5), (69, 52, 10, 6), (97, 54, 8, 5)]:
        scene.carve_circle(*chamber, "s")
    scene.carve_rect(52, 30, 60, 48, "s")

    for x0, x1, y in [(20, 32, 29), (18, 34, 42), (78, 93, 30), (80, 96, 43),
                      (48, 64, 38), (7, 31, 47), (37, 63, 51), (75, 103, 49)]:
        scene.platform(x0, x1, y)
    for x, y0, y1 in [(27, 18, 41), (87, 18, 42), (56, 31, 50)]:
        scene.rope(x, y0, y1)
    scene.water(50, 55, 72, 58)
    for x, y in [(25, 28), (25, 42), (87, 29), (88, 42), (55, 37), (14, 46), (45, 50), (78, 48), (99, 52)]:
        scene.place_object(x, y, "*")
    return scene


def make_jungle() -> Scene:
    scene = Scene("jungle-cenote", "Jungle cenote and root network", 112, 64, 4106, 18, "sky")
    surface = [18 + round(3 * math.sin(x / 7) + math.sin(x / 2.8)) for x in range(scene.width)]
    scene.fill_from_surface(surface, lambda _x, y, top: "M" if y < top + 24 else "S")
    scene.skin_surface("J", surface)

    # A wide cenote leads to a lake chamber rather than ending as a narrow shaft.
    for y in range(14, 53):
        half = 7 + (y - 14) // 7
        for x in range(56 - half, 57 + half):
            scene.carve(x, y, "d" if y < 36 else "s")
    scene.carve_circle(56, 50, 18, 10, "s")
    scene.carve_path([(4, 36), (25, 31), (40, 39), (56, 34), (73, 38), (91, 31), (108, 38)], 4, "d")
    scene.carve_path([(6, 52), (28, 48), (47, 55), (67, 49), (88, 56), (108, 50)], 3, "s")
    for chamber in [(18, 43, 9, 6), (34, 55, 8, 5), (79, 45, 9, 6), (96, 55, 8, 5)]:
        scene.carve_circle(*chamber, "s")

    # Thick living roots frame paths and split the cenote into several routes.
    for x0, y0, x1, y1 in [(12, 18, 37, 35), (100, 18, 76, 37), (31, 20, 48, 43), (82, 20, 65, 42)]:
        steps = max(abs(x1 - x0), abs(y1 - y0))
        for step in range(steps + 1):
            t = step / steps
            x = round(x0 + (x1 - x0) * t)
            y = round(y0 + (y1 - y0) * t)
            if scene.terrain[y][x] == "." and (x + y) % 3:
                scene.set_terrain(x, y, "L")
    for x0, x1, y in [(8, 36, 34), (76, 104, 34), (37, 52, 42), (61, 77, 42),
                      (10, 40, 49), (72, 104, 49)]:
        scene.platform(x0, x1, y)
    for x, y0, y1 in [(45, 20, 41), (67, 19, 41), (56, 18, 47), (23, 35, 48), (90, 35, 48)]:
        scene.rope(x, y0, y1)
    scene.water(40, 50, 72, 58)
    for x, y in [(9, 33), (27, 33), (42, 41), (70, 41), (85, 33), (103, 34), (17, 48), (95, 48), (55, 47)]:
        scene.place_object(x, y, "*")
    return scene


def main() -> None:
    scenes = [
        make_vertical_forest(),
        make_mountain(),
        make_surface_mine(),
        make_desert(),
        make_snow(),
        make_jungle(),
    ]
    for scene in scenes:
        write_scene(scene)
        print(f"wrote {scene.slug}.toml ({scene.width}x{scene.height})")


if __name__ == "__main__":
    main()
