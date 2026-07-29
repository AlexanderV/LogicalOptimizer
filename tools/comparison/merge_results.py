#!/usr/bin/env python3
"""Roadmap P0.2 — merge the committed OUR-side results with competitor adapter output.

OUR columns are ALWAYS taken from doc/comparison/our-results.json (the committed
artifact); competitor columns are filled from the Markdown a competitor adapter
printed, keyed by function name, and left ``pending`` when no adapter output was
supplied. Nothing is fabricated: a missing competitor input simply stays ``pending``,
and a ``timeout`` cell from an adapter is copied through verbatim.

Usage:
    python tools/comparison/merge_results.py doc/comparison/our-results.json \\
        [--sympy sympy_out.md] [--sat sat_out.md] [--modelcount mc_out.md]

Each competitor file is a Markdown pipe-table whose first column is the function
name; remaining columns are matched to the summary's competitor columns by header.
Prints the merged Markdown to stdout.
"""
from __future__ import annotations

import argparse
import json
import sys


def parse_table(path):
    """Parse a Markdown pipe table into {function_name: {header: cell}}.

    The key is the ``Function`` column wherever it sits — not blindly the first column:
    the SymPy/PyEDA adapter leads with a ``Zone`` column, so keying on column 0 would key
    its rows by zone and never match a function name. Falls back to column 0 when no
    ``Function`` header is present.
    """
    rows = {}
    headers = None
    key_idx = 0
    with open(path, "r", encoding="utf-8") as handle:
        for raw in handle:
            line = raw.strip()
            if not line.startswith("|"):
                continue
            cells = [c.strip() for c in line.strip("|").split("|")]
            if set("".join(cells)) <= set("-: "):  # separator row
                continue
            if headers is None:
                headers = cells
                for i, h in enumerate(headers):
                    if h.strip().lower() == "function":
                        key_idx = i
                        break
                continue
            if key_idx < len(cells):
                rows[cells[key_idx]] = dict(zip(headers, cells))
    return rows, (headers or [])


def cell(compet, name, *header_options, default="`pending`"):
    """First matching competitor cell for `name` under any of header_options."""
    row = compet.get(name)
    if not row:
        return default
    for h in header_options:
        for key, value in row.items():
            if h.lower() in key.lower():
                return value
    return default


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("results", help="doc/comparison/our-results.json")
    ap.add_argument("--sympy", help="compare_sympy_pyeda.py output (Markdown)")
    ap.add_argument("--sat", help="run_sat_competitors.sh output (Markdown)")
    ap.add_argument("--modelcount", help="run_modelcount_competitors.sh output (Markdown)")
    ap.add_argument("--logicng", help="tools/comparison/logicng adapter output (Markdown)")
    args = ap.parse_args(argv)

    with open(args.results, "r", encoding="utf-8") as handle:
        data = json.load(handle)

    sympy = parse_table(args.sympy)[0] if args.sympy else {}
    sat = parse_table(args.sat)[0] if args.sat else {}
    mc = parse_table(args.modelcount)[0] if args.modelcount else {}
    logicng = parse_table(args.logicng)[0] if args.logicng else {}

    out = []
    out.append("# Cross-library comparison — merged (OUR committed + competitor adapters)\n")
    out.append(f"Corpus sha256 `{data['corpus']['sha256']}`, {data['corpus']['functionCount']} functions. "
               "OUR columns from `our-results.json`; competitor columns from adapter output or `pending`.\n")

    out.append("## 1. Symbolic optimization (result size)\n")
    out.append("| Function | LogicalOptimizer out lits | SymPy lits | PyEDA lits |")
    out.append("|----------|--------------------------:|:----------:|:----------:|")
    for r in data["symbolicOptimization"]:
        n = r["name"]
        out.append(f"| {n} | {r['outputLiterals']} | "
                   f"{cell(sympy, n, 'SymPy literals')} | {cell(sympy, n, 'PyEDA literals')} |")

    out.append("\n## 2. Two-level SOP (result size)\n")
    out.append("| Function | LogicalOptimizer DNF lits | SymPy lits | PyEDA lits |")
    out.append("|----------|--------------------------:|:----------:|:----------:|")
    for r in data["twoLevelMinimization"]:
        n = r["name"]
        lits = "-" if r["abandoned"] else r["literals"]
        out.append(f"| {n} | {lits} | "
                   f"{cell(sympy, n, 'SymPy literals')} | {cell(sympy, n, 'PyEDA literals')} |")

    out.append("\n## 3. SAT (equivalence miter)\n")
    out.append("| Function | LogicalOptimizer verdict | conflicts | CaDiCaL | Kissat |")
    out.append("|----------|:------------------------:|----------:|:-------:|:------:|")
    for r in data["sat"]:
        n = r["name"]
        out.append(f"| {n} | {r['verdict']} | {r['conflicts']} | "
                   f"{cell(sat, n, 'cadical verdict', 'cadical')} | {cell(sat, n, 'kissat verdict', 'kissat')} |")

    out.append("\n## 4. Model counting (BDD / d-DNNF vs c2d/d4 and LogicNG BDD)\n")
    out.append("The competitor #SAT is counted on the count-preserving per-function CNF "
               "(`comparison-suite --emit-function-dimacs`), so it is directly comparable to "
               "OUR exact `modelCount`; LogicNG counts its own BDD. Matching numbers are an "
               "independent cross-check of OUR #SAT.\n")
    out.append("| Function | LogicalOptimizer #SAT | c2d/d4 #SAT | LogicNG #SAT | LogicNG nodes |")
    out.append("|----------|----------------------:|:-----------:|:-----------:|:------------:|")
    for r in data["bddDnnf"]:
        n = r["name"]
        out.append(f"| {n} | {r['modelCount'] or '-'} | {cell(mc, n, 'd4 #SAT', 'c2d #SAT', 'd4', 'c2d')} | "
                   f"{cell(logicng, n, 'LogicNG #SAT')} | {cell(logicng, n, 'LogicNG nodes')} |")

    print("\n".join(out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
