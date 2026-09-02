#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
artifact_root="${VANILLA_WORLDS_OVERHAULED_MATRIX_ROOT:-/tmp}"
matrix_dir="$(mktemp -d "$artifact_root/vanilla-worlds-overhauled-matrix.XXXXXX")"
export VANILLA_WORLDS_OVERHAULED_PLAYTEST_DIR="$matrix_dir"

printf 'World-generation matrix artifacts: %s\n' "$matrix_dir"

"$script_dir/generate-playtest-world.sh" --classic --size small --seed Majesty-Matrix-Small-001
"$script_dir/generate-playtest-world.sh" --journey --size medium --seed Majesty-Matrix-Medium-001
"$script_dir/generate-playtest-world.sh" --classic --size large --seed Majesty-Matrix-Large-001

printf 'Validated small, medium, and large worlds. Artifacts remain at %s\n' "$matrix_dir"
