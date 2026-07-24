namespace LogicalOptimizer;

public enum SatResult
{
    Satisfiable,
    Unsatisfiable,

    /// <summary>The conflict budget was exhausted before a verdict.</summary>
    Unknown
}

/// <summary>One step of a DRAT proof: a learnt (added) clause or a deleted one.</summary>
public readonly record struct SatProofStep(bool IsDeletion, int[] Literals);

/// <summary>
///     Self-contained CDCL SAT solver: two-watched literals, 1UIP clause learning,
///     heap-based VSIDS activities, Luby restarts, LBD-driven learnt-clause database
///     reduction, bounded subsumption preprocessing, incremental solving under
///     assumptions with unsat cores, and optional DRAT proof logging.
///     Variables are 1-based DIMACS indices; a positive literal v asserts variable v,
///     a negative literal -v its negation. No dependencies.
/// </summary>
public sealed class SatSolver
{
    private const double ActivityRescaleThreshold = 1e100;
    private const int RestartBaseInterval = 100;
    private const int GlueLbdThreshold = 3; // clauses this "glue" or better are never deleted
    private const int MaxPreprocessClauses = 20_000;

    private readonly int _variableCount;
    private readonly List<int[]?> _clauses = new(); // null = deleted (tombstone)
    private readonly List<int> _clauseLbd = new(); // 0 for original clauses
    private readonly List<int>[] _watches; // clause indices watching each literal
    private readonly sbyte[] _assignment; // 0 unassigned, +1 true, -1 false
    private readonly int[] _decisionLevel;
    private readonly int[] _reason; // clause index that implied the variable, -1 for decisions
    private readonly double[] _activity;
    private readonly bool[] _savedPhase;
    private readonly List<int> _trail = new();
    private readonly List<int> _trailLimits = new();
    private readonly bool[] _seen;
    private readonly ActivityHeap _orderHeap;

    private int _propagationHead;
    private double _activityIncrement = 1.0;
    private bool _foundEmptyClause;
    private List<SatProofStep>? _proof;
    private List<int>? _unsatCore;
    private int _learntCount;
    private int _conflictsUntilReduce = 2000;
    private int _reduceIncrement = 1000;
    private bool _preprocessPending = true;
    private int _lastPreprocessClauseCount;

    public SatSolver(int variableCount)
    {
        if (variableCount < 0) throw new ArgumentOutOfRangeException(nameof(variableCount));
        _variableCount = variableCount;
        _watches = new List<int>[2 * variableCount + 2];
        for (var i = 0; i < _watches.Length; i++) _watches[i] = new List<int>();
        _assignment = new sbyte[variableCount + 1];
        _decisionLevel = new int[variableCount + 1];
        _reason = new int[variableCount + 1];
        _activity = new double[variableCount + 1];
        _savedPhase = new bool[variableCount + 1];
        _seen = new bool[variableCount + 1];
        _orderHeap = new ActivityHeap(variableCount, _activity);
    }

    /// <summary>Conflicts spent by the last <see cref="Solve(int, CancellationToken)" /> call.</summary>
    public int ConflictCount { get; private set; }

    /// <summary>
    ///     Start recording a DRAT proof (learnt and deleted clauses in order, ending with
    ///     the empty clause on UNSAT). Must be called before the first
    ///     <see cref="Solve(int, CancellationToken)" />. Every addition step is
    ///     RUP-checkable against the original clauses plus preceding steps, so an
    ///     Unsatisfiable verdict becomes externally verifiable (e.g. with drat-trim).
    /// </summary>
    public void EnableProofLogging()
    {
        _proof = new List<SatProofStep>();
    }

    /// <summary>Recorded proof steps, or null if logging was not enabled.</summary>
    public IReadOnlyList<SatProofStep>? Proof => _proof;

    /// <summary>Proof in textual DRAT format ("d " prefix for deletions, 0-terminated lines).</summary>
    public string ToDrat()
    {
        if (_proof == null)
            throw new InvalidOperationException("Proof logging was not enabled");
        return string.Join("\n", _proof.Select(step =>
            (step.IsDeletion ? "d " : "") + string.Join(" ", step.Literals.Append(0))));
    }

    public static SatSolver FromCnf(TseitinCnf cnf)
    {
        var solver = new SatSolver(cnf.TotalVariableCount);
        foreach (var clause in cnf.Clauses) solver.AddClause(clause);
        return solver;
    }

