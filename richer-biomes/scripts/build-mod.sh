#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
tml_install="${TML_INSTALL:-$HOME/.local/share/Steam/steamapps/common/tModLoader}"
tml_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
package="$tml_save_dir/Mods/RicherBiomes.tmod"

if [[ ! -f "$tml_install/tMLMod.targets" || ! -f "$tml_install/tModLoader.dll" ]]; then
	printf 'tModLoader was not found at %s\n' "$tml_install" >&2
	exit 1
fi

stage_dir="$(mktemp -d)"
trap 'rm -rf "$stage_dir"' EXIT
ln -s "$mod_dir" "$stage_dir/RicherBiomes"

dotnet build "$stage_dir/RicherBiomes/RicherBiomes.csproj" \
	--configuration Release \
	-p:TML_INSTALL="$tml_install"

if [[ ! -f "$package" ]]; then
	printf 'The build completed but %s was not created.\n' "$package" >&2
	exit 1
fi

printf '%s\n' "$package"
