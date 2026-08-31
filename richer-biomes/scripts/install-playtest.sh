#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
default_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
playtest_dir="${RICHER_BIOMES_PLAYTEST_DIR:-$mod_dir/.playtest}"
world_mode="classic"

case "${1:-}" in
	--classic|'')
		world_basename="Richer_Biomes_Playtest_Large"
		;;
	--journey)
		world_mode="journey"
		world_basename="Richer_Biomes_Playtest_Journey"
		;;
	--help|-h)
		printf 'Usage: %s [--classic|--journey]\n' "$0"
		exit 0
		;;
	*)
		printf 'Usage: %s [--classic|--journey]\n' "$0" >&2
		exit 2
		;;
esac

if (( $# > 1 )); then
	printf 'Usage: %s [--classic|--journey]\n' "$0" >&2
	exit 2
fi

source_world="$playtest_dir/Worlds/$world_basename.wld"
source_sidecar="${source_world%.wld}.twld"
source_mod="$playtest_dir/Mods/RicherBiomes.tmod"
target_world="$default_save_dir/Worlds/$world_basename.wld"

for artifact in "$source_world" "$source_sidecar" "$source_mod"; do
	if [[ ! -s "$artifact" ]]; then
		printf 'Missing playtest artifact: %s\n' "$artifact" >&2
		exit 1
	fi
done

if [[ -e "$target_world" || -e "${target_world%.wld}.twld" ]]; then
	printf 'Refusing to overwrite an existing installed playtest world at %s\n' "$target_world" >&2
	exit 2
fi

mkdir -p "$default_save_dir/Mods" "$default_save_dir/Worlds"
cp -f "$source_mod" "$default_save_dir/Mods/RicherBiomes.tmod"
cp "$source_world" "$target_world"
cp "$source_sidecar" "${target_world%.wld}.twld"

printf 'Installed mod: %s\n' "$default_save_dir/Mods/RicherBiomes.tmod"
printf 'Installed %s world: %s\n' "$world_mode" "$target_world"
