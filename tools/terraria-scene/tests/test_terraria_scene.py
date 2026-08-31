from __future__ import annotations

import sys
import tempfile
import unittest
from dataclasses import replace
from pathlib import Path

from PIL import Image


TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))

import terraria_scene as scene_tool  # noqa: E402


class SceneParserTests(unittest.TestCase):
    def test_example_parses(self) -> None:
        scene = scene_tool.load_scene(TOOL_DIR / "examples/vertical-forest.toml")

        self.assertEqual((scene.width, scene.height), (64, 34))
        self.assertEqual(scene.boundary, "world")
        self.assertEqual(scene.palette["G"].asset, "Tiles_2")
        self.assertEqual(scene.layers["objects"][15][23], "=")

    def test_invalid_boundary_is_rejected(self) -> None:
        source = """\
format = 1
[canvas]
boundary = "wrap"
[palette]
D = "dirt"
[map]
terrain = '''
D
'''
"""
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "invalid.toml"
            path.write_text(source, encoding="utf-8")

            with self.assertRaisesRegex(scene_tool.SceneError, "canvas.boundary"):
                scene_tool.load_scene(path)

    def test_undefined_symbol_reports_its_cell(self) -> None:
        source = """\
format = 1
[palette]
D = "dirt"
[map]
terrain = """
        source += "'''\nD?\nDD\n'''\n"
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "invalid.toml"
            path.write_text(source, encoding="utf-8")

            with self.assertRaisesRegex(scene_tool.SceneError, "undefined symbol.*2,1"):
                scene_tool.load_scene(path)


class FramingTests(unittest.TestCase):
    def test_standard_block_frames_match_terraria_layout(self) -> None:
        block_frame = scene_tool._block_frame

        self.assertEqual(block_frame(True, True, True, True, True, True, True, True, 1), (2, 1))
        self.assertEqual(block_frame(False, True, True, True, False, False, True, True, 2), (3, 0))
        self.assertEqual(block_frame(True, True, False, False, False, False, False, False, 0), (5, 0))
        self.assertEqual(block_frame(False, False, False, False, False, False, False, False, 2), (11, 3))

    def test_wall_frames_use_terrarias_wall_lookup(self) -> None:
        self.assertEqual(scene_tool._wall_frame(0, 4, 5, 1), (10, 3))
        self.assertEqual(scene_tool._wall_frame(15, 0, 0, 2), (8, 2))
        self.assertEqual(scene_tool._wall_frame(15, 1, 1, 1), (7, 1))

    def test_platform_frames_follow_neighbor_topology(self) -> None:
        platform_frame = scene_tool._platform_frame

        self.assertEqual(platform_frame("platform", "platform"), (0, 0))
        self.assertEqual(platform_frame("platform", "empty"), (1, 0))
        self.assertEqual(platform_frame("empty", "platform"), (2, 0))
        self.assertEqual(platform_frame("solid", "platform"), (3, 0))
        self.assertEqual(platform_frame("platform", "solid"), (4, 0))
        self.assertEqual(platform_frame("empty", "empty"), (5, 0))
        self.assertEqual(platform_frame("solid", "empty"), (6, 0))
        self.assertEqual(platform_frame("empty", "solid"), (7, 0))

    def test_slopes_use_two_pixel_terraria_steps(self) -> None:
        tile = Image.new("RGBA", (16, 16), (255, 255, 255, 255))

        rising_right = scene_tool._apply_shape(tile, "/")
        rising_left = scene_tool._apply_shape(tile, "\\")

        self.assertEqual(rising_right.getpixel((0, 13))[3], 0)
        self.assertEqual(rising_right.getpixel((0, 14))[3], 255)
        self.assertEqual(rising_right.getpixel((15, 0))[3], 255)
        self.assertEqual(rising_left.getpixel((0, 0))[3], 255)
        self.assertEqual(rising_left.getpixel((15, 13))[3], 0)
        self.assertEqual(rising_left.getpixel((15, 14))[3], 255)

    def test_background_ground_row_ignores_transparent_scenery(self) -> None:
        layer = Image.new("RGBA", (20, 8), (0, 0, 0, 0))
        layer.paste((10, 80, 40, 255), (0, 3, 20, 8))

        self.assertEqual(scene_tool._background_ground_row(layer), 3)

    def test_world_boundary_continues_but_open_boundary_exposes_edges(self) -> None:
        scene = scene_tool.load_scene(TOOL_DIR / "examples/vertical-forest.toml")
        world_renderer = scene_tool.Renderer(scene, None)
        open_renderer = scene_tool.Renderer(replace(scene, boundary="open"), None)

        self.assertTrue(world_renderer._terrain_connected(0, 16, -1, 0, "solid"))
        self.assertFalse(open_renderer._terrain_connected(0, 16, -1, 0, "solid"))
        self.assertTrue(world_renderer._terrain_connected(0, 33, 0, 1, "solid"))
        self.assertFalse(open_renderer._terrain_connected(0, 33, 0, 1, "solid"))


