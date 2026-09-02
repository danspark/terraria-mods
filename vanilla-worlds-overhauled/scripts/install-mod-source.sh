#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
local_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
mod_sources_dir="$local_save_dir/ModSources"
installed_source="$mod_sources_dir/VanillaWorldsOverhauled"
legacy_source="$mod_sources_dir/RicherBiomes"
legacy_mod_dir="$(dirname "$mod_dir")/richer-biomes"

mkdir -p "$mod_sources_dir"

if [[ -L "$legacy_source" ]] && [[ "$(readlink -m "$legacy_source")" == "$legacy_mod_dir" ]]; then
	rm -- "$legacy_source"
fi

if [[ -L "$installed_source" ]]; then
	linked_source="$(readlink -f "$installed_source" || true)"
	if [[ "$linked_source" != "$mod_dir" ]]; then
		printf 'Refusing to replace source link %s -> %s\n' "$installed_source" "$(readlink "$installed_source")" >&2
		exit 1
	fi
elif [[ -e "$installed_source" ]]; then
	printf 'Refusing to replace existing source path: %s\n' "$installed_source" >&2
	exit 1
else
	ln -s "$mod_dir" "$installed_source"
fi

printf 'Installed mod source: %s -> %s\n' "$installed_source" "$mod_dir"
