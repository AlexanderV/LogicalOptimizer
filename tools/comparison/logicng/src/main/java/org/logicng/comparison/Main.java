package org.logicng.comparison;

import java.io.BufferedReader;
import java.io.IOException;
import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.SortedSet;
import java.util.concurrent.Callable;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

import org.logicng.datastructures.Tristate;
import org.logicng.explanations.smus.SmusComputation;
import org.logicng.formulas.FType;
import org.logicng.formulas.Formula;
import org.logicng.formulas.FormulaFactory;
import org.logicng.formulas.Literal;
import org.logicng.formulas.Variable;
import org.logicng.handlers.TimeoutOptimizationHandler;
import org.logicng.handlers.TimeoutSATHandler;
import org.logicng.io.parsers.PropositionalParser;
import org.logicng.io.readers.DimacsReader;
import org.logicng.knowledgecompilation.bdds.BDD;
import org.logicng.knowledgecompilation.bdds.BDDFactory;
import org.logicng.knowledgecompilation.bdds.jbuddy.BDDKernel;
import org.logicng.primecomputation.PrimeCompiler;
import org.logicng.primecomputation.PrimeResult;
import org.logicng.solvers.MiniSat;
import org.logicng.util.FormulaHelper;

/**
 * Roadmap P0.2 — LogicNG competitor adapter. Reads the shared comparison corpus
 * (the SAME {@code tools/comparison_corpus.txt} the OUR-side harness consumes) and prints
 * a GitHub-Markdown row per function with three independent LogicNG measurements, applying
 * one identical per-function timeout (methodology: single runner, identical timeout) and
 * NEVER fabricating a number: a function that errors or times out gets an honest
 * {@code error} / {@code timeout} cell, and a measurement whose input is absent stays
 * {@code pending} (self-skip), mirroring the other adapters.
 *
 * <ol>
 *     <li><b>BDD</b> (columns {@code LogicNG nodes} / {@code LogicNG #SAT} /
 *     {@code LogicNG ms}, unchanged semantics): each function is compiled to a BDD and its
 *     exact model count over the function's variables is reported — directly comparable to
 *     the {@code modelCount} column of {@code our-results.json} (BDD / d-DNNF #SAT).</li>
 *     <li><b>SAT</b> (columns {@code LogicNG SAT verdict} / {@code LogicNG SAT ms}):
 *     LogicNG's MiniSat solves the SAME {@code <name>.miter.cnf} DIMACS file CaDiCaL /
 *     Kissat / Z3 consume (emitted by {@code comparison-suite --emit-sat-dimacs}). Every
 *     miter is expected {@code unsat} (⇒ the optimization is equivalence-preserving).
 *     When no miter directory is supplied the columns stay {@code pending}.</li>
 *     <li><b>Two-level minimization</b> (columns {@code LogicNG min lits} /
 *     {@code LogicNG min ms}): LogicNG's minimum prime-implicant-cover DNF of the corpus
 *     function — the documented core of LogicNG's {@code AdvancedSimplifier}
 *     ({@link PrimeCompiler#getWithMinimization()} + smallest-MUS coverage via
 *     {@link SmusComputation}, then the DNF is built from the minimal cover). The full
 *     {@code AdvancedSimplifier} is deliberately NOT used here: its rating step may return
 *     the (possibly non-DNF) input formula when that happens to rate smaller, which would
 *     not be a two-level result. The reported number is the LITERAL-OCCURRENCE count of
 *     the minimum-cover DNF — like-for-like with the {@code OUR DNF lits} / SymPy / PyEDA
 *     columns of the two-level table, which count literal occurrences the same way.</li>
 * </ol>
 *
 * <p>Usage: {@code java -jar logicng-adapter.jar <corpus-path> [timeout-seconds] [miter-dimacs-dir]}</p>
 */
public final class Main {

    private static final int BDD_NODE_TABLE = 1_000_000;
    private static final int BDD_CACHE = 100_000;
    private static final String PENDING = "`pending`";