    public void AddClause(params int[] literals)
    {
        AddClause((IReadOnlyCollection<int>)literals);
    }

    public void AddClause(IReadOnlyCollection<int> literals)
    {
        // Incremental use: adding clauses after a Solve is allowed — state above level 0
        // is discarded, level-0 facts and learnt clauses are kept
        BacktrackTo(0);
        _preprocessPending = true;

        // Normalize: drop duplicate literals, detect tautologies (v | !v)
        var clause = new List<int>(literals.Count);
        foreach (var literal in literals)
        {
            var variable = Math.Abs(literal);
            if (variable < 1 || variable > _variableCount)
                throw new ArgumentOutOfRangeException(nameof(literals), $"Literal {literal} out of range");
            if (clause.Contains(-literal)) return; // tautology, always satisfied
            if (!clause.Contains(literal)) clause.Add(literal);
        }

        if (clause.Count == 0)
        {
            _foundEmptyClause = true;
            return;
        }

        StoreClause(clause.ToArray(), 0);
    }

    private int StoreClause(int[] clause, int lbd)
    {
        var index = _clauses.Count;
        _clauses.Add(clause);
        _clauseLbd.Add(lbd);
        if (clause.Length >= 2)
        {
            _watches[LiteralIndex(clause[0])].Add(index);
            _watches[LiteralIndex(clause[1])].Add(index);
        }

        return index;
    }

    public SatResult Solve(int maxConflicts = 1_000_000, CancellationToken cancellationToken = default)
    {
        return Solve(Array.Empty<int>(), maxConflicts, cancellationToken);
    }

