# Export Worlds to Vanilla

One click. No file hunting.

Export Worlds to Vanilla adds a folder button to each row in tModLoader's world selection menu. Click it to copy that world's `.wld` file into vanilla Terraria's local `Worlds` folder.

The copied `.wld` contains the world state that Terraria can read. tModLoader keeps mod-owned data in a separate `.twld` file, so modded tiles, items, and other mod data are not available in vanilla Terraria.

The export keeps any existing vanilla files intact. If a file with the same name contains different data, the mod writes `<name>_tModLoader.wld`, followed by numbered names when needed. Clicking the button again detects an identical exported file and does not make another copy.

Steam Cloud tModLoader worlds are supported as sources. The destination is always vanilla Terraria's local `Worlds` folder, so the exported world appears as a local world in vanilla Terraria.

## Build and install

Run:

```bash
./scripts/build-mod.sh
```

The script runs the file-copy tests, links this directory into tModLoader's `ModSources`, builds the package, installs `WorldToVanilla.tmod` in the local tModLoader `Mods` directory, and verifies that the installed package matches the build output.
