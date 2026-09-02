#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$(cd "$script_dir/.." && pwd)"
tml_install="${TML_INSTALL:-$HOME/.local/share/Steam/steamapps/common/tModLoader}"
playtest_dir="${RICHER_BIOMES_PLAYTEST_DIR:-$mod_dir/.playtest}"
build_save_dir="${RICHER_BIOMES_BUILD_SAVE_DIR:-$mod_dir/.playtest/build-save}"
source_package="$build_save_dir/Mods/RicherBiomes.tmod"
server_log="$tml_install/tModLoader-Logs/server.log"
world_mode="classic"
world_size="large"
world_seed="RicherBiomes-Playtest-001"
expected_evil=""

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
		--expect-evil)
			expected_evil="${2:-}"
			shift 2
			;;
		--help|-h)
			printf 'Usage: %s [--classic|--journey] [--size small|medium|large] [--seed value] [--expect-evil Corruption|Crimson]\n' "$0"
			exit 0
			;;
		*)
			printf 'Unknown argument: %s\n' "$1" >&2
			exit 2
			;;
	esac
done

case "$world_size" in
	small)
		autocreate=1
		expected_dimensions="4200x1200"
		expected_width=4200
		expected_height=1200
		;;
	medium)
		autocreate=2
		expected_dimensions="6400x1800"
		expected_width=6400
		expected_height=1800
		;;
	large)
		autocreate=3
		expected_dimensions="8400x2400"
		expected_width=8400
		expected_height=2400
		;;
	*)
		printf 'Invalid world size: %s\n' "$world_size" >&2
		exit 2
		;;
esac

case "$expected_evil" in
	''|Corruption|Crimson)
		;;
	*)
		printf 'Invalid expected evil: %s\n' "$expected_evil" >&2
		exit 2
		;;
esac

if [[ "$world_mode" == "journey" ]]; then
	world_difficulty=3
else
	world_difficulty=0
fi

safe_seed="$(printf '%s' "$world_seed" | tr -cs 'A-Za-z0-9' '_' | cut -c1-40)"
world_basename="Richer_Biomes_${world_size}_${world_mode}_${safe_seed}"
world_name="Richer Biomes ${world_size^} ${world_mode^}"
world_dir="$playtest_dir/Worlds"
world_file="$world_dir/$world_basename.wld"
console_log="$playtest_dir/generation-console-$world_size-$world_mode-$safe_seed.log"

"$script_dir/build-mod.sh"

mkdir -p "$playtest_dir/Mods" "$world_dir" "$playtest_dir/Logs"
cp -f "$source_package" "$playtest_dir/Mods/RicherBiomes.tmod"
printf '[\n  "RicherBiomes"\n]\n' > "$playtest_dir/Mods/enabled.json"

if [[ -f "$world_file" ]]; then
	printf 'Refusing to overwrite the existing playtest world: %s\n' "$world_file" >&2
	exit 2
fi

fifo_dir="$(mktemp -d)"
input_fifo="$fifo_dir/server-input"
server_config="$fifo_dir/serverconfig.txt"
mkfifo "$input_fifo"
trap 'rm -rf "$fifo_dir"' EXIT

printf '%s\n' \
	"world=$world_file" \
	"autocreate=$autocreate" \
	"seed=$world_seed" \
	"worldname=$world_name" \
	"difficulty=$world_difficulty" \
	'maxplayers=1' \
	'port=7779' \
	'upnp=0' > "$server_config"

printf -v generation_command '%q ' \
	dotnet ./tModLoader.dll \
	-server \
	-nosteam \
	-tmlsavedirectory "$playtest_dir" \
	-modpath "$playtest_dir/Mods" \
	-config "$server_config"

(
	cd "$tml_install"
	timeout 15m script -qefc "$generation_command" /dev/null < "$input_fifo" > "$console_log" 2>&1
) &
server_pid=$!