    public static void main(final String[] args) throws IOException {
        if (args.length < 1) {
            System.err.println(
                    "usage: java -jar logicng-adapter.jar <corpus-path> [timeout-seconds] [miter-dimacs-dir]");
            System.exit(2);
            return;
        }
        final String corpusPath = args[0];
        final long timeoutSeconds = args.length >= 2 ? Long.parseLong(args[1]) : 20L;
        final String miterDir = args.length >= 3 ? args[2] : null;

        final List<String[]> functions = readCorpus(corpusPath);
        if (functions.isEmpty()) {
            System.err.println("# Corpus '" + corpusPath + "' contained no functions.");
            return;
        }
        if (miterDir == null) {
            System.err.println("# No miter DIMACS dir supplied; LogicNG SAT columns stay 'pending'. "
                    + "Emit it with 'comparison-suite --emit-sat-dimacs' and pass it as the 3rd argument.");
        } else if (!Files.isDirectory(Paths.get(miterDir))) {
            System.err.println("# Miter DIMACS dir '" + miterDir
                    + "' not found; LogicNG SAT columns stay 'pending'.");
        }

        System.out.println("<!-- generated by tools/comparison/logicng (LogicNG 2.4.1); timeout "
                + timeoutSeconds + "s; miters: " + (miterDir == null ? "none" : miterDir) + " -->");
        System.out.println("| Function | LogicNG nodes | LogicNG #SAT | LogicNG ms "
                + "| LogicNG SAT verdict | LogicNG SAT ms | LogicNG min lits | LogicNG min ms |");
        System.out.println("|----------|-------------:|------------:|----------:"
                + "|:-------------------|---------------:|----------------:|--------------:|");

        final ExecutorService executor = Executors.newSingleThreadExecutor();
        try {
            for (final String[] fn : functions) {
                final String name = fn[1];
                final String expression = fn[2].replace("!", "~");
                final Result bdd = measure(executor, expression, timeoutSeconds);
                final Cell sat = measureMiterSat(miterDir, name, timeoutSeconds);
                final Cell min = measureMinDnf(expression, timeoutSeconds);
                System.out.println("| " + name + " | " + bdd.nodes + " | " + bdd.modelCount + " | " + bdd.ms
                        + " | " + sat.value + " | " + sat.ms + " | " + min.value + " | " + min.ms + " |");
            }
        } finally {
            executor.shutdownNow();
        }
    }

    private static Result measure(final ExecutorService executor, final String expression,
                                  final long timeoutSeconds) {
        final Callable<Result> task = () -> {
            final FormulaFactory f = new FormulaFactory();
            final PropositionalParser parser = new PropositionalParser(f);
            final Formula formula = parser.parse(expression);
            final SortedSet<Variable> variables = formula.variables();
            final List<Variable> ordering = new ArrayList<>(variables);
            final long start = System.nanoTime();
            final BDDKernel kernel = ordering.isEmpty()
                    ? new BDDKernel(f, 0, BDD_NODE_TABLE, BDD_CACHE)
                    : new BDDKernel(f, ordering, BDD_NODE_TABLE, BDD_CACHE);
            final BDD bdd = BDDFactory.build(formula, kernel);
            final BigInteger modelCount = bdd.modelCount();
            final long ms = (System.nanoTime() - start) / 1_000_000L;
            return new Result(String.valueOf(bdd.nodeCount()), modelCount.toString(), String.valueOf(ms));
        };

        final Future<Result> future = executor.submit(task);
        try {
            return future.get(timeoutSeconds, TimeUnit.SECONDS);
        } catch (final TimeoutException e) {
            future.cancel(true);
            return new Result("timeout", "timeout", ">" + timeoutSeconds + "s");
        } catch (final Exception e) {
            return new Result("error", "error", "-");
        }
    }

    /**
     * Solves {@code <miterDir>/<name>.miter.cnf} — byte-identical input to what the
     * CaDiCaL / Kissat / Z3 adapters consume — with LogicNG's MiniSat under the shared
     * timeout ({@link TimeoutSATHandler}). The reported wall-clock covers DIMACS read +
     * clause loading + solving, matching the external solvers whose timing also includes
     * parsing the file.
     */
    private static Cell measureMiterSat(final String miterDir, final String name,
                                        final long timeoutSeconds) {
        if (miterDir == null) {
            return new Cell(PENDING, PENDING);
        }
        final Path cnf = Paths.get(miterDir, name + ".miter.cnf");
        if (!Files.isRegularFile(cnf)) {
            System.err.println("# No miter DIMACS for '" + name + "' in " + miterDir
                    + "; its SAT cells stay 'pending'.");
            return new Cell(PENDING, PENDING);
        }
        try {
            final long start = System.nanoTime();
            final FormulaFactory f = new FormulaFactory();
            final List<Formula> clauses = DimacsReader.readCNF(cnf.toFile(), f);
            final MiniSat solver = MiniSat.miniSat(f);
            solver.add(clauses);
            final Tristate verdict = solver.sat(new TimeoutSATHandler(timeoutSeconds * 1000L));
            final long ms = (System.nanoTime() - start) / 1_000_000L;
            if (verdict == Tristate.UNDEF) {
                return new Cell("timeout", ">" + timeoutSeconds + "s");
            }
            return new Cell(verdict == Tristate.FALSE ? "unsat" : "sat", String.valueOf(ms));
        } catch (final Exception e) {
            return new Cell("error", "-");
        }
    }

