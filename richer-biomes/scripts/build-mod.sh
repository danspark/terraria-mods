#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
tml_install="${TML_INSTALL:-$HOME/.local/share/Steam/steamapps/common/tModLoader}"
build_save_dir="${RICHER_BIOMES_BUILD_SAVE_DIR:-$mod_dir/.playtest/build-save}"
package="$build_save_dir/Mods/RicherBiomes.tmod"
local_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
installed_package="$local_save_dir/Mods/RicherBiomes.tmod"

if [[ ! -f "$tml_install/tMLMod.targets" || ! -f "$tml_install/tModLoader.dll" ]]; then
	printf 'tModLoader was not found at %s\n' "$tml_install" >&2
	exit 1
fi

"$script_dir/install-mod-source.sh"

stage_dir="$(mktemp -d)"
trap 'rm -rf "$stage_dir"' EXIT
ln -s "$mod_dir" "$stage_dir/RicherBiomes"
mkdir -p "$build_save_dir/Mods"

dotnet build "$stage_dir/RicherBiomes/RicherBiomes.csproj" \
	--configuration Release \
	-p:TML_INSTALL="$tml_install" \
	-p:ExtraBuildModFlags="-tmlsavedirectory \"$build_save_dir\""

if [[ ! -f "$package" ]]; then
	printf 'The build completed but %s was not created.\n' "$package" >&2
	exit 1
fi

mkdir -p "$(dirname "$installed_package")"
install -m 0644 "$package" "$installed_package"

if ! cmp -s "$package" "$installed_package"; then
	printf 'The installed package does not match the completed build: %s\n' "$installed_package" >&2
	exit 1
fi

printf 'Built package: %s\n' "$package"
printf 'Installed package: %s\n' "$installed_package"