class RenderTests(unittest.TestCase):
    def test_png_asset_directory_renders_without_xnb_tools(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            assets = root / "assets"
            assets.mkdir()
            Image.new("RGBA", (8, 32), (90, 160, 220, 255)).save(assets / "Background_0.png")
            Image.new("RGBA", (288, 270), (130, 90, 50, 255)).save(assets / "Tiles_0.png")

            source = root / "scene.toml"
            source.write_text(
                """\
format = 1
[canvas]
scale = 1
background = "sky"
[palette]
D = "dirt"
[map]
terrain = '''
DDD
DDD
DDD
'''
""",
                encoding="utf-8",
            )
            output = root / "out.png"

            image = scene_tool.render_scene(source, output, assets_path=assets)

            self.assertEqual(image.size, (48, 48))
            self.assertTrue(output.is_file())
            self.assertEqual(image.getpixel((24, 24)), (130, 90, 50, 255))

    def test_forest_tree_uses_root_frames_at_its_base(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            assets = root / "assets"
            assets.mkdir()
            Image.new("RGBA", (8, 32), (90, 160, 220, 255)).save(assets / "Background_0.png")
            Image.new("RGBA", (288, 270), (130, 90, 50, 255)).save(assets / "Tiles_0.png")
            Image.new("RGBA", (288, 270), (90, 170, 70, 255)).save(assets / "Tiles_2.png")

            trunks = Image.new("RGBA", (110, 198), (0, 0, 0, 0))
            for frame_row in range(3):
                trunks.paste((80, 45, 25, 255), (0, frame_row * 22, 20, frame_row * 22 + 20))
            for frame_column in range(5):
                for frame_row in range(6, 9):
                    left = frame_column * 22
                    top = frame_row * 22
                    trunks.paste((240, 40, 20, 255), (left, top, left + 20, top + 20))
            trunks.save(assets / "Tiles_5.png")
            Image.new("RGBA", (84, 126), (0, 0, 0, 0)).save(assets / "Tree_Branches_0.png")
            Image.new("RGBA", (246, 82), (0, 0, 0, 0)).save(assets / "Tree_Tops_0.png")

            source = root / "tree.toml"
            source.write_text(
                """\
format = 1
[canvas]
scale = 1
background = "sky"
[palette]
D = "dirt"
G = "grass"
T = "forest-tree"
[map]
terrain = '''
.....
.....
.....
.....
.....
.....
.....
.GGG.
DDDDD
'''
objects = '''
.....
.....
.....
.....
.....
.....
..T..
.....
.....
'''
""",
                encoding="utf-8",
            )

            image = scene_tool.render_scene(source, root / "tree.png", assets_path=assets)

            self.assertEqual(image.getpixel((40, 104)), (240, 40, 20, 255))
            side_root_pixels = {image.getpixel((24, 104)), image.getpixel((56, 104))}
            self.assertIn((240, 40, 20, 255), side_root_pixels)

    def test_owned_xnb_texture_decodes_when_terraria_is_installed(self) -> None:
        try:
            assets = scene_tool.AssetStore(None)
        except scene_tool.SceneError:
            self.skipTest("Terraria is not installed in a standard location")

        tile_sheet = assets.load("Tiles_0")

        self.assertEqual(tile_sheet.size, (288, 270))
        self.assertGreater(tile_sheet.getchannel("A").getbbox()[2], 0)


if __name__ == "__main__":
    unittest.main()
