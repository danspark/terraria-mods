#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
creator_source="$script_dir/test-character-creator"
tml_install="${TML_INSTALL:-$HOME/.local/share/Steam/steamapps/common/tModLoader}"
tml_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
character_mode="journey"

case "${1:-}" in
	--journey)
		shift
		;;
	--classic)
		character_mode="classic"
		shift
		;;
	--help|-h)
		printf 'Usage: %s [--journey|--classic] [character name]\n' "$0"
		printf 'Defaults: --journey and character name "god".\n'
		exit 0
		;;
	--*)
		printf 'Usage: %s [--journey|--classic] [character name]\n' "$0" >&2
		exit 2
		;;
esac

player_name="${1:-god}"

if (( $# > 1 )); then
	printf 'Usage: %s [--journey|--classic] [character name]\n' "$0" >&2
	exit 2
fi

if [[ -z "$player_name" || ${#player_name} -gt 20 ]]; then
	printf 'Character names must contain between 1 and 20 characters.\n' >&2
	exit 2
fi

if [[ "$player_name" == *'/'* || "$player_name" == *'\\'* ]]; then
	printf 'Character names cannot contain path separators.\n' >&2
	exit 2
fi

if [[ ! -f "$tml_install/tModLoader.dll" ]]; then
	printf 'Could not find tModLoader.dll under %s\n' "$tml_install" >&2
	exit 1
fi

run_dir="$(mktemp -d)"
stage_dir="$(mktemp -d)"
trap 'rm -rf "$run_dir" "$stage_dir"' EXIT

ln -s "$creator_source" "$stage_dir/TestCharacterCreator"

dotnet build "$stage_dir/TestCharacterCreator/TestCharacterCreator.csproj" \
	-c Release \
	-p:ExtraBuildModFlags="-tmlsavedirectory $run_dir"

creator_package="$run_dir/Mods/TestCharacterCreator.tmod"
if [[ ! -s "$creator_package" ]]; then
	printf 'The helper package was not produced at %s\n' "$creator_package" >&2
	exit 1
fi

printf '[\n  "TestCharacterCreator"\n]\n' > "$run_dir/Mods/enabled.json"

console_log="$run_dir/creator-console.log"
world_file="$run_dir/Worlds/Creator_Disposable.wld"

if ! (
	cd "$tml_install"
	printf 'exit\n' | \
		TEST_CHARACTER_NAME="$player_name" \
		TEST_CHARACTER_MODE="$character_mode" \
		TEST_CHARACTER_RESULT="$run_dir/test-character-result.txt" \
		timeout 5m dotnet ./tModLoader.dll \
			-server \
			-nosteam \
			-tmlsavedirectory "$run_dir" \
			-modpath "$run_dir/Mods" \
			-world "$world_file" \
			-autocreate 1 \
			-worldname "Character Creator Disposable" \
			-seed "TestCharacterCreator" \
			-difficulty 3 \
			-maxplayers 1 \
			-port 7781 \
			-noupnp
) > "$console_log" 2>&1; then
	tail -n 160 "$console_log" >&2
	exit 1
fi

mapfile -d '' generated_players < <(find "$run_dir/Players" -maxdepth 1 -type f -name '*.plr' -print0)
if (( ${#generated_players[@]} != 1 )); then
	printf 'Expected one generated .plr file, found %d.\n' "${#generated_players[@]}" >&2
	tail -n 160 "$console_log" >&2
	exit 1
fi

source_player="${generated_players[0]}"
source_sidecar="${source_player%.plr}.tplr"
if [[ ! -s "$source_sidecar" ]]; then
	printf 'The generated tModLoader sidecar is missing: %s\n' "$source_sidecar" >&2
	exit 1
fi

if [[ ! -s "$run_dir/test-character-result.txt" ]]; then
	printf 'The helper did not record a successful save/load verification.\n' >&2
	exit 1
fi

mkdir -p "$tml_save_dir/Players"
target_player="$tml_save_dir/Players/$(basename "$source_player")"
target_sidecar="${target_player%.plr}.tplr"
if [[ -e "$target_player" || -e "$target_sidecar" ]]; then
	printf 'Refusing to overwrite the existing character: %s\n' "$target_player" >&2
	exit 2
fi

cp "$source_player" "$target_player"
cp "$source_sidecar" "$target_sidecar"

printf 'Created %s test character: %s\n' "$character_mode" "$player_name"
printf 'Player save: %s\n' "$target_player"
if [[ "$character_mode" == "journey" ]]; then
	printf 'Godmode: enabled\n'
else
	printf 'Godmode: unavailable for Classic characters\n'
fi
printf 'Quick Mount: Drill Containment Unit (R by default)\n'
printf 'Light pet: Suspicious Looking Tentacle\n'
