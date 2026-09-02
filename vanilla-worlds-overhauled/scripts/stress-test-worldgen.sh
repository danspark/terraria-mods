#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
artifact_parent="${VANILLA_WORLDS_OVERHAULED_STRESS_ROOT:-/tmp}"
run_count=12
reload_world=0

while (( $# > 0 )); do
	case "$1" in
		--count)
			run_count="${2:-}"
			shift 2
			;;
		--full-reload)
			reload_world=1
			shift
			;;
		--help|-h)
			printf 'Usage: %s [--count number] [--full-reload]\n' "$0"
			exit 0
			;;
		*)
			printf 'Unknown argument: %s\n' "$1" >&2
			exit 2
			;;
	esac
done

if [[ ! "$run_count" =~ ^[1-9][0-9]*$ ]]; then
	printf 'Stress count must be a positive integer: %s\n' "$run_count" >&2
	exit 2
fi

stress_dir="$(mktemp -d "$artifact_parent/vanilla-worlds-overhauled-stress.XXXXXX")"
summary_file="$stress_dir/results.tsv"
printf 'index\tsize\tmode\tseed\tresult\tseconds\tartifacts\n' > "$summary_file"
printf 'World-generation stress artifacts: %s\n' "$stress_dir"

"$script_dir/build-mod.sh"

sizes=(small medium large)
modes=(classic journey)
failures=0
for (( index = 0; index < run_count; index++ )); do
	size="${sizes[index % ${#sizes[@]}]}"
	mode="${modes[index % ${#modes[@]}]}"
	if (( index == 0 )); then
		seed="Majesty-Small-035"
		size="small"
		mode="classic"
	else
		seed="VWO-Stress-$(printf '%03d' "$index")-$size-$mode"
	fi
	run_dir="$stress_dir/run-$(printf '%03d' "$index")-$size-$mode"
	start_seconds=$SECONDS
	arguments=("--$mode" --size "$size" --seed "$seed" --skip-build)
	if (( ! reload_world )); then
		arguments+=(--generation-only)
	fi
	printf '[%d/%d] %s %s seed %s\n' "$((index + 1))" "$run_count" "$mode" "$size" "$seed"
	if VANILLA_WORLDS_OVERHAULED_PLAYTEST_DIR="$run_dir" \
		"$script_dir/generate-playtest-world.sh" "${arguments[@]}"; then
		result=pass
	else
		result=fail
		failures=$((failures + 1))
	fi
	elapsed=$((SECONDS - start_seconds))
	printf '%d\t%s\t%s\t%s\t%s\t%d\t%s\n' \
		"$index" "$size" "$mode" "$seed" "$result" "$elapsed" "$run_dir" >> "$summary_file"
done

passes=$((run_count - failures))
printf 'World-generation stress result: %d/%d passed. Summary: %s\n' "$passes" "$run_count" "$summary_file"
if (( failures > 0 )); then
	printf '%d world-generation run(s) failed; all logs and partial artifacts remain under %s\n' "$failures" "$stress_dir" >&2
	exit 1
fi
