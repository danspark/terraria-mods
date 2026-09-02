#!/usr/bin/env python3
"""Generate the six large underground Terraria scene studies.

The output TOML files are deterministic. They use only Terraria texture names and
the version 1 scene format documented by tools/terraria-scene/FORMAT.md.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from math import hypot
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SOURCE_DIR = ROOT / "vanilla-worlds-overhauled/terraria-scenes/sources"


CUSTOM_SPRITES = {
    "N": ("tile", "Tiles_147", "block", "solid", 1.0),  # Snow
    "I": ("tile", "Tiles_161", "block", "solid", 1.0),  # Ice
    "U": ("tile", "Tiles_224", "block", "solid", 1.0),  # Slush
    "M": ("tile", "Tiles_59", "block", "solid", 1.0),   # Mud
    "J": ("tile", "Tiles_60", "block", "solid", 1.0),   # Jungle grass
    "H": ("tile", "Tiles_158", "block", "solid", 1.0),  # Rich Mahogany
    "X": ("tile", "Tiles_226", "block", "solid", 1.0),  # Lihzahrd brick
    "E": ("tile", "Tiles_25", "block", "solid", 1.0),   # Ebonstone
    "P": ("tile", "Tiles_117", "block", "solid", 1.0),  # Pearlstone
    "G": ("tile", "Tiles_70", "block", "solid", 1.0),   # Mushroom grass
    "A": ("tile", "Tiles_57", "block", "solid", 1.0),   # Ash
    "K": ("tile", "Tiles_75", "block", "solid", 1.0),   # Obsidian brick
    "F": ("tile", "Tiles_76", "block", "solid", 1.0),   # Hellstone brick
    "O": ("tile", "Tiles_56", "block", "solid", 1.0),   # Obsidian
    "Q": ("tile", "Tiles_58", "block", "solid", 1.0),   # Hellstone
    "1": ("tile", "Tiles_6", "block", "solid", 1.0),    # Iron
    "2": ("tile", "Tiles_7", "block", "solid", 1.0),    # Copper
    "3": ("tile", "Tiles_8", "block", "solid", 1.0),    # Gold
    "4": ("tile", "Tiles_9", "block", "solid", 1.0),    # Silver
    "5": ("tile", "Tiles_63", "block", "solid", 1.15),  # Sapphire
    "6": ("tile", "Tiles_64", "block", "solid", 1.15),  # Ruby
    "7": ("tile", "Tiles_65", "block", "solid", 1.15),  # Emerald
    "8": ("tile", "Tiles_68", "block", "solid", 1.15),  # Diamond
}


CUSTOM_WALLS = {
    "i": ("Wall_40", 0.58),
    "j": ("Wall_15", 0.52),
    "x": ("Wall_87", 0.55),
    "e": ("Wall_3", 0.38),
    "p": ("Wall_28", 0.66),
    "m": ("Wall_64", 0.58),
    "h": ("Wall_13", 0.48),
}


CUSTOM_OBJECTS = {
    "C": ("Tiles_129", [16, 16], [18, 18], [0, 0], 1.35),
    "T": ("Tiles_72", [16, 16], [18, 18], [0, 0], 1.0),
}


CUSTOM_LIQUIDS = {
    "L": "Liquid_1",  # Lava
    "Y": "Liquid_2",  # Honey
}


@dataclass
class SceneMap:
    width: int
    height: int
    base: str = "S"
    terrain: list[list[str]] = field(init=False)
    walls: list[list[str]] = field(init=False)
    liquids: list[list[str]] = field(init=False)
    objects: list[list[str]] = field(init=False)

    def __post_init__(self) -> None:
        self.terrain = [[self.base for _ in range(self.width)] for _ in range(self.height)]
        self.walls = [["." for _ in range(self.width)] for _ in range(self.height)]
        self.liquids = [["." for _ in range(self.width)] for _ in range(self.height)]
        self.objects = [["." for _ in range(self.width)] for _ in range(self.height)]

    def inside(self, x: int, y: int) -> bool:
        return 0 <= x < self.width and 0 <= y < self.height

    def fill_rect(self, x1: int, y1: int, x2: int, y2: int, material: str) -> None:
        for y in range(max(0, y1), min(self.height, y2 + 1)):
            for x in range(max(0, x1), min(self.width, x2 + 1)):
                self.terrain[y][x] = material

    def fill_ellipse(self, cx: float, cy: float, rx: float, ry: float, material: str,
                     only_solid: bool = False) -> None:
        for y in range(max(0, int(cy - ry - 1)), min(self.height, int(cy + ry + 2))):
            for x in range(max(0, int(cx - rx - 1)), min(self.width, int(cx + rx + 2))):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                    if not only_solid or self.terrain[y][x] != ".":
                        self.terrain[y][x] = material

    def carve_ellipse(self, cx: float, cy: float, rx: float, ry: float, wall: str) -> None:
        for y in range(max(0, int(cy - ry - 1)), min(self.height, int(cy + ry + 2))):
            for x in range(max(0, int(cx - rx - 1)), min(self.width, int(cx + rx + 2))):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                    self.terrain[y][x] = "."
                    self.walls[y][x] = wall

    def carve_rect(self, x1: int, y1: int, x2: int, y2: int, wall: str) -> None:
        for y in range(max(0, y1), min(self.height, y2 + 1)):
            for x in range(max(0, x1), min(self.width, x2 + 1)):
                self.terrain[y][x] = "."
                self.walls[y][x] = wall

    def carve_tunnel(self, start: tuple[float, float], end: tuple[float, float],
                     radius: float, wall: str) -> None:
        x1, y1 = start
        x2, y2 = end
        steps = max(1, int(hypot(x2 - x1, y2 - y1) * 2))
        for step in range(steps + 1):
            t = step / steps
            x = x1 + (x2 - x1) * t
            y = y1 + (y2 - y1) * t
            self.carve_ellipse(x, y, radius, radius * 0.8, wall)

    def draw_line(self, start: tuple[float, float], end: tuple[float, float],
                  radius: float, material: str) -> None:
        x1, y1 = start
        x2, y2 = end
        steps = max(1, int(hypot(x2 - x1, y2 - y1) * 2))
        for step in range(steps + 1):
            t = step / steps
            self.fill_ellipse(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t,
                              radius, radius, material)

    def paint_exposed(self, material: str, predicate=lambda _x, _y: True) -> None:
        updates: list[tuple[int, int]] = []
        for y in range(self.height):
            for x in range(self.width):
                if self.terrain[y][x] == "." or not predicate(x, y):
                    continue
                if any(self.inside(nx, ny) and self.terrain[ny][nx] == "."
                       for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))):
                    updates.append((x, y))
        for x, y in updates:
            self.terrain[y][x] = material

    def pool(self, x1: int, x2: int, y1: int, y2: int, liquid: str) -> None:
        for y in range(max(0, y1), min(self.height, y2 + 1)):
            for x in range(max(0, x1), min(self.width, x2 + 1)):
                if self.terrain[y][x] == ".":
                    self.liquids[y][x] = liquid

    def ledge(self, x1: int, x2: int, y: int, material: str, thickness: int = 1) -> None:
        for yy in range(y, min(self.height, y + thickness)):
            for x in range(max(0, x1), min(self.width, x2 + 1)):
                self.terrain[yy][x] = material
                self.objects[yy][x] = "."

    def platforms(self, x1: int, x2: int, y: int) -> None:
        for x in range(max(0, x1), min(self.width, x2 + 1)):
            if self.terrain[y][x] == ".":
                self.objects[y][x] = "="

    def rope(self, x: int, y1: int, y2: int) -> None:
        for y in range(max(0, y1), min(self.height, y2 + 1)):
            if self.terrain[y][x] == ".":
                self.objects[y][x] = "|"

    def torch(self, x: int, y: int) -> None:
        if self.inside(x, y) and self.terrain[y][x] == ".":
            self.objects[y][x] = "*"

    def cabin(self, x1: int, y1: int, x2: int, y2: int, block: str = "B",
              wall: str = "b", floors: tuple[int, ...] = ()) -> None:
        self.carve_rect(x1 + 1, y1 + 1, x2 - 1, y2 - 1, wall)
        self.ledge(x1, x2, y1, block)
        self.ledge(x1, x2, y2, block)
        for y in range(y1, y2 + 1):
            self.terrain[y][x1] = block
            self.terrain[y][x2] = block
            self.objects[y][x1] = "."
            self.objects[y][x2] = "."
        for floor_y in floors:
            self.ledge(x1 + 1, x2 - 1, floor_y, block)
        for y in range(y1 + 1, y2):
            for x in range(x1 + 1, x2):
                if self.terrain[y][x] == ".":
                    self.walls[y][x] = wall

    def put_object(self, x: int, y: int, symbol: str) -> None:
        if self.inside(x, y) and self.terrain[y][x] == ".":
            self.objects[y][x] = symbol

    def rows(self, layer: str) -> str:
        return "\n".join("".join(row) for row in getattr(self, layer))


def tile_table(symbol: str, spec: tuple[str, str, str, str, float]) -> str:
    kind, asset, autotile, connect, brightness = spec
    return f'''\n[palette."{symbol}"]\nkind = "{kind}"\nasset = "{asset}"\nframe_size = 16\nstride = 18\nautotile = "{autotile}"\nconnect = "{connect}"\nbrightness = {brightness}\n'''


def wall_table(symbol: str, spec: tuple[str, float]) -> str:
    asset, brightness = spec
    return f'''\n[palette."{symbol}"]\nkind = "wall"\nasset = "{asset}"\nframe_size = [32, 32]\nstride = [36, 36]\nautotile = "wall"\nconnect = "wall"\nbrightness = {brightness}\noffset = [-8, -8]\n'''


def object_table(symbol: str, spec: tuple[str, list[int], list[int], list[int], float]) -> str:
    asset, frame_size, stride, frame, brightness = spec
    return f'''\n[palette."{symbol}"]\nkind = "object"\nasset = "{asset}"\nframe_size = {frame_size}\nstride = {stride}\nautotile = "fixed"\nframe = {frame}\nbrightness = {brightness}\n'''


def liquid_table(symbol: str, asset: str) -> str:
    return f'''\n[palette."{symbol}"]\nkind = "liquid"\nasset = "{asset}"\nframe_size = [16, 16]\nstride = [16, 16]\nautotile = "liquid"\n'''


def scene_toml(name: str, seed: int, scene: SceneMap, *, sky: str,
               used: set[str], entities: str = "", notes: tuple[str, ...] = ()) -> str:
    comments = "\n".join(f"# {line}" for line in notes)
    builtins = {
        "S": "stone", "D": "dirt", "B": "wood", "b": "wood-wall",
        "s": "stone-wall", "w": "water", "=": "wood-platform",
        "|": "rope", "*": "torch",
    }
    palette_lines = [f'"{symbol}" = "{sprite}"' for symbol, sprite in builtins.items()
                     if symbol in used]
    custom = ""
    for symbol, spec in CUSTOM_SPRITES.items():
        if symbol in used:
            custom += tile_table(symbol, spec)
    for symbol, spec in CUSTOM_WALLS.items():
        if symbol in used:
            custom += wall_table(symbol, spec)
    for symbol, spec in CUSTOM_OBJECTS.items():
        if symbol in used:
            custom += object_table(symbol, spec)
    for symbol, asset in CUSTOM_LIQUIDS.items():
        if symbol in used:
            custom += liquid_table(symbol, asset)
    return f'''format = 1\nname = "{name}"\nseed = {seed}\n{comments}\n\n[canvas]\nsize = [{scene.width}, {scene.height}]\nscale = 1\nboundary = "world"\nbackground = "transparent"\nsky = "{sky}"\n\n[palette]\n{chr(10).join(palette_lines)}\n{custom}\n[map]\nterrain = \'''\n{scene.rows("terrain")}\n\'''\n\nwalls = \'''\n{scene.rows("walls")}\n\'''\n\nliquids = \'''\n{scene.rows("liquids")}\n\'''\n\nobjects = \'''\n{scene.rows("objects")}\n\'''\n{entities}'''


def frozen_fissures() -> str:
    m = SceneMap(120, 68, "S")
    m.fill_rect(0, 0, 119, 18, "N")
    m.fill_ellipse(25, 28, 26, 24, "N")
    m.fill_ellipse(92, 25, 28, 25, "N")
    for cx, cy, rx, ry in ((30, 14, 18, 7), (75, 14, 19, 7), (26, 34, 18, 9),
                           (67, 33, 22, 10), (98, 38, 17, 10), (58, 55, 30, 9),
                           (19, 56, 12, 7)):
        m.carve_ellipse(cx, cy, rx, ry, "i")
    for x in (13, 55, 91, 108):
        m.carve_tunnel((x, 0), (x + 4, 65), 2.4, "i")
    for a, b in (((30, 14), (26, 34)), ((30, 14), (67, 33)), ((75, 14), (67, 33)),
                 ((75, 14), (98, 38)), ((26, 34), (19, 56)), ((26, 34), (58, 55)),
                 ((67, 33), (58, 55)), ((98, 38), (58, 55))):
        m.carve_tunnel(a, b, 2.5, "i")
    m.paint_exposed("I", lambda _x, y: y > 10)
    m.paint_exposed("N", lambda _x, y: y <= 10)
    m.fill_ellipse(55, 64, 22, 4, "U", only_solid=True)
    for x1, x2, y in ((9, 17, 12), (49, 60, 20), (87, 96, 25), (10, 18, 31),
                      (49, 58, 39), (88, 96, 45), (14, 23, 53), (51, 64, 58)):
        m.platforms(x1, x2, y)
    for x, y1, y2 in ((13, 3, 30), (55, 2, 38), (91, 3, 43), (108, 14, 56), (59, 42, 62)):
        m.rope(x, y1, y2)
    m.pool(5, 30, 58, 65, "w")
    m.pool(46, 78, 59, 64, "w")
    m.cabin(91, 25, 111, 36, floors=(31,))
    for x, y in ((25, 13), (41, 31), (66, 31), (82, 35), (101, 29), (102, 34),
                 (22, 53), (56, 53), (75, 55)):
        m.torch(x, y)
    return scene_toml(
        "Frozen fissures: shelves, chimneys, and Boreal refuge", 42017, m,
        sky="Background_165", used=set("SNIRUibsw=|*B"),
        notes=("120 x 68 cells. Four full-height fissures connect seven large chambers.",
               "Alternating shelves, ropes, and side loops break every long fall.",
               "The right-hand Boreal refuge is a two-storey landmark, not the only route."),
    )


def jungle_root_network() -> str:
    m = SceneMap(120, 68, "M")
    for cx, cy, rx, ry in ((20, 13, 17, 8), (55, 13, 18, 9), (86, 15, 16, 8),
                           (23, 34, 19, 10), (59, 35, 21, 11), (91, 37, 18, 10),
                           (39, 56, 23, 8), (81, 56, 25, 8)):
        m.carve_ellipse(cx, cy, rx, ry, "j")
    for x in (43, 78):
        m.carve_tunnel((x, 0), (x + 2, 62), 2.6, "j")
    for a, b in (((20, 13), (55, 13)), ((55, 13), (86, 15)), ((20, 13), (23, 34)),
                 ((55, 13), (59, 35)), ((86, 15), (91, 37)), ((23, 34), (59, 35)),
                 ((59, 35), (91, 37)), ((23, 34), (39, 56)), ((59, 35), (39, 56)),
                 ((59, 35), (81, 56)), ((91, 37), (81, 56))):
        m.carve_tunnel(a, b, 2.4, "j")
    m.paint_exposed("J")
    # Two heavy Mahogany arches make the root network legible at a glance.
    m.draw_line((30, 44), (48, 29), 1.2, "H")
    m.draw_line((48, 29), (67, 44), 1.2, "H")
    m.draw_line((55, 51), (72, 39), 1.0, "H")
    m.draw_line((72, 39), (88, 50), 1.0, "H")
    m.pool(6, 34, 37, 42, "w")
    m.pool(25, 54, 58, 64, "w")
    m.pool(62, 73, 41, 46, "Y")
    for x, y1, y2 in ((43, 2, 31), (78, 3, 35), (25, 23, 48), (92, 19, 48)):
        m.rope(x, y1, y2)
    for x1, x2, y in ((38, 47, 15), (73, 82, 20), (19, 29, 30), (49, 59, 39),
                      (83, 94, 43), (32, 43, 55), (72, 84, 57)):
        m.platforms(x1, x2, y)
    # A large, sealed Temple slice with visible internal levels.
    m.fill_rect(100, 17, 119, 53, "X")
    m.carve_rect(103, 20, 116, 49, "x")
    for floor_y in (29, 39):
        m.ledge(103, 116, floor_y, "X")
    m.draw_line((110, 20), (110, 27), 0.6, "X")
    m.draw_line((108, 31), (108, 37), 0.6, "X")
    m.draw_line((112, 41), (112, 49), 0.6, "X")
    for x, y in ((17, 12), (34, 33), (53, 12), (65, 34), (87, 14), (89, 36),
                 (107, 25), (114, 35), (105, 45), (39, 55), (80, 54)):
        m.torch(x, y)
    return scene_toml(
        "Jungle root network: cenotes, wet basins, and sealed Temple", 73193, m,
        sky="Background_154", used=set("MJHXjxswY=|*"),
        notes=("120 x 68 cells. Eight major basins form a looping underground network.",
               "Two surface cenotes, dry ledges, water routes, and a honey pocket offer distinct paths.",
               "The Temple remains sealed while its large multi-level interior stays visible."),
    )


def evil_hallow_depths() -> str:
    m = SceneMap(120, 68, "S")
    m.fill_rect(0, 0, 39, 67, "E")
    m.fill_rect(81, 0, 119, 67, "P")
    for cx, cy, rx, ry, wall in ((16, 13, 13, 8, "e"), (31, 31, 12, 9, "e"),
                                 (15, 51, 14, 10, "e"), (54, 16, 15, 8, "s"),
                                 (61, 36, 18, 10, "s"), (52, 56, 18, 8, "s"),
                                 (94, 14, 16, 9, "p"), (101, 34, 15, 10, "p"),
                                 (91, 55, 19, 9, "p")):
        m.carve_ellipse(cx, cy, rx, ry, wall)
    for x, wall in ((11, "e"), (31, "e"), (91, "p"), (109, "p")):
        m.carve_tunnel((x, 0), (x + 3, 66), 2.2, wall)
    for a, b, wall in (((16, 13), (31, 31), "e"), ((31, 31), (15, 51), "e"),
                       ((31, 31), (54, 16), "s"), ((31, 31), (61, 36), "s"),
                       ((15, 51), (52, 56), "s"), ((54, 16), (94, 14), "s"),
                       ((61, 36), (101, 34), "s"), ((52, 56), (91, 55), "s"),
                       ((94, 14), (101, 34), "p"), ((101, 34), (91, 55), "p")):
        m.carve_tunnel(a, b, 2.2, wall)
    m.paint_exposed("E", lambda x, _y: x < 41)
    m.paint_exposed("P", lambda x, _y: x > 79)
    for x1, x2, y in ((6, 15, 18), (25, 35, 28), (10, 20, 48), (45, 58, 21),
                      (52, 67, 39), (43, 57, 55), (87, 97, 18), (96, 108, 36),
                      (83, 94, 54)):
        m.platforms(x1, x2, y)
    for x, y1, y2 in ((11, 3, 31), (31, 16, 52), (61, 17, 57), (91, 3, 38), (109, 17, 60)):
        m.rope(x, y1, y2)
    m.pool(3, 34, 56, 64, "w")
    m.pool(86, 115, 58, 64, "w")
    for x, y in ((88, 13), (98, 11), (104, 31), (111, 28), (86, 52), (96, 49), (107, 55)):
        m.put_object(x, y, "C")
    for x, y in ((15, 12), (29, 29), (16, 50), (53, 15), (63, 34), (50, 55),
                 (94, 13), (102, 33), (91, 54)):
        m.torch(x, y)
    return scene_toml(
        "Evil and Hallow depths: opposed faults around a neutral cavern spine", 98117, m,
        sky="Background_69", used=set("SEPespswC=|*"),
        notes=("120 x 68 cells. Corruption and Hallow each occupy a full-height province.",
               "Two cross-world loops survive conversion through the neutral stone middle.",
               "Chasms have ledges and side passages; crystal chimneys remain climbable."),
    )


def glowing_mushroom_basin() -> str:
    m = SceneMap(120, 68, "S")
    m.fill_ellipse(60, 43, 57, 29, "M", only_solid=True)
    for cx, cy, rx, ry in ((60, 40, 52, 24), (60, 14, 18, 13), (20, 25, 14, 8),
                           (101, 27, 13, 9), (21, 52, 15, 8), (100, 53, 15, 8)):
        m.carve_ellipse(cx, cy, rx, ry, "m")
    m.carve_tunnel((60, 0), (60, 40), 3.0, "m")
    m.carve_tunnel((20, 25), (42, 37), 2.5, "m")
    m.carve_tunnel((101, 27), (79, 37), 2.5, "m")
    m.carve_tunnel((21, 52), (43, 49), 2.3, "m")
    m.carve_tunnel((100, 53), (78, 49), 2.3, "m")
    m.paint_exposed("G", lambda x, y: 5 < x < 115 and y > 8)
    m.pool(38, 82, 51, 62, "w")
    m.ledge(9, 36, 48, "G")
    m.ledge(84, 111, 48, "G")
    m.ledge(44, 55, 37, "G")
    m.ledge(67, 77, 35, "G")
    m.platforms(35, 45, 43)
    m.platforms(76, 87, 42)
    m.rope(60, 2, 32)
    m.rope(32, 31, 48)
    m.rope(90, 31, 48)
    mushrooms = ((14, 47, 40, 0), (25, 47, 36, 1), (48, 36, 24, 2),
                 (72, 34, 21, 0), (94, 47, 35, 1), (106, 47, 40, 2),
                 (54, 49, 42, 1), (68, 49, 40, 2))
    entity_lines: list[str] = []
    for x, floor, top, variant in mushrooms:
        for y in range(top, floor):
            m.put_object(x, y, "T")
        entity_lines.append(f'''\n[[entities]]\nname = "giant glowing mushroom {x}"\nasset = "Shroom_Tops"\nat = [{x}, {top + 1}]\nunits = "tiles"\nsource = [{variant * 62}, 0, 62, 44]\nanchor = "bottom-center"\nbrightness = 1.15\nz = 180\n''')
    for x, y in ((16, 24), (40, 39), (59, 29), (79, 37), (103, 26), (21, 50), (99, 51)):
        m.torch(x, y)
    return scene_toml(
        "Glowing Mushroom basin: giant chamber, dry rim, and spore lake", 26003, m,
        sky="Background_69", used=set("SMGmswT=|*"), entities="".join(entity_lines),
        notes=("120 x 68 cells. The basin spans almost the full scene and has a tall central chimney.",
               "Two side loops and an elevated dry rim keep the lake from becoming a route lock.",
               "Eight composed Giant Glowing Mushrooms use exact installed Terraria frames."),
    )


def cavern_stone_province() -> str:
    m = SceneMap(140, 72, "S")
    m.fill_ellipse(22, 20, 20, 16, "D", only_solid=True)
    m.fill_ellipse(112, 48, 26, 20, "D", only_solid=True)
    for cx, cy, rx, ry in ((20, 14, 16, 7), (58, 13, 21, 8), (109, 14, 20, 8),
                           (27, 35, 21, 10), (72, 35, 24, 11), (119, 36, 17, 10),
                           (20, 59, 17, 8), (58, 58, 19, 9), (103, 58, 27, 8)):
        m.carve_ellipse(cx, cy, rx, ry, "s")
    for x in (42, 87, 126):
        m.carve_tunnel((x, 0), (x + 3, 70), 2.5, "s")
    for a, b in (((20, 14), (58, 13)), ((58, 13), (109, 14)), ((20, 14), (27, 35)),
                 ((58, 13), (72, 35)), ((109, 14), (119, 36)), ((27, 35), (72, 35)),
                 ((72, 35), (119, 36)), ((27, 35), (20, 59)), ((27, 35), (58, 58)),
                 ((72, 35), (58, 58)), ((72, 35), (103, 58)), ((119, 36), (103, 58))):
        m.carve_tunnel(a, b, 2.3, "s")
    for cx, cy, rx, ry, mat in ((8, 27, 5, 3, "1"), (48, 27, 5, 4, "2"),
                                (83, 8, 5, 3, "3"), (130, 25, 5, 4, "4"),
                                (8, 66, 4, 3, "5"), (40, 66, 4, 3, "6"),
                                (87, 66, 4, 3, "7"), (132, 64, 4, 3, "8")):
        m.fill_ellipse(cx, cy, rx, ry, mat, only_solid=True)
    m.pool(6, 42, 38, 45, "w")
    m.pool(92, 131, 61, 68, "L")
    for x1, x2, y in ((36, 46, 17), (81, 91, 19), (120, 131, 23), (21, 33, 34),
                      (62, 76, 38), (108, 122, 38), (39, 51, 55), (82, 96, 57)):
        m.platforms(x1, x2, y)
    for x, y1, y2 in ((42, 2, 39), (87, 3, 58), (126, 3, 47), (58, 42, 66)):
        m.rope(x, y1, y2)
    m.cabin(56, 47, 78, 58, floors=(53,))
    for x, y in ((19, 13), (55, 12), (106, 13), (28, 34), (70, 34), (117, 35),
                 (19, 58), (65, 51), (65, 56), (102, 57)):
        m.torch(x, y)
    return scene_toml(
        "Cavern stone province: halls, ore districts, and deep lava shelf", 55301, m,
        sky="Background_69", used=set("SDs12345678wL=|*Bb"),
        notes=("140 x 72 cells. Nine halls and three chimneys form several complete route loops.",
               "Ore pockets sit in solid mining ground between rooms instead of replacing traversal space.",
               "Water, a two-storey cabin, and a deep lava shelf divide the province into districts."),
    )


def underworld_districts() -> str:
    m = SceneMap(140, 72, "A")
    m.carve_rect(0, 8, 139, 58, ".")
    # Ceiling teeth and lower ash islands divide the layer vertically.
    for x, depth in ((8, 8), (25, 13), (47, 9), (70, 16), (93, 10), (117, 15), (134, 9)):
        m.draw_line((x, 7), (x, 7 + depth), 2.1, "A")
    for x, top, radius in ((8, 49, 7), (31, 54, 9), (52, 48, 7), (75, 54, 10),
                           (101, 49, 8), (127, 53, 9)):
        m.fill_ellipse(x, top + radius, radius, radius, "A")
    # The long fight route has deliberate gaps, each crossed by short platforms.
    m.ledge(0, 139, 33, "A", thickness=2)
    for x1, x2 in ((18, 23), (57, 63), (96, 103), (124, 130)):
        m.carve_rect(x1, 32, x2, 35, ".")
        m.platforms(x1, x2, 33)
    # Vertical travel shafts connect the fight shelf to upper and lower districts.
    for x in (15, 44, 70, 111, 132):
        m.carve_tunnel((x, 8), (x, 57), 1.6, ".")
        m.rope(x, 9, 55)
    # Lava deltas stay below the main route, with obsidian at their edges.
    m.carve_rect(0, 49, 139, 67, ".")
    for x, top, radius in ((6, 55, 7), (29, 58, 8), (49, 53, 6), (73, 58, 9),
                           (99, 54, 7), (124, 58, 9)):
        m.fill_ellipse(x, top + radius, radius, radius, "A")
    m.pool(0, 139, 55, 70, "L")
    m.ledge(0, 17, 53, "O")
    m.ledge(35, 53, 51, "O")
    m.ledge(86, 104, 52, "O")
    m.ledge(124, 139, 51, "O")
    # Large Ruined House districts sit above, beside, and below the main corridor.
    # They are placed after the lava delta so the low district remains intact.
    m.cabin(5, 12, 28, 26, block="K", wall="h", floors=(19,))
    m.cabin(91, 10, 121, 26, block="F", wall="h", floors=(18,))
    m.cabin(52, 39, 83, 52, block="K", wall="h", floors=(45,))
    for cx, cy in ((4, 65), (38, 63), (90, 64), (134, 64)):
        m.fill_ellipse(cx, cy, 4, 3, "Q", only_solid=True)
    for x, y in ((9, 17), (21, 24), (96, 16), (113, 24), (58, 47), (77, 54),
                 (4, 31), (35, 31), (72, 31), (108, 31), (136, 31)):
        m.torch(x, y)
    return scene_toml(
        "Underworld districts: vertical ruins above a broken fight corridor", 66029, m,
        sky="Background_129", used=set("AKFOQhL=|*"),
        notes=("140 x 72 cells. Three large multi-level Ruined House districts occupy separate heights.",
               "A long open fight corridor crosses the scene, broken by four short platform spans.",
               "Ropes and shafts connect ceiling ruins, the combat route, obsidian shelves, and lava deltas."),
    )


SCENES = {
    "frozen-fissures": frozen_fissures,
    "jungle-root-network": jungle_root_network,
    "evil-hallow-depths": evil_hallow_depths,
    "glowing-mushroom-basin": glowing_mushroom_basin,
    "cavern-stone-province": cavern_stone_province,
    "underworld-districts": underworld_districts,
}


def main() -> None:
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    for slug, build in SCENES.items():
        path = SOURCE_DIR / f"{slug}.toml"
        path.write_text(build(), encoding="utf-8")
        print(path.relative_to(ROOT))


if __name__ == "__main__":
    main()
