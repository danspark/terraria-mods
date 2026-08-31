from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image


TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))

import terraria_scene as scene_tool  # noqa: E402


class SceneParserTests(unittest.TestCase):
    def test_example_parses(self) -> None:
        scene = scene_tool.load_scene(TOOL_DIR / "examples/vertical-forest.toml")

        self.assertEqual((scene.width, scene.height), (64, 34))
        self.assertEqual(scene.palette["G"].asset, "Tiles_2")
        self.assertEqual(scene.layers["objects"][15][23], "=")

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
