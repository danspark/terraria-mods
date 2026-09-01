#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
artifact_root="${RICHER_BIOMES_MATRIX_ROOT:-/tmp}"
matrix_dir="$(mktemp -d "$artifact_root/richer-biomes-matrix.XXXXXX")"
export RICHER_BIOMES_PLAYTEST_DIR="$matrix_dir"

printf 'World-generation matrix artifacts: %s\n' "$matrix_dir"

"$script_dir/generate-playtest-world.sh" --classic --size small --seed Majesty-Matrix-Small-001
"$script_dir/generate-playtest-world.sh" --journey --size medium --seed Majesty-Matrix-Medium-001
"$script_dir/generate-playtest-world.sh" --classic --size large --seed Majesty-Matrix-Large-001

printf 'Validated small, medium, and large worlds. Artifacts remain at %s\n' "$matrix_dir"