exec 3> "$input_fifo"
while kill -0 "$server_pid" 2>/dev/null; do
	if grep -q 'Server started' "$console_log" 2>/dev/null; then
		printf 'exit\n' >&3
		break
	fi
	if grep -Eq 'A problem was encountered during world generation|Unhandled Exception|Fatal Error' "$console_log" 2>/dev/null; then
		printf '\n' >&3
		break
	fi
	sleep 1
done
exec 3>&-

if ! wait "$server_pid"; then
	tail -n 160 "$console_log" >&2
	exit 1
fi

generation_log="$playtest_dir/Logs/server-$world_size-$world_mode-$safe_seed-generation.log"
cp "$server_log" "$generation_log"

if [[ ! -s "$world_file" || ! -s "${world_file%.wld}.twld" ]]; then
	printf 'tModLoader did not create both world artifacts under %s\n' "$world_dir" >&2
	exit 1
fi

if ! grep -q 'Richer Biomes validation passed' "$generation_log"; then
	printf 'The world exists, but Richer Biomes validation was not recorded.\n' >&2
	exit 1
fi
if grep -q 'automatically disabled' "$generation_log"; then
	printf 'tModLoader disabled a mod during the generation run.\n' >&2
	exit 1
fi
if ! grep -q "Generation of $expected_dimensions" "$generation_log"; then
	printf 'The generated world was not the requested %s size.\n' "$world_size" >&2
	exit 1
fi
if [[ -n "$expected_evil" ]] && ! grep -q "Generation of $expected_dimensions $expected_evil world" "$generation_log"; then
	printf 'The seed did not create the expected %s world.\n' "$expected_evil" >&2
	exit 1
fi

reload_console="$playtest_dir/reload-console-$world_size-$world_mode-$safe_seed.log"
printf -v reload_command '%q ' \
	dotnet ./tModLoader.dll \
	-server \
	-nosteam \
	-tmlsavedirectory "$playtest_dir" \
	-modpath "$playtest_dir/Mods" \
	-world "$world_file" \
	-maxplayers 1 \
	-port 7779 \
	-noupnp
if ! (
	cd "$tml_install"
	printf 'exit\n' | timeout 5m script -qefc "$reload_command" /dev/null
) > "$reload_console" 2>&1; then
	tail -n 160 "$reload_console" >&2
	exit 1
fi

reload_log="$playtest_dir/Logs/server-$world_size-$world_mode-$safe_seed-reload.log"
cp "$server_log" "$reload_log"
if ! grep -Eq "Loading World: .*Width: $expected_width, Height: $expected_height, .*GameMode: $world_difficulty" "$reload_log"; then
	printf 'The saved world did not reload with the requested size and mode.\n' >&2
	exit 1
fi
if ! grep -Eq 'Loaded Richer Biomes manifest v5: landmarks=11; mountains=[1-9][0-9]*; bridges=[1-9][0-9]*; skyHighlands=[1-9][0-9]*; mine=present; validation=valid=True' "$reload_log"; then
	printf 'The first reload did not recover the complete Richer Biomes feature manifest.\n' >&2
	exit 1
fi

# A reload saves the world again on shutdown. Open that second-generation save
# once more so the test proves that metadata survives a full load/save cycle.
persistence_console="$playtest_dir/persistence-console-$world_size-$world_mode-$safe_seed.log"
if ! (
	cd "$tml_install"
	printf 'exit\n' | timeout 5m script -qefc "$reload_command" /dev/null
) > "$persistence_console" 2>&1; then
	tail -n 160 "$persistence_console" >&2
	exit 1
fi

persistence_log="$playtest_dir/Logs/server-$world_size-$world_mode-$safe_seed-persistence.log"
cp "$server_log" "$persistence_log"
if ! grep -Eq 'Loaded Richer Biomes manifest v5: landmarks=11; mountains=[1-9][0-9]*; bridges=[1-9][0-9]*; skyHighlands=[1-9][0-9]*; mine=present; validation=valid=True' "$persistence_log"; then
	printf 'The Richer Biomes feature manifest did not survive the reload/save/reload cycle.\n' >&2
	exit 1
fi

printf 'Validated %s %s world: %s\n' "$world_mode" "$world_size" "$world_file"
printf 'Validation log: %s\n' "$generation_log"