    /**
     * LogicNG's minimum prime-implicant-cover DNF of the function, i.e. exactly the
     * {@code computeMinDnf} pipeline inside LogicNG's {@code AdvancedSimplifier}
     * (documented as "computation of all prime implicants; finding a minimal coverage by
     * finding a smallest MUS; building a DNF from the minimal prime implicant coverage"),
     * under one shared {@link TimeoutOptimizationHandler} budget. The minimal coverage
     * minimizes the NUMBER of prime implicants; the reported value is the resulting DNF's
     * literal-occurrence count.
     */
    private static Cell measureMinDnf(final String expression, final long timeoutSeconds) {
        try {
            final FormulaFactory f = new FormulaFactory();
            final Formula formula = new PropositionalParser(f).parse(expression);
            final TimeoutOptimizationHandler handler =
                    new TimeoutOptimizationHandler(timeoutSeconds * 1000L);
            final long start = System.nanoTime();
            final PrimeResult primes = PrimeCompiler.getWithMinimization()
                    .compute(formula, PrimeResult.CoverageType.IMPLICANTS_COMPLETE, handler);
            if (primes == null || handler.aborted()) {
                return new Cell("timeout", ">" + timeoutSeconds + "s");
            }
            final List<Formula> negatedPrimes = new ArrayList<>();
            for (final SortedSet<Literal> implicant : primes.getPrimeImplicants()) {
                negatedPrimes.add(f.or(FormulaHelper.negateLiterals(implicant, ArrayList::new)));
            }
            final List<Formula> minimalCover = SmusComputation.computeSmusForFormulas(
                    negatedPrimes, Collections.singletonList(formula), f, handler);
            if (minimalCover == null || handler.aborted()) {
                return new Cell("timeout", ">" + timeoutSeconds + "s");
            }
            final List<Formula> terms = new ArrayList<>();
            for (final Formula negatedImplicant : minimalCover) {
                terms.add(f.and(FormulaHelper.negateLiterals(negatedImplicant.literals(), ArrayList::new)));
            }
            final Formula minDnf = f.or(terms);
            final long ms = (System.nanoTime() - start) / 1_000_000L;
            return new Cell(String.valueOf(countLiteralOccurrences(minDnf)), String.valueOf(ms));
        } catch (final Exception e) {
            return new Cell("error", "-");
        }
    }

    /** Total literal occurrences, the same structural count the SymPy/PyEDA adapter uses. */
    private static long countLiteralOccurrences(final Formula formula) {
        if (formula.type() == FType.LITERAL) {
            return 1;
        }
        long count = 0;
        for (final Formula operand : formula) {
            count += countLiteralOccurrences(operand);
        }
        return count;
    }

    private static List<String[]> readCorpus(final String path) throws IOException {
        final List<String[]> functions = new ArrayList<>();
        try (BufferedReader reader = Files.newBufferedReader(Paths.get(path), StandardCharsets.UTF_8)) {
            String raw;
            while ((raw = reader.readLine()) != null) {
                final String line = raw.trim();
                if (line.isEmpty() || line.startsWith("#")) {
                    continue;
                }
                final String[] parts = line.split("\\|", 3);
                if (parts.length != 3) {
                    continue;
                }
                functions.add(new String[]{parts[0].trim(), parts[1].trim(), parts[2].trim()});
            }
        }
        return functions;
    }

    private static final class Result {
        final String nodes;
        final String modelCount;
        final String ms;

        Result(final String nodes, final String modelCount, final String ms) {
            this.nodes = nodes;
            this.modelCount = modelCount;
            this.ms = ms;
        }
    }

    /** One value/ms cell pair of the SAT or minimization columns. */
    private static final class Cell {
        final String value;
        final String ms;

        Cell(final String value, final String ms) {
            this.value = value;
            this.ms = ms;
        }
    }

    private Main() {
    }
}
