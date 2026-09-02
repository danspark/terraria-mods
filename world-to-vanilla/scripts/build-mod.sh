#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
tml_install="${TML_INSTALL:-$HOME/.local/share/Steam/steamapps/common/tModLoader}"
build_save_dir="${WORLD_TO_VANILLA_BUILD_SAVE_DIR:-$mod_dir/.build-save}"
package="$build_save_dir/Mods/WorldToVanilla.tmod"
tml_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
installed_package="$tml_save_dir/Mods/WorldToVanilla.tmod"

if [[ ! -f "$tml_install/tMLMod.targets" || ! -f "$tml_install/tModLoader.dll" ]]; then
	printf 'tModLoader was not found at %s\n' "$tml_install" >&2
	exit 1
fi

"$script_dir/install-mod-source.sh"

dotnet run \
	--project "$mod_dir/tests/WorldToVanilla.Tests.csproj" \
	--configuration Release

stage_dir="$(mktemp -d)"
trap 'rm -rf "$stage_dir"' EXIT
ln -s "$mod_dir" "$stage_dir/WorldToVanilla"
mkdir -p "$build_save_dir/Mods"

dotnet build "$stage_dir/WorldToVanilla/WorldToVanilla.csproj" \
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
