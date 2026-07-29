#!/usr/bin/env bash
# Roadmap P0.2 — run the whole cross-library comparison inside the container and copy the
# artifacts back to the mounted repo (/work). Every competitor is best-effort: a tool that
# is absent (failed to build in the image) self-skips and leaves its cell `pending`; a tool
# that exceeds the shared timeout records `timeout`. Nothing is fabricated.
#
# The source tree is copied to /build first so the Linux dotnet build never writes bin/obj
# into the mounted (Windows) working tree. Only the small text artifacts are copied back;
# the regenerable DIMACS directories are not.
set -uo pipefail

SRC=/work
BUILD=/build
OUT_REL="doc/comparison"
TIMEOUT_SAT="${TIMEOUT_SAT:-20}"
TIMEOUT_MC="${TIMEOUT_MC:-60}"
TIMEOUT_SYMPY="${TIMEOUT_SYMPY:-20}"
TIMEOUT_LOGICNG="${TIMEOUT_LOGICNG:-20}"
# SymPy and PyEDA build a 2^n truth table internally, so a large function makes them
# allocate 16M+ rows in C where the per-function SIGALRM cannot interrupt them. Bound
# them by variable count (our engine still reports every function); functions above the
# ceiling exceed these truth-table tools' budget and are left without a competitor cell.
MAX_VARS_TRUTHTABLE="${MAX_VARS_TRUTHTABLE:-16}"

echo "== P0.2 controlled cross-library comparison =="

mkdir -p "$BUILD"
rsync -a --exclude '.git' --exclude 'bin' --exclude 'obj' \
      --exclude 'BenchmarkDotNet.Artifacts' --exclude 'TestResults' \
      "$SRC/" "$BUILD/"
cd "$BUILD"

OUT="$BUILD/$OUT_REL"
SATDIR="$OUT/sat-cnf"
FUNCDIR="$OUT/func-cnf"
mkdir -p "$OUT"

echo "== 1/6 OUR side + emit DIMACS (Linux manifest) =="
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- comparison-suite \
    --emit-sat-dimacs "$SATDIR" --emit-function-dimacs "$FUNCDIR"

echo "== 2/6 SymPy / PyEDA =="
python3 tools/compare_sympy_pyeda.py --timeout "$TIMEOUT_SYMPY" --max-vars "$MAX_VARS_TRUTHTABLE" \
    > "$OUT/sympy_out.md" 2> "$OUT/sympy_out.log" || true

echo "== 3/7 CaDiCaL / Kissat (equivalence miters) =="
bash tools/comparison/run_sat_competitors.sh "$SATDIR" "$TIMEOUT_SAT" \
    > "$OUT/sat_out.md" 2> "$OUT/sat_out.log" || true

echo "== 4/7 Z3 (equivalence miters) =="
python3 tools/comparison/run_z3_competitor.py "$SATDIR" "$TIMEOUT_SAT" \
    > "$OUT/z3_out.md" 2> "$OUT/z3_out.log" || true

echo "== 5/7 d4 / c2d (#SAT on count-preserving function CNF) =="
bash tools/comparison/run_modelcount_competitors.sh "$FUNCDIR" "$TIMEOUT_MC" \
    > "$OUT/mc_out.md" 2> "$OUT/mc_out.log" || true

echo "== 6/7 LogicNG BDD =="
if [[ -f /opt/logicng-adapter.jar ]]; then
    java -jar /opt/logicng-adapter.jar tools/comparison_corpus.txt "$TIMEOUT_LOGICNG" \
        > "$OUT/logicng_out.md" 2> "$OUT/logicng_out.log" || true
else
    echo "# LogicNG jar not built in this image; column stays pending." > "$OUT/logicng_out.md"
fi

echo "== 7/7 merge into merged.md =="
python3 tools/comparison/merge_results.py "$OUT/our-results.json" \
    --sympy "$OUT/sympy_out.md" --sat "$OUT/sat_out.md" --z3 "$OUT/z3_out.md" \
    --modelcount "$OUT/mc_out.md" --logicng "$OUT/logicng_out.md" \
    --html "$OUT/merged.html" \
    > "$OUT/merged.md" 2> "$OUT/merged.log" || true

echo "== copy artifacts back to $SRC/$OUT_REL =="
DEST="$SRC/$OUT_REL"
mkdir -p "$DEST"
for f in our-results.json summary.md manifest.json \
         sympy_out.md sat_out.md z3_out.md mc_out.md logicng_out.md merged.md merged.html; do
    if [[ -f "$OUT/$f" ]]; then cp "$OUT/$f" "$DEST/$f"; fi
done

echo "== DONE — artifacts in $DEST =="
ls -la "$DEST"
