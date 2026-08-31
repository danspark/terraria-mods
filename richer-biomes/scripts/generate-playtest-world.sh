#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
tml_install="${TML_INSTALL:-$HOME/.local/share/Steam/steamapps/common/tModLoader}"
default_save_dir="${TML_SAVE_DIRECTORY:-$HOME/.local/share/Terraria/tModLoader}"
playtest_dir="${RICHER_BIOMES_PLAYTEST_DIR:-$mod_dir/.playtest}"
world_dir="$playtest_dir/Worlds"
source_package="$default_save_dir/Mods/RicherBiomes.tmod"
server_log="$tml_install/tModLoader-Logs/server.log"
world_mode="classic"

case "${1:-}" in
	--classic|'')
		;;
	--journey)
		world_mode="journey"
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

if [[ "$world_mode" == "journey" ]]; then
	world_basename="Richer_Biomes_Playtest_Journey"
	world_name="Richer Biomes Playtest Journey"
	world_difficulty=3
else
	world_basename="Richer_Biomes_Playtest_Large"
	world_name="Richer Biomes Playtest Large"
	world_difficulty=0
fi

world_file="$world_dir/$world_basename.wld"
console_log="$playtest_dir/generation-console-$world_mode.log"

"$script_dir/build-mod.sh"

mkdir -p "$playtest_dir/Mods" "$world_dir" "$playtest_dir/Logs"
cp -f "$source_package" "$playtest_dir/Mods/RicherBiomes.tmod"
printf '[\n  "RicherBiomes"\n]\n' > "$playtest_dir/Mods/enabled.json"

if [[ -f "$world_file" ]]; then
	printf 'Refusing to overwrite the existing playtest world: %s\n' "$world_file" >&2
	printf 'Move it aside or set RICHER_BIOMES_PLAYTEST_DIR to a new directory.\n' >&2
	exit 2
fi

fifo_dir="$(mktemp -d)"
input_fifo="$fifo_dir/server-input"
server_config="$fifo_dir/serverconfig.txt"
mkfifo "$input_fifo"
trap 'rm -rf "$fifo_dir"' EXIT

printf '%s\n' \
	"world=$world_file" \
	'autocreate=3' \
	'seed=RicherBiomes-Playtest-001' \
	"worldname=$world_name" \
	"difficulty=$world_difficulty" \
	'maxplayers=1' \
	'port=7779' \
	'upnp=0' > "$server_config"

(
	cd "$tml_install"
	timeout 15m dotnet ./tModLoader.dll \
		-server \
		-nosteam \
		-tmlsavedirectory "$playtest_dir" \
		-modpath "$playtest_dir/Mods" \
		-config "$server_config" < "$input_fifo" > "$console_log" 2>&1
) &
server_pid=$!

exec 3> "$input_fifo"
while kill -0 "$server_pid" 2>/dev/null; do
	if grep -q 'Server started' "$console_log" 2>/dev/null; then
		printf 'exit\n' >&3
		break
	fi
	sleep 1
done
exec 3>&-

if ! wait "$server_pid"; then
	tail -n 120 "$console_log" >&2
	exit 1
fi

generation_log="$playtest_dir/Logs/server-$world_mode-generation.log"
cp "$server_log" "$generation_log"

if [[ ! -s "$world_file" || ! -s "${world_file%.wld}.twld" ]]; then
	printf 'tModLoader did not create both world artifacts under %s\n' "$world_dir" >&2
	exit 1
fi

latest_log="$generation_log"
if [[ ! -s "$latest_log" ]]; then
	printf 'No tModLoader server log was copied to %s\n' "$latest_log" >&2
	exit 1
fi

if ! grep -q 'Richer Biomes validation passed' "$latest_log"; then
	printf 'The world exists, but route validation was not recorded in %s\n' "$latest_log" >&2
	exit 1
fi

if grep -q 'automatically disabled' "$latest_log"; then
	printf 'tModLoader disabled a mod during the generation run.\n' >&2
	exit 1
fi

if ! grep -q 'Generation of 8400x2400' "$latest_log"; then
	printf 'The generated world was not the required 8400x2400 large size.\n' >&2
	exit 1
fi

reload_console="$playtest_dir/reload-console-$world_mode.log"
if ! (
	cd "$tml_install"
	printf 'exit\n' | timeout 5m dotnet ./tModLoader.dll \
		-server \
		-nosteam \
		-tmlsavedirectory "$playtest_dir" \
		-modpath "$playtest_dir/Mods" \
		-world "$world_file" \
		-maxplayers 1 \
		-port 7779 \
		-noupnp
) > "$reload_console" 2>&1; then
	tail -n 120 "$reload_console" >&2
	exit 1
fi

reload_log="$playtest_dir/Logs/server-$world_mode-reload.log"
cp "$server_log" "$reload_log"
if ! grep -Eq "Loading World: .*Width: 8400, Height: 2400, .*GameMode: $world_difficulty" "$reload_log"; then
	printf 'The saved world did not reload as %s mode.\n' "$world_mode" >&2
	exit 1
fi
if ! grep -q 'Loaded Richer Biomes world metadata' "$reload_log"; then
	printf 'The saved world reloaded without Richer Biomes metadata.\n' >&2
	exit 1
fi

printf 'Validated %s large world: %s\n' "$world_mode" "$world_file"
printf 'Validation log: %s\n' "$latest_log"
