using System.Globalization;

namespace Enochian.Benchmark;

public static class SequenceNullGenerator
{
    public static IReadOnlyList<SequenceNullRecord> Generate(
        string analysisId,
        IEnumerable<SamplingCandidate> candidates,
        IEnumerable<SamplingQuery> queries,
        IReadOnlyDictionary<string, double[]> mapping,
        int repetitions,
        int seed,
        string generatorVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repetitions);
        var candidateArray = candidates.OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal).ToArray();
        var rows = new List<SequenceNullRecord>();
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            rows.AddRange(GenerateForSample(
                analysisId,
                string.Create(CultureInfo.InvariantCulture, $"{analysisId}.all.rep-{repetition:D4}"),
                candidateArray.GroupBy(candidate => candidate.Language).Min(group => group.Count()),
                candidateArray,
                queries,
                mapping,
                repetition,
                seed,
                generatorVersion));
        }

        return [.. rows.OrderBy(row => row.NullId, StringComparer.Ordinal).ThenBy(row => row.AnalysisMode, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<SequenceNullRecord> GenerateForSample(
        string analysisId,
        string sampleId,
        int requestedSize,
        IEnumerable<SamplingCandidate> candidates,
        IEnumerable<SamplingQuery> queries,
        IReadOnlyDictionary<string, double[]> mapping,
        int repetition,
        int seed,
        string generatorVersion,
        int nullRepetition = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repetition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nullRepetition);
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        var candidateArray = candidates.OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal).ToArray();
        var queryArray = queries.OrderBy(query => query.QueryId, StringComparer.Ordinal).ToArray();
        ValidateQueries(queryArray, mapping);
        var rows = new List<SequenceNullRecord>();
        var randomizationRepetition = checked((repetition * 100000) + nullRepetition);
        var shuffledMapping = ShuffleMapping(mapping, seed, randomizationRepetition);
        foreach (var query in queryArray)
        {
            double[][] observed = [.. query.Symbols.Select(symbol => mapping[symbol])];
            AddModes(rows, analysisId, sampleId, requestedSize, "mapping-assignment-shuffle", "all", query,
                [.. query.Symbols.Select(symbol => shuffledMapping[symbol])], repetition, nullRepetition, seed, generatorVersion);
            AddModes(rows, analysisId, sampleId, requestedSize, "within-query-shuffle", "all", query,
                ShufflePhones(observed, seed, randomizationRepetition, query.QueryId), repetition, nullRepetition, seed, generatorVersion);
        }

        foreach (var languageGroup in candidateArray.GroupBy(candidate => candidate.Language).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var model = new LanguagePhoneModel(languageGroup);
            foreach (var query in queryArray)
            {
                AddModes(rows, analysisId, sampleId, requestedSize, "unigram-pseudoword", languageGroup.Key, query,
                    model.GenerateUnigram(query.Symbols.Count, seed, randomizationRepetition, query.QueryId), repetition, nullRepetition, seed, generatorVersion);
                AddModes(rows, analysisId, sampleId, requestedSize, "biphone-pseudoword", languageGroup.Key, query,
                    model.GenerateBiphone(query.Symbols.Count, seed, randomizationRepetition, query.QueryId), repetition, nullRepetition, seed, generatorVersion);
            }
        }

        return [.. rows.OrderBy(row => row.NullId, StringComparer.Ordinal).ThenBy(row => row.AnalysisMode, StringComparer.Ordinal)];
    }

    private static void ValidateQueries(
        SamplingQuery[] queries,
        IReadOnlyDictionary<string, double[]> mapping)
    {
        if (queries.Length == 0)
        {
            throw new InvalidDataException("Null generation requires at least one query.");
        }

        foreach (var query in queries)
        {
            if (query.Symbols.Count == 0 || query.TokenFrequency <= 0)
            {
                throw new InvalidDataException($"Query '{query.QueryId}' requires symbols and positive token frequency.");
            }

            foreach (var symbol in query.Symbols)
            {
                if (!mapping.ContainsKey(symbol))
                {
                    throw new InvalidDataException($"Query '{query.QueryId}' uses unmapped symbol '{symbol}'.");
                }
            }
        }
    }

    private static Dictionary<string, double[]> ShuffleMapping(
        IReadOnlyDictionary<string, double[]> mapping,
        int seed,
        int repetition)
    {
        var symbols = mapping.Keys.Order(StringComparer.Ordinal).ToArray();
        var assignments = symbols.Select(symbol => mapping[symbol]).ToArray();
        var orderedAssignments = assignments
            .Select((phone, index) => (Phone: phone, Key: BalancedSampler.StableKey(
                seed,
                "mapping-assignment-shuffle",
                repetition.ToString(CultureInfo.InvariantCulture),
                index.ToString(CultureInfo.InvariantCulture))))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Phone)
            .ToArray();
        return symbols.Select((symbol, index) => (symbol, orderedAssignments[index]))
            .ToDictionary(pair => pair.symbol, pair => pair.Item2, StringComparer.Ordinal);
    }

    private static IReadOnlyList<double[]> ShufflePhones(
        IReadOnlyList<double[]> phones,
        int seed,
        int repetition,
        string queryId) =>
        [
            .. phones.Select((phone, index) => (Phone: phone, Key: BalancedSampler.StableKey(
                    seed,
                    "within-query-shuffle",
                    repetition.ToString(CultureInfo.InvariantCulture),
                    queryId,
                    index.ToString(CultureInfo.InvariantCulture))))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Phone.ToArray()),
        ];

    private static void AddModes(
        List<SequenceNullRecord> rows,
        string analysisId,
        string sampleId,
        int requestedSize,
        string nullKind,
        string language,
        SamplingQuery query,
        IReadOnlyList<double[]> phones,
        int repetition,
        int nullRepetition,
        int seed,
        string generatorVersion)
    {
        var nullId = string.Create(
            CultureInfo.InvariantCulture,
            $"null.{sampleId}.{nullKind}.{language}.{query.QueryId}.draw-{nullRepetition:D4}");
        rows.Add(Create("type-primary", 1));
        rows.Add(Create("token-weighted", query.TokenFrequency));

        SequenceNullRecord Create(string mode, int weight) => new(
            "1.0.0",
            analysisId,
            sampleId,
            requestedSize,
            nullId,
            true,
            nullKind,
            mode,
            weight,
            repetition,
            nullRepetition,
            seed,
            generatorVersion,
            language,
            query.QueryId,
            query.Symbols.Count,
            [.. phones.Select(phone => phone.ToArray())]);
    }

    private sealed class LanguagePhoneModel
    {
        private readonly Dictionary<(string Phone, int Remaining), bool> completionCache = [];
        private readonly double[][] unigrams;
        private readonly Dictionary<string, IReadOnlyList<double[]>> transitions;

        public LanguagePhoneModel(IEnumerable<SamplingCandidate> candidates)
        {
            IReadOnlyList<double[]>[] sequences = [.. candidates.Select(candidate => candidate.Phones).Where(phones => phones.Count != 0)];
            unigrams = [.. sequences.SelectMany(phones => phones).Select(phone => phone.ToArray())];
            if (unigrams.Length == 0)
            {
                throw new InvalidDataException("Pseudoword generation requires non-empty candidate phonologies.");
            }

            transitions = sequences
                .SelectMany(sequence => sequence.Zip(sequence.Skip(1), (left, right) => (Left: Key(left), Right: right)))
                .GroupBy(pair => pair.Left, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<double[]>)[.. group.Select(pair => pair.Right).OrderBy(Key, StringComparer.Ordinal)],
                    StringComparer.Ordinal);
        }

        public double[][] GenerateUnigram(int length, int seed, int repetition, string queryId) =>
            [.. Enumerable.Range(0, length)
                .Select(index => (double[])[.. Select(unigrams, seed, "unigram", repetition, queryId, index)])];

        public List<double[]> GenerateBiphone(int length, int seed, int repetition, string queryId)
        {
            if (length == 1)
            {
                return [[.. Select(unigrams, seed, "biphone-start", repetition, queryId, 0)]];
            }

            double[][] starts = [.. unigrams.Where(phone => CanComplete(Key(phone), length - 1))];
            if (starts.Length == 0)
            {
                throw new InvalidDataException($"No observed biphone walk can produce length {length}.");
            }

            List<double[]> result = [[.. Select(starts, seed, "biphone-start", repetition, queryId, 0)]];
            for (var index = 1; index < length; index++)
            {
                var remaining = length - index - 1;
                double[][] next = [.. transitions[Key(result[^1])].Where(phone => CanComplete(Key(phone), remaining))];
                result.Add([.. Select(next, seed, "biphone-next", repetition, queryId, index)]);
            }

            return result;
        }

        private bool CanComplete(string phone, int remaining)
        {
            if (remaining == 0)
            {
                return true;
            }

            var cacheKey = (phone, remaining);
            if (!completionCache.TryGetValue(cacheKey, out var result))
            {
                result = transitions.TryGetValue(phone, out var next) && next.Any(candidate => CanComplete(Key(candidate), remaining - 1));
                completionCache[cacheKey] = result;
            }

            return result;
        }

        private static double[] Select(
            double[][] choices,
            int seed,
            string kind,
            int repetition,
            string queryId,
            int index)
        {
            var key = BalancedSampler.StableKey(
                seed,
                kind,
                repetition.ToString(CultureInfo.InvariantCulture),
                queryId,
                index.ToString(CultureInfo.InvariantCulture));
            var offset = Convert.ToUInt64(key[..16], 16) % (ulong)choices.Length;
            return choices[(int)offset];
        }

        private static string Key(double[] phone) => string.Join(',', phone.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    }
}
