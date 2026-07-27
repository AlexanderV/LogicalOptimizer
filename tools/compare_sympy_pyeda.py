#!/usr/bin/env python3
"""Competitor side of roadmap B2: measure SymPy and PyEDA on the SHARED corpus.

Reads tools/comparison_corpus.txt (the SAME file the .NET
`LogicalOptimizer.Benchmarks -- compare` harness consumes), runs each function
through SymPy's ``simplify_logic`` (Quine-McCluskey / minterm two-level DNF) and
PyEDA's ``espresso_exprs`` (Espresso two-level SOP), and prints a GitHub-markdown
table of each tool's result size (literal count) and wall-clock time.

Honesty / reproducibility contract (mirrors LogicalOptimizer.Tests
SymPyDifferentialTests): if a tool is not importable this script SELF-SKIPS that
column cleanly with a printed note instead of failing or fabricating numbers.
So it is safe to run anywhere; the real competitor numbers are produced where the
tools are installed (e.g. CI, which pip-installs sympy; PyPI is blocked on the
maintainer's network).

Usage:
    python tools/compare_sympy_pyeda.py [--corpus PATH] [--max-vars N]

    --max-vars N   Skip functions with more than N variables (both tools build a
                   2^n truth table internally, so large lines can be slow). Default:
                   no limit.

Versions used for the published run are printed in the header line.
"""
from __future__ import annotations

import argparse
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_CORPUS = os.path.join(HERE, "comparison_corpus.txt")


def read_corpus(path):
    """Yield (zone, name, expression) triples from the shared corpus file."""
    with open(path, "r", encoding="utf-8") as handle:
        for raw in handle:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split("|", 2)
            if len(parts) != 3:
                continue
            yield parts[0].strip(), parts[1].strip(), parts[2].strip()


def to_operator_syntax(expression):
    """Corpus uses ! for NOT; both SymPy and PyEDA parsers want ~."""
    return expression.replace("!", "~")


# --- SymPy -----------------------------------------------------------------

def try_import_sympy():
    try:
        import sympy  # noqa: F401
        from sympy import Symbol, preorder_traversal  # noqa: F401
        from sympy.logic.boolalg import simplify_logic  # noqa: F401
        from sympy.parsing.sympy_parser import parse_expr  # noqa: F401
        return sympy
    except Exception:
        return None


def sympy_literals(expr):
    """Count literal occurrences: every Symbol node in the (DNF) expression tree."""
    from sympy import Symbol, preorder_traversal
    return sum(1 for node in preorder_traversal(expr) if isinstance(node, Symbol))


def run_sympy(expression):
    """Return (literal_count, milliseconds) for SymPy's minimal DNF, or None on error."""
    from sympy.logic.boolalg import simplify_logic
    from sympy.parsing.sympy_parser import parse_expr
    try:
        parsed = parse_expr(to_operator_syntax(expression))
        start = time.perf_counter()
        minimized = simplify_logic(parsed, form="dnf", force=True)
        elapsed_ms = (time.perf_counter() - start) * 1000.0
        return sympy_literals(minimized), elapsed_ms
    except Exception as exc:  # pragma: no cover - per-line robustness
        print(f"# sympy failed on '{expression}': {exc}", file=sys.stderr)
        return None


# --- PyEDA -----------------------------------------------------------------

def try_import_pyeda():
    try:
        import pyeda  # noqa: F401
        from pyeda.boolalg.expr import expr, espresso_exprs  # noqa: F401
        return pyeda
    except Exception:
        return None


def pyeda_literals(node):
    """Count literals by structural walk; degrades to 0 for constants/atoms."""
    xs = getattr(node, "xs", None)
    if xs:
        return sum(pyeda_literals(x) for x in xs)
    # A leaf: a Variable or Complement is one literal; a constant contributes none.
    is_zero = getattr(node, "is_zero", None)
    is_one = getattr(node, "is_one", None)
    try:
        if (callable(is_zero) and is_zero()) or (callable(is_one) and is_one()):
            return 0
    except Exception:
        pass
    return 1


def run_pyeda(expression):
    """Return (literal_count, milliseconds) for PyEDA's Espresso SOP, or None on error."""
    from pyeda.boolalg.expr import expr, espresso_exprs
    try:
        f = expr(to_operator_syntax(expression))
        # Espresso needs a DNF/cover input; to_dnf() gives one without extra deps.
        cover = f.to_dnf()
        start = time.perf_counter()
        (minimized,) = espresso_exprs(cover)
        elapsed_ms = (time.perf_counter() - start) * 1000.0
        return pyeda_literals(minimized), elapsed_ms
    except Exception as exc:  # pragma: no cover - per-line robustness
        print(f"# pyeda failed on '{expression}': {exc}", file=sys.stderr)
        return None


# --- Driver ----------------------------------------------------------------

def var_count(expression):
    import re
    names = set(re.findall(r"[A-Za-z][A-Za-z0-9_]*", expression))
    return len(names)


def main(argv=None):
    parser = argparse.ArgumentParser(description="SymPy/PyEDA comparison on the shared B2 corpus.")
    parser.add_argument("--corpus", default=DEFAULT_CORPUS, help="Path to comparison_corpus.txt")
    parser.add_argument("--max-vars", type=int, default=None,
                        help="Skip functions with more than N variables (both tools build 2^n rows).")
    args = parser.parse_args(argv)

    if not os.path.exists(args.corpus):
        print(f"Corpus not found: {args.corpus}", file=sys.stderr)
        return 1

    functions = list(read_corpus(args.corpus))
    if not functions:
        print(f"Corpus '{args.corpus}' contained no functions.", file=sys.stderr)
        return 1

    sympy_mod = try_import_sympy()
    pyeda_mod = try_import_pyeda()

    header = []
    if sympy_mod is not None:
        header.append(f"sympy {sympy_mod.__version__}")
    else:
        print("# NOTE: sympy is not importable here - its columns are skipped "
              "(install via `pip install sympy`, e.g. in CI). No numbers fabricated.",
              file=sys.stderr)
    if pyeda_mod is not None:
        header.append(f"pyeda {pyeda_mod.__version__}")
    else:
        print("# NOTE: pyeda is not importable here - its columns are skipped "
              "(install via `pip install pyeda`). No numbers fabricated.",
              file=sys.stderr)

    if sympy_mod is None and pyeda_mod is None:
        print("# Neither SymPy nor PyEDA is available; nothing to measure. "
              "Run this script where the tools are installed to fill the table.")
        return 0

    print(f"<!-- generated by tools/compare_sympy_pyeda.py on Python "
          f"{sys.version.split()[0]}; {', '.join(header)} -->")
    print("| Zone | Function | Vars | SymPy literals | SymPy ms | PyEDA literals | PyEDA ms |")
    print("|------|----------|-----:|---------------:|---------:|---------------:|---------:|")

    for zone, name, expression in functions:
        nvars = var_count(expression)
        if args.max_vars is not None and nvars > args.max_vars:
            continue

        sym_lits = sym_ms = "—"
        if sympy_mod is not None:
            result = run_sympy(expression)
            if result is not None:
                sym_lits, sym_ms = result[0], f"{result[1]:.3f}"

        pye_lits = pye_ms = "—"
        if pyeda_mod is not None:
            result = run_pyeda(expression)
            if result is not None:
                pye_lits, pye_ms = result[0], f"{result[1]:.3f}"

        print(f"| {zone} | {name} | {nvars} | {sym_lits} | {sym_ms} | {pye_lits} | {pye_ms} |")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
