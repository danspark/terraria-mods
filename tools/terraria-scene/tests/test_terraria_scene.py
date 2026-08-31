from __future__ import annotations

import json
import sys
import tempfile
import unittest
from dataclasses import replace
from pathlib import Path

from PIL import Image


TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))

import terraria_scene as scene_tool  # noqa: E402
from asset_catalog import discover_assets  # noqa: E402


class SceneParserTests(unittest.TestCase):
    def test_example_parses(self) -> None:
        scene = scene_tool.load_scene(TOOL_DIR / "examples/vertical-forest.toml")

        self.assertEqual((scene.width, scene.height), (64, 34))
        self.assertEqual(scene.boundary, "world")
        self.assertEqual(scene.palette["G"].asset, "Tiles_2")
        self.assertEqual(scene.layers["objects"][15][24], "=")

    def test_object_over_terrain_is_rejected(self) -> None:
        source = """\
format = 1
[palette]
D = "dirt"
"|" = "rope"
[map]
terrain = '''
D
'''
objects = '''
|
'''
"""
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "overlap.toml"
            path.write_text(source, encoding="utf-8")

            with self.assertRaisesRegex(
                scene_tool.SceneError,
                "places 'rope' over map.terrain sprite 'dirt' at 1,1",
            ):
                scene_tool.load_scene(path)

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

    def test_scene_size_and_scale_have_no_arbitrary_cap(self) -> None:
        width = 241
        height = 136
        terrain = "\n".join("." * width for _ in range(height))
        source = f'''\
format = 1
[canvas]
scale = 99
[map]
terrain = """
{terrain}
"""
'''
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "large.toml"
            path.write_text(source, encoding="utf-8")

            scene = scene_tool.load_scene(path)

        self.assertEqual((scene.width, scene.height), (width, height))
        self.assertEqual(scene.scale, 99)

        huge_source = '''\
format = 1
[canvas]
size = [1000000000, 1000000000]
background = "transparent"
'''
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "huge.toml"
            path.write_text(huge_source, encoding="utf-8")

            huge_scene = scene_tool.load_scene(path)

        self.assertEqual((huge_scene.width, huge_scene.height), (1000000000, 1000000000))

    def test_token_map_can_use_inline_sprites_and_an_external_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "terrain.map").write_text("soil soil .\nsoil rock rock\n", encoding="utf-8")
            source = root / "tokens.toml"
            source.write_text(
                '''\
format = 1
[palette.soil]
kind = "tile"
asset = "Tiles_0"
autotile = "fixed"

[palette.rock]
kind = "tile"
asset = "Tiles_1"
autotile = "fixed"

[map]
encoding = "tokens"
terrain = { file = "terrain.map", encoding = "tokens" }
''',
                encoding="utf-8",
            )

            scene = scene_tool.load_scene(source)

        self.assertEqual((scene.width, scene.height), (3, 2))
        self.assertEqual(scene.layers["terrain"][0], ("soil", "soil", "."))
        self.assertEqual(scene.palette["rock"].asset, "Tiles_1")

    def test_canvas_can_contain_entities_without_a_tile_map(self) -> None:
        source = '''\
format = 1
[canvas]
size = [5, 3]
background = "transparent"

[[entities]]
asset = "NPC_1"
at = [2.5, 2]
anchor = "bottom-center"
source = [0, 0, 16, 16]
'''
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "entities.toml"
            path.write_text(source, encoding="utf-8")

            scene = scene_tool.load_scene(path)

        self.assertEqual((scene.width, scene.height), (5, 3))
        self.assertEqual(scene.layers, {})
        self.assertEqual(scene.entities[0].position, (40, 32))


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

    def test_arbitrary_asset_entity_renders_a_source_rectangle(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            assets = root / "assets"
            (assets / "NPCs").mkdir(parents=True)
            sprite = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
            sprite.paste((200, 100, 50, 255), (2, 2, 6, 6))
            sprite.save(assets / "NPCs" / "Example.png")
            source = root / "entity.toml"
            source.write_text(
                '''\
format = 1
[canvas]
size = [3, 2]
scale = 1
background = "transparent"

[[entities]]
asset = "NPCs/Example"
at = [16, 16]
units = "pixels"
anchor = "center"
source = [2, 2, 4, 4]
scale = 2
flip_x = true
rotation = 90
opacity = 0.5
''',
                encoding="utf-8",
            )

            image = scene_tool.render_scene(source, root / "entity.png", assets_path=assets)

            self.assertEqual(image.size, (48, 32))
            self.assertEqual(image.getpixel((16, 16)), (200, 100, 50, 128))
            self.assertEqual(image.getpixel((11, 11)), (0, 0, 0, 0))

    def test_tiled_render_stitches_to_the_full_render(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            assets = root / "assets"
            assets.mkdir()
            Image.new("RGBA", (10, 6), (180, 70, 30, 255)).save(assets / "Any.png")
            source = root / "scene.toml"
            source.write_text(
                '''\
format = 1
[canvas]
size = [5, 3]
scale = 1
background = "transparent"

[[entities]]
asset = "Any"
at = [32, 20]
units = "pixels"
anchor = "center"
rotation = 17
''',
                encoding="utf-8",
            )
            full = scene_tool.render_scene(source, root / "full.png", assets_path=assets)
            manifest = scene_tool.render_scene_tiles(
                source,
                root / "tiles",
                assets_path=assets,
                tile_size=(2, 2),
            )
            stitched = Image.new("RGBA", full.size, (0, 0, 0, 0))
            for tile in manifest["tiles"]:
                tile_image = Image.open(root / "tiles" / tile["file"]).convert("RGBA")
                stitched.alpha_composite(tile_image, tuple(tile["pixel_origin"]))

            self.assertIsNone(scene_tool.ImageChops.difference(full, stitched).getbbox())
            saved_manifest = json.loads((root / "tiles" / "manifest.json").read_text())
            self.assertEqual(len(saved_manifest["tiles"]), 6)

    def test_small_region_renders_from_a_billion_cell_canvas(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            assets = root / "assets"
            assets.mkdir()
            Image.new("RGBA", (1, 1), (0, 0, 0, 0)).save(assets / "Unused.png")
            source = root / "huge.toml"
            source.write_text(
                '''\
format = 1
[canvas]
size = [1000000000, 1000000000]
scale = 1
background = "transparent"
''',
                encoding="utf-8",
            )

            image = scene_tool.render_scene(
                source,
                root / "region.png",
                assets_path=assets,
                region=scene_tool.RenderRegion(999999998, 999999998, 2, 2),
            )

        self.assertEqual(image.size, (32, 32))
        self.assertIsNone(image.getbbox())

    def test_owned_xnb_texture_decodes_when_terraria_is_installed(self) -> None:
        try:
            assets = scene_tool.AssetStore(None)
        except scene_tool.SceneError:
            self.skipTest("Terraria is not installed in a standard location")

        tile_sheet = assets.load("Tiles_0")

        self.assertEqual(tile_sheet.size, (288, 270))
        self.assertGreater(tile_sheet.getchannel("A").getbbox()[2], 0)


class AssetCatalogTests(unittest.TestCase):
    def test_catalog_recurses_and_prefers_exported_pngs(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            images = Path(temp_dir)
            (images / "NPCs").mkdir()
            (images / "Tiles_1.xnb").write_bytes(b"xnb")
            (images / "Tiles_1.png").write_bytes(b"png")
            (images / "NPCs" / "Guide.xnb").write_bytes(b"xnb")
            (images / "ignore.txt").write_text("ignored")

            records = discover_assets(images)

        self.assertEqual([record.name for record in records], ["NPCs/Guide", "Tiles_1"])
        self.assertEqual(records[0].category, "NPCs")
        self.assertEqual(records[1].format, "png")

    def test_owned_install_scan_verifies_every_discovered_xnb_texture(self) -> None:
        try:
            store = scene_tool.AssetStore(None)
        except scene_tool.SceneError:
            self.skipTest("Terraria is not installed in a standard location")

        records = discover_assets(store.images)
        dimensions = store.scan_xnb_dimensions()
        xnb_names = {record.name for record in records if record.format == "xnb"}

        self.assertEqual(set(dimensions), xnb_names)
        self.assertTrue(all(width > 0 and height > 0 for width, height in dimensions.values()))


if __name__ == "__main__":
    unittest.main()
