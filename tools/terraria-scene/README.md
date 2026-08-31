# Terraria scene renderer

`terraria_scene.py` turns aligned TOML text grids into a Terraria concept render. It reads textures from your installed copy of Terraria. The repository does not contain game art.

Use the renderer to compare terrain silhouettes, routes, cave openings, water, and landmark placement before you write world-generation code.

## Render the included forest study

Install Python 3.11 or newer. The first XNB render also needs the .NET 8 SDK.

From the repository root, enter the tool directory. Then create an environment and install Pillow:

```bash
cd tools/terraria-scene
python3 -m venv .venv
source .venv/bin/activate
python -m pip install -r requirements.txt
```

From this directory, validate the map:

```bash
python3 terraria_scene.py validate examples/vertical-forest.toml
```

The command prints:

```text
ok: Vertical forest route study (64x34 tiles)
```

Render the map:

```bash
python3 terraria_scene.py render examples/vertical-forest.toml \
  --output examples/vertical-forest.png
```

The renderer looks for the Linux Steam install in its standard locations. To use another location, pass the Terraria directory:

```bash
python3 terraria_scene.py render scene.toml \
  --assets /path/to/Terraria \
  --output scene.png
```

You can also set `TERRARIA_PATH`. If you already exported the textures, pass a directory that contains PNG files such as `Tiles_0.png` and `Background_0.png`.

## Create a terrain study

1. Copy `examples/vertical-forest.toml`.
2. Replace its palette with the materials that you need.
3. Edit `map.terrain`. Use one character for each Terraria tile and a dot for air.
4. Add same-sized `walls`, `liquids`, `objects`, or `shapes` grids when you need them.
5. Run `validate` after each edit.
6. Run `render` when the map passes validation.

Keep `seed` stable while you compare terrain changes. Change the seed to inspect other texture, tree, and frame variants.

Use `--grid` to show cell boundaries. Use `--scale 1` for a fast native-size render.

Read [FORMAT.md](FORMAT.md) for every field, built-in sprite, and custom sprite option.

## Run the checks

```bash
python3 -m unittest discover -s tests -v
```

The test suite parses the example, checks Terraria's standard block-frame lookup, renders from exported PNG textures, and decodes one owned XNB texture when Terraria is installed.

## Keep game art local

The tool caches decoded textures under `~/.cache/terraria-scene`. Generated example PNG files are ignored by Git. Do not commit or redistribute decoded sheets or renders that contain Terraria art.
