#!/usr/bin/env bash
# Compare PKHeX.Core (save-report) with the Python prototype readers on every save given.
# Usage: ./compare.sh <save file>...
# .srm -> read_em_save.py (Gen 3), .sav -> read_pt_save.py (Gen 4). Saves the readers can't
# handle (e.g. Gen 5, no Python reader yet) are printed by PKHeX only.
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
protos="$here/../../prototypes"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

dotnet build "$here/SpikeA.Cli" -v q -nologo >/dev/null || exit 1
report() { dotnet run --no-build --project "$here/SpikeA.Cli" -- "$@"; }

# Lines that only one side prints (file path, block/slot diagnostics, PKHeX game type).
strip() { grep -Ev '^(File: |game: |save slot -> |general block -> |storage block -> )'; }

pass=0 fail=0 only=0
for save in "$@"; do
    name="$(basename "$save")"
    report "$save" 2>/dev/null | strip > "$tmp/cs.txt"
    game="$(report "$save" 2>/dev/null | sed -n 's/^game: \([A-Za-z0-9]*\).*/\1/p')"
    case "$game" in
        R|S|E|FR|LG) py=read_em_save.py ;;
        D|P|Pt)      py=read_pt_save.py ;;
        *)           py="" ;;
    esac
    if [[ -z "$py" ]]; then
        echo "-- $name: $game, no Python reader; PKHeX only:"
        grep -Ev '\(empty\)|\(0/30\)|^\s*$' "$tmp/cs.txt" | sed 's/^/   /'
        only=$((only + 1))
        continue
    fi
    python3 "$protos/$py" "$save" 2>&1 | strip > "$tmp/py.txt"
    if diff -u "$tmp/py.txt" "$tmp/cs.txt" > "$tmp/diff.txt"; then
        echo "OK  $name ($game): identical"
        pass=$((pass + 1))
    else
        echo "DIFF $name ($game):  (- python, + PKHeX)"
        tail -n +3 "$tmp/diff.txt" | sed 's/^/   /'
        fail=$((fail + 1))
    fi
done
echo
echo "identical: $pass   different: $fail   PKHeX only: $only"
[[ $fail -eq 0 ]]
