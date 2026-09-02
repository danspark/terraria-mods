#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
tml_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
sources_dir="$tml_save_dir/ModSources"
source_link="$sources_dir/WorldToVanilla"

mkdir -p "$sources_dir"

if [[ -L "$source_link" ]]; then
	linked_dir="$(readlink -f "$source_link")"
	if [[ "$linked_dir" == "$mod_dir" ]]; then
		printf 'Mod source link is current: %s\n' "$source_link"
		exit 0
	fi

	printf 'Refusing to replace source link %s, which points to %s\n' "$source_link" "$linked_dir" >&2
	exit 1
fi

if [[ -e "$source_link" ]]; then
	printf 'Refusing to replace existing source path: %s\n' "$source_link" >&2
	exit 1
fi

ln -s "$mod_dir" "$source_link"
printf 'Installed mod source link: %s -> %s\n' "$source_link" "$mod_dir"
