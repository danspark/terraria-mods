#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
default_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
playtest_dir="${VANILLA_WORLDS_OVERHAULED_PLAYTEST_DIR:-$mod_dir/.playtest}"
world_mode="classic"
world_size="large"
world_seed="VanillaWorldsOverhauled-Playtest-001"

while (( $# > 0 )); do
	case "$1" in
		--classic)
			world_mode="classic"
			shift
			;;
		--journey)
			world_mode="journey"
			shift
			;;
		--size)
			world_size="${2:-}"
			shift 2
			;;
		--seed)
			world_seed="${2:-}"
			shift 2
			;;
		--help|-h)
			printf 'Usage: %s [--classic|--journey] [--size small|medium|large] [--seed value]\n' "$0"
			exit 0
			;;
		*)
			printf 'Unknown argument: %s\n' "$1" >&2
			exit 2
			;;
	esac
done

case "$world_size" in
	small|medium|large)
		;;
	*)
		printf 'Invalid world size: %s\n' "$world_size" >&2
		exit 2
		;;
esac

safe_seed="$(printf '%s' "$world_seed" | tr -cs 'A-Za-z0-9' '_' | cut -c1-40)"
world_basename="Vanilla_Worlds_Overhauled_${world_size}_${world_mode}_${safe_seed}"

source_world="$playtest_dir/Worlds/$world_basename.wld"
source_sidecar="${source_world%.wld}.twld"
source_mod="$playtest_dir/Mods/VanillaWorldsOverhauled.tmod"
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
cp -f "$source_mod" "$default_save_dir/Mods/VanillaWorldsOverhauled.tmod"
rm -f -- "$default_save_dir/Mods/RicherBiomes.tmod"
cp "$source_world" "$target_world"
cp "$source_sidecar" "${target_world%.wld}.twld"

printf 'Installed mod: %s\n' "$default_save_dir/Mods/VanillaWorldsOverhauled.tmod"
printf 'Installed %s world: %s\n' "$world_mode" "$target_world"