    /// <summary>
    ///     Incremental solve under assumptions: temporary unit constraints that hold for
    ///     this call only. Learnt clauses and level-0 facts persist across calls, and
    ///     clauses may be added between calls. On Unsatisfiable, <see cref="UnsatCore" />
    ///     holds the subset of assumptions responsible (empty = UNSAT regardless of them).
    /// </summary>
    public SatResult Solve(IReadOnlyList<int> assumptions, int maxConflicts = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        ConflictCount = 0;
        _unsatCore = null;
        BacktrackTo(0);
        // Replay level-0 propagation so clauses added after a previous Solve get their
        // watches updated and their consequences asserted
        _propagationHead = 0;

        if (_foundEmptyClause)
        {
            _proof?.Add(new SatProofStep(false, Array.Empty<int>()));
            _unsatCore = new List<int>();
            return SatResult.Unsatisfiable;
        }

        // Preprocess when the database grew meaningfully since the last pass — running it
        // on every incremental clause addition (e.g. model-enumeration blocking clauses)
        // would cost more than it saves
        if (_preprocessPending && _clauses.Count > 2 * _lastPreprocessClauseCount + 16)
        {
            PreprocessSubsumption();
            _lastPreprocessClauseCount = _clauses.Count;
            _preprocessPending = false;
        }

        // Assert unit clauses at level 0
        foreach (var clause in _clauses)
            if (clause is { Length: 1 } && !Enqueue(clause[0], -1))
            {
                _proof?.Add(new SatProofStep(false, Array.Empty<int>()));
                _unsatCore = new List<int>();
                return SatResult.Unsatisfiable;
            }

        RebuildOrderHeap();
        var restartCount = 0;
        var restartThreshold = RestartBaseInterval * Luby(restartCount++);
        var conflictsSinceRestart = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var conflictClause = Propagate();
            if (conflictClause >= 0)
            {
                ConflictCount++;
                conflictsSinceRestart++;
                if (CurrentLevel == 0)
                {
                    _proof?.Add(new SatProofStep(false, Array.Empty<int>()));
                    _unsatCore = new List<int>();
                    return SatResult.Unsatisfiable;
                }
                if (ConflictCount >= maxConflicts) return SatResult.Unknown;

                AnalyzeAndBackjump(conflictClause);
                DecayActivities();

                if (ConflictCount >= _conflictsUntilReduce)
                {
                    _conflictsUntilReduce += _reduceIncrement;
                    _reduceIncrement += 500;
                    ReduceLearntDatabase();
                }

                continue;
            }

            // Assumptions occupy the first decision levels, re-pushed after each restart
            if (CurrentLevel < assumptions.Count)
            {
                var assumption = assumptions[CurrentLevel];
                var value = LiteralValue(assumption);
                if (value < 0)
                {
                    _unsatCore = AnalyzeFinal(assumption);
                    return SatResult.Unsatisfiable;
                }

                _trailLimits.Add(_trail.Count);
                if (value == 0) Enqueue(assumption, -1);
                continue;
            }

            if (_trail.Count == _variableCount) return SatResult.Satisfiable;

            if (conflictsSinceRestart >= restartThreshold)
            {
                conflictsSinceRestart = 0;
                restartThreshold = RestartBaseInterval * Luby(restartCount++);
                BacktrackTo(0);
                continue;
            }

            var variable = PickBranchVariable();
            _trailLimits.Add(_trail.Count);
            Enqueue(_savedPhase[variable] ? variable : -variable, -1);
        }
    }

    /// <summary>
    ///     After an Unsatisfiable verdict from the assumption overload: the subset of
    ///     assumption literals jointly inconsistent with the formula. Empty means the
    ///     formula is unsatisfiable without any assumptions. Null after Satisfiable/Unknown.
    /// </summary>
    public IReadOnlyList<int>? UnsatCore => _unsatCore;

    /// <summary>Value of a variable in the satisfying assignment found by the last Solve.</summary>
    public bool GetValue(int variable)
    {
        if (variable < 1 || variable > _variableCount) throw new ArgumentOutOfRangeException(nameof(variable));
        return _assignment[variable] > 0;
    }

    private int CurrentLevel => _trailLimits.Count;

    private static int LiteralIndex(int literal)
    {
        var variable = Math.Abs(literal);
        return literal > 0 ? 2 * variable : 2 * variable + 1;
    }

    private sbyte LiteralValue(int literal)
    {
        var value = _assignment[Math.Abs(literal)];
        return literal > 0 ? value : (sbyte)-value;
    }

    /// <summary>Reluctant-doubling sequence 1,1,2,1,1,2,4,... (Luby et al.).</summary>
    internal static int Luby(int i)
    {
        int size = 1, sequence = 0;
        while (size < i + 1)
        {
            sequence++;
            size = 2 * size + 1;
        }

        while (size - 1 != i)
        {
            size = (size - 1) / 2;
            sequence--;
            i %= size;
        }

        return 1 << sequence;
    }

    private bool Enqueue(int literal, int reasonClause)
    {
        var value = LiteralValue(literal);
        if (value > 0) return true; // already satisfied
        if (value < 0) return false; // conflicting assignment

        var variable = Math.Abs(literal);
        _assignment[variable] = literal > 0 ? (sbyte)1 : (sbyte)-1;
        _decisionLevel[variable] = CurrentLevel;
        _reason[variable] = reasonClause;
        _savedPhase[variable] = literal > 0;
        _trail.Add(literal);
        return true;
    }

    /// <summary>Unit propagation; returns the index of a conflicting clause or -1.</summary>
    private int Propagate()
    {
        while (_propagationHead < _trail.Count)
        {
            var trueLiteral = _trail[_propagationHead++];
            var falseLiteral = -trueLiteral;
            var watchList = _watches[LiteralIndex(falseLiteral)];

            var kept = 0;
            for (var i = 0; i < watchList.Count; i++)
            {
                var clauseIndex = watchList[i];
                var clause = _clauses[clauseIndex];
                if (clause == null) continue; // deleted clause: drop the watch lazily

                // Ensure the false literal sits at position 1
                if (clause[0] == falseLiteral) (clause[0], clause[1]) = (clause[1], clause[0]);

                if (LiteralValue(clause[0]) > 0)
                {
                    watchList[kept++] = clauseIndex; // clause already satisfied, keep watch
                    continue;
                }

                var movedWatch = false;
                for (var k = 2; k < clause.Length; k++)
                    if (LiteralValue(clause[k]) >= 0)
                    {
                        (clause[1], clause[k]) = (clause[k], clause[1]);
                        _watches[LiteralIndex(clause[1])].Add(clauseIndex);
                        movedWatch = true;
                        break;
                    }

                if (movedWatch) continue;

                // Clause is unit or conflicting
                watchList[kept++] = clauseIndex;
                if (!Enqueue(clause[0], clauseIndex))
                {
                    // Conflict: keep remaining watches and report
                    for (var j = i + 1; j < watchList.Count; j++) watchList[kept++] = watchList[j];
                    watchList.RemoveRange(kept, watchList.Count - kept);
                    return clauseIndex;
                }
            }

            watchList.RemoveRange(kept, watchList.Count - kept);
        }

        return -1;
    }

    /// <summary>1UIP conflict analysis, clause learning and non-chronological backjump.</summary>
    private void AnalyzeAndBackjump(int conflictClause)
    {
        var learnt = new List<int> { 0 }; // slot 0 reserved for the asserting literal
        var counter = 0;
        var assertingLiteral = 0;
        var trailIndex = _trail.Count - 1;
        var clauseIndex = conflictClause;

        while (true)
        {
            var clause = _clauses[clauseIndex]!;
            foreach (var literal in clause)
            {
                if (literal == assertingLiteral) continue;
                var variable = Math.Abs(literal);
                if (_seen[variable] || _decisionLevel[variable] == 0) continue;

                _seen[variable] = true;
                BumpActivity(variable);
                if (_decisionLevel[variable] == CurrentLevel) counter++;
                else learnt.Add(literal);
            }

            int uipCandidate;
            do
            {
                uipCandidate = _trail[trailIndex--];
            } while (!_seen[Math.Abs(uipCandidate)]);

            var uipVariable = Math.Abs(uipCandidate);
            _seen[uipVariable] = false;
            counter--;
            if (counter == 0)
            {
                assertingLiteral = -uipCandidate;
                break;
            }

            assertingLiteral = uipCandidate;
            clauseIndex = _reason[uipVariable];
        }

        learnt[0] = assertingLiteral;
        foreach (var literal in learnt) _seen[Math.Abs(literal)] = false;
        _proof?.Add(new SatProofStep(false, learnt.ToArray()));

        // Backjump to the second-highest level in the learnt clause
        var backjumpLevel = 0;
        var secondWatchIndex = 1;
        for (var i = 1; i < learnt.Count; i++)
        {
            var level = _decisionLevel[Math.Abs(learnt[i])];
            if (level > backjumpLevel)
            {
                backjumpLevel = level;
                secondWatchIndex = i;
            }
        }

        BacktrackTo(backjumpLevel);

        if (learnt.Count == 1)
        {
            Enqueue(assertingLiteral, -1);
            return;
        }

        (learnt[1], learnt[secondWatchIndex]) = (learnt[secondWatchIndex], learnt[1]);
        var learntIndex = StoreClause(learnt.ToArray(), ComputeLbd(learnt));
        _learntCount++;
        Enqueue(assertingLiteral, learntIndex);
    }

    /// <summary>Literal-block distance: distinct decision levels in the clause.</summary>
    private int ComputeLbd(List<int> clause)
    {
        var levels = new HashSet<int>();
        foreach (var literal in clause)
        {
            var level = _decisionLevel[Math.Abs(literal)];
            if (level > 0) levels.Add(level);
        }

        return levels.Count;
    }

    /// <summary>
    ///     Drop the worst (highest-LBD) half of deletable learnt clauses. Glue clauses,
    ///     binary clauses and clauses currently acting as reasons are kept. Deletions are
    ///     tombstones (watches are dropped lazily) and go into the DRAT proof.
    /// </summary>
    private void ReduceLearntDatabase()
    {
        var deletable = new List<int>();
        for (var i = 0; i < _clauses.Count; i++)
        {
            var clause = _clauses[i];
            if (clause == null || _clauseLbd[i] == 0) continue; // deleted or original
            if (_clauseLbd[i] <= GlueLbdThreshold || clause.Length <= 2) continue;
            if (IsReason(i, clause)) continue;
            deletable.Add(i);
        }

        deletable.Sort((a, b) => _clauseLbd[b].CompareTo(_clauseLbd[a]));
        foreach (var index in deletable.Take(deletable.Count / 2))
        {
            _proof?.Add(new SatProofStep(true, _clauses[index]!));
            _clauses[index] = null;
            _learntCount--;
        }
    }

    private bool IsReason(int clauseIndex, int[] clause)
    {
        var variable = Math.Abs(clause[0]);
        return _assignment[variable] != 0 && _reason[variable] == clauseIndex;
    }

    /// <summary>
    ///     Bounded forward subsumption and self-subsuming resolution over the clause
    ///     database. Subsumed clauses are deleted; self-subsumption strengthens a clause
    ///     by removing one literal (the strengthened clause is RUP against the pair, so
    ///     the proof stays valid: add the stronger clause, delete the weaker).
    /// </summary>
    private void PreprocessSubsumption()
    {
        var live = new List<int>();
        for (var i = 0; i < _clauses.Count; i++)
            if (_clauses[i] != null && !IsReason(i, _clauses[i]!))
                live.Add(i);
        if (live.Count == 0 || live.Count > MaxPreprocessClauses) return;

        // Occurrence lists and 64-bit signatures for fast subset pre-filtering
        var occurrences = new Dictionary<int, List<int>>();
        var signatures = new Dictionary<int, ulong>();
        foreach (var index in live)
        {
            ulong signature = 0;
            foreach (var literal in _clauses[index]!)
            {
                signature |= 1UL << (Math.Abs(literal) % 64);
                if (!occurrences.TryGetValue(literal, out var list))
                    occurrences[literal] = list = new List<int>();
                list.Add(index);
            }

            signatures[index] = signature;
        }

        foreach (var index in live.OrderBy(i => _clauses[i]!.Length))
        {
            var clause = _clauses[index];
            if (clause == null) continue;

            // Scan candidates through the literal with the fewest occurrences
            var pivot = clause.MinBy(l => occurrences.TryGetValue(l, out var o) ? o.Count : 0);
            if (!occurrences.TryGetValue(pivot, out var candidates)) continue;

            foreach (var otherIndex in candidates)
            {
                var other = _clauses[otherIndex];
                if (other == null || otherIndex == index || other.Length < clause.Length) continue;
                if ((signatures[index] & ~signatures[otherIndex]) != 0) continue;
                if (!IsSubset(clause, other)) continue;

                // clause ⊆ other: other is redundant
                _proof?.Add(new SatProofStep(true, other));
                if (_clauseLbd[otherIndex] > 0) _learntCount--;
                _clauses[otherIndex] = null;
            }

            // Self-subsumption: clause = C ∪ {l}; any D ⊇ C ∪ {¬l} shrinks to D \ {¬l}
            foreach (var literal in clause)
            {
                if (!occurrences.TryGetValue(-literal, out var negOccurrences)) continue;
                foreach (var otherIndex in negOccurrences)
                {
                    var other = _clauses[otherIndex];
                    if (other == null || other.Length < clause.Length) continue;
                    if ((signatures[index] & ~(signatures[otherIndex] | (1UL << (Math.Abs(literal) % 64)))) != 0)
                        continue;
                    if (!IsSubsetExcept(clause, other, literal)) continue;

                    var strengthened = other.Where(l => l != -literal).ToArray();
                    if (strengthened.Length == 0) continue; // would need conflict handling; skip
                    _proof?.Add(new SatProofStep(false, strengthened));
                    _proof?.Add(new SatProofStep(true, other));
                    if (_clauseLbd[otherIndex] > 0) _learntCount--;
                    _clauses[otherIndex] = null;
                    StoreClause(strengthened, _clauseLbd[otherIndex]);
                }
            }
        }
    }

    private static bool IsSubset(int[] small, int[] large)
    {
        foreach (var literal in small)
            if (Array.IndexOf(large, literal) < 0)
                return false;
        return true;
    }

    /// <summary>small \ {exception} ⊆ large, and -exception ∈ large.</summary>
    private static bool IsSubsetExcept(int[] small, int[] large, int exception)
    {
        foreach (var literal in small)
        {
            if (literal == exception) continue;
            if (Array.IndexOf(large, literal) < 0) return false;
        }

        return Array.IndexOf(large, -exception) >= 0;
    }

    /// <summary>Assumptions on the reason chain of a failed assumption literal.</summary>
    private List<int> AnalyzeFinal(int failedAssumption)
    {
        var core = new List<int> { failedAssumption };
        if (CurrentLevel == 0) return core; // falsified by formula facts alone

        _seen[Math.Abs(failedAssumption)] = true;
        for (var i = _trail.Count - 1; i >= _trailLimits[0]; i--)
        {
            var variable = Math.Abs(_trail[i]);
            if (!_seen[variable]) continue;
            _seen[variable] = false;

            if (_reason[variable] == -1)
            {
                if (_decisionLevel[variable] > 0) core.Add(_trail[i]);
            }
            else
            {
                foreach (var literal in _clauses[_reason[variable]]!)
                {
                    var reasonVariable = Math.Abs(literal);
                    // Skip the implied literal itself: re-marking the variable just
                    // cleared would leak _seen state into later conflict analyses
                    if (reasonVariable == variable) continue;
                    if (_decisionLevel[reasonVariable] > 0) _seen[reasonVariable] = true;
                }
            }
        }

        _seen[Math.Abs(failedAssumption)] = false;
        return core;
    }

    private void BacktrackTo(int level)
    {
        if (CurrentLevel <= level) return;

        var target = _trailLimits[level];
        for (var i = _trail.Count - 1; i >= target; i--)
        {
            var variable = Math.Abs(_trail[i]);
            _savedPhase[variable] = _assignment[variable] > 0;
            _assignment[variable] = 0;
            _orderHeap.Insert(variable);
        }

        _trail.RemoveRange(target, _trail.Count - target);
        _trailLimits.RemoveRange(level, _trailLimits.Count - level);
        _propagationHead = _trail.Count;
    }

    private void RebuildOrderHeap()
    {
        for (var variable = 1; variable <= _variableCount; variable++)
            if (_assignment[variable] == 0)
                _orderHeap.Insert(variable);
    }

    private int PickBranchVariable()
    {
        while (!_orderHeap.IsEmpty)
        {
            var variable = _orderHeap.PopMax();
            if (_assignment[variable] == 0) return variable;
        }

        // Heap exhausted (defensive): linear fallback
        for (var variable = 1; variable <= _variableCount; variable++)
            if (_assignment[variable] == 0)
                return variable;
        throw new InvalidOperationException("No unassigned variable to branch on");
    }

    private void BumpActivity(int variable)
    {
        _activity[variable] += _activityIncrement;
        _orderHeap.NotifyIncreased(variable);
        if (_activity[variable] > ActivityRescaleThreshold)
        {
            // Uniform rescale preserves heap order
            for (var v = 1; v <= _variableCount; v++) _activity[v] *= 1e-100;
            _activityIncrement *= 1e-100;
        }
    }

    private void DecayActivities()
    {
        _activityIncrement /= 0.95;
    }

    /// <summary>Indexed binary max-heap over variable activities (O(log n) decisions).</summary>
    private sealed class ActivityHeap
    {
        private readonly List<int> _heap = new();
        private readonly int[] _position; // 0 = absent, otherwise heap index + 1
        private readonly double[] _activity;

        public ActivityHeap(int variableCount, double[] activity)
        {
            _position = new int[variableCount + 1];
            _activity = activity;
        }

        public bool IsEmpty => _heap.Count == 0;

        public void Insert(int variable)
        {
            if (_position[variable] != 0) return;
            _heap.Add(variable);
            _position[variable] = _heap.Count;
            SiftUp(_heap.Count - 1);
        }

        public int PopMax()
        {
            var top = _heap[0];
            var last = _heap[^1];
            _heap.RemoveAt(_heap.Count - 1);
            _position[top] = 0;
            if (_heap.Count > 0)
            {
                _heap[0] = last;
                _position[last] = 1;
                SiftDown(0);
            }

            return top;
        }

        public void NotifyIncreased(int variable)
        {
            if (_position[variable] != 0) SiftUp(_position[variable] - 1);
        }

        private void SiftUp(int index)
        {
            var variable = _heap[index];
            while (index > 0)
            {
                var parentIndex = (index - 1) / 2;
                var parent = _heap[parentIndex];
                if (_activity[parent] >= _activity[variable]) break;
                _heap[index] = parent;
                _position[parent] = index + 1;
                index = parentIndex;
            }

            _heap[index] = variable;
            _position[variable] = index + 1;
        }

        private void SiftDown(int index)
        {
            var variable = _heap[index];
            while (true)
            {
                var childIndex = 2 * index + 1;
                if (childIndex >= _heap.Count) break;
                if (childIndex + 1 < _heap.Count &&
                    _activity[_heap[childIndex + 1]] > _activity[_heap[childIndex]])
                    childIndex++;
                if (_activity[_heap[childIndex]] <= _activity[variable]) break;
                _heap[index] = _heap[childIndex];
                _position[_heap[index]] = index + 1;
                index = childIndex;
            }

            _heap[index] = variable;
            _position[variable] = index + 1;
        }
    }
}
