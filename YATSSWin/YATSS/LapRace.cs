namespace YATSS
{
    public sealed record LapRaceOptions(
        int MinLapMilliseconds,
        int MaxLapMilliseconds,
        double TrackLengthFeet,
        int RawSensorLockoutMilliseconds = 0)
    {
        public static LapRaceOptions Default { get; } = new(1000, 600000, 155.0);
    }

    public enum LapUpdateKind
    {
        Started,
        Counted,
        Duplicate,
        RawIgnored,
        TooFast,
        MissedFrame,
        Invalid
    }

    public sealed record LapUpdate(
        LapUpdateKind Kind,
        int LaneIndex,
        int? LapMilliseconds,
        int MissedFrames,
        string Detail);

    public sealed class LapRace
    {
        private sealed class LaneRuntime
        {
            public LaneRuntime(int laneIndex)
            {
                Stats = new Lane(laneIndex);
            }

            public Lane Stats { get; private set; }
            public uint? LastAcceptedTimestamp { get; set; }
            public uint? LastRawTimestamp { get; set; }
            public uint? LastSequence { get; set; }
            public int MissedFrames { get; set; }

            public void Reset(int laneIndex)
            {
                Stats = new Lane(laneIndex);
                LastAcceptedTimestamp = null;
                LastRawTimestamp = null;
                LastSequence = null;
                MissedFrames = 0;
            }
        }

        private readonly LaneRuntime[] _lanes;
        private LapRaceOptions _options;
        private readonly object _gate = new();

        public LapRace(LapRaceOptions? options = null)
        {
            _options = options ?? LapRaceOptions.Default;
            _lanes = Enumerable.Range(0, LapProtocolParser.LaneCount)
                .Select(i => new LaneRuntime(i))
                .ToArray();
        }

        public LapRaceOptions Options
        {
            get
            {
                lock (_gate)
                {
                    return _options;
                }
            }
        }

        public void SetOptions(LapRaceOptions options)
        {
            lock (_gate)
            {
                _options = options;
            }
        }

        public Lane GetLane(int laneIndex)
        {
            lock (_gate)
            {
                return _lanes[laneIndex].Stats;
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                for (int i = 0; i < _lanes.Length; i++)
                {
                    _lanes[i].Reset(i);
                }
            }
        }

        public void ResetLane(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= _lanes.Length)
            {
                return;
            }

            lock (_gate)
            {
                _lanes[laneIndex].Reset(laneIndex);
            }
        }

        public LapUpdate Process(LapEdge edge) =>
            Process(edge, countFirstEdgeAsLap: false, fastestLapEligible: true);

        public LapUpdate Process(
            LapEdge edge,
            bool countFirstEdgeAsLap,
            bool fastestLapEligible,
            int? firstLapMilliseconds = null)
        {
            if (edge.LaneIndex < 0 || edge.LaneIndex >= _lanes.Length)
            {
                return new LapUpdate(LapUpdateKind.Invalid, edge.LaneIndex, null, 0, "lane out of range");
            }

            lock (_gate)
            {
                LaneRuntime lane = _lanes[edge.LaneIndex];
                int missedFrames = 0;

                if (edge.Sequence.HasValue && lane.LastSequence.HasValue)
                {
                    uint sequenceDelta = unchecked(edge.Sequence.Value - lane.LastSequence.Value);
                    if (sequenceDelta == 0)
                    {
                        return new LapUpdate(LapUpdateKind.Duplicate, edge.LaneIndex, null, lane.MissedFrames, "duplicate serial frame");
                    }

                    if (sequenceDelta > int.MaxValue)
                    {
                        return new LapUpdate(LapUpdateKind.Duplicate, edge.LaneIndex, null, lane.MissedFrames, "stale serial frame");
                    }

                    if (sequenceDelta > 1)
                    {
                        missedFrames = (int)sequenceDelta - 1;
                        lane.MissedFrames += missedFrames;
                    }
                }

                lane.LastSequence = edge.Sequence ?? lane.LastSequence;

                int rawSensorLockout = Math.Max(0, _options.RawSensorLockoutMilliseconds);
                if (rawSensorLockout > 0 && lane.LastRawTimestamp.HasValue)
                {
                    uint rawElapsed = unchecked(edge.TimestampMillis - lane.LastRawTimestamp.Value);
                    lane.LastRawTimestamp = edge.TimestampMillis;
                    if (rawElapsed < rawSensorLockout)
                    {
                        return new LapUpdate(
                            LapUpdateKind.RawIgnored,
                            edge.LaneIndex,
                            (int)Math.Min(rawElapsed, int.MaxValue),
                            lane.MissedFrames,
                            $"ignored {rawElapsed} ms raw edge below sensor lockout {rawSensorLockout} ms");
                    }
                }
                else
                {
                    lane.LastRawTimestamp = edge.TimestampMillis;
                }

                if (!lane.LastAcceptedTimestamp.HasValue)
                {
                    lane.LastAcceptedTimestamp = edge.TimestampMillis;
                    if (countFirstEdgeAsLap)
                    {
                        if (firstLapMilliseconds.HasValue)
                        {
                            lane.Stats.AddLap(firstLapMilliseconds.Value, eligibleForBest: false);
                            return new LapUpdate(
                                LapUpdateKind.Counted,
                                edge.LaneIndex,
                                firstLapMilliseconds.Value,
                                lane.MissedFrames,
                                "first edge counted with heat-start timing; excluded from fastest lap");
                        }

                        lane.Stats.AddLapCountOnly();
                        return new LapUpdate(LapUpdateKind.Counted, edge.LaneIndex, null, lane.MissedFrames, "first edge counted without lap timing");
                    }

                    return new LapUpdate(LapUpdateKind.Started, edge.LaneIndex, null, lane.MissedFrames, "first edge establishes baseline");
                }

                uint elapsed = unchecked(edge.TimestampMillis - lane.LastAcceptedTimestamp.Value);
                if (elapsed < _options.MinLapMilliseconds)
                {
                    return new LapUpdate(LapUpdateKind.TooFast, edge.LaneIndex, (int)elapsed, lane.MissedFrames, $"ignored {elapsed} ms lap below minimum {_options.MinLapMilliseconds} ms");
                }

                if (elapsed > _options.MaxLapMilliseconds || elapsed > int.MaxValue)
                {
                    lane.LastAcceptedTimestamp = edge.TimestampMillis;
                    return new LapUpdate(LapUpdateKind.Invalid, edge.LaneIndex, null, lane.MissedFrames, $"ignored implausible {elapsed} ms lap and reset baseline");
                }

                int lapMilliseconds = (int)elapsed;
                lane.LastAcceptedTimestamp = edge.TimestampMillis;
                lane.Stats.AddLap(lapMilliseconds, fastestLapEligible);

                return new LapUpdate(
                    missedFrames > 0 ? LapUpdateKind.MissedFrame : LapUpdateKind.Counted,
                    edge.LaneIndex,
                    lapMilliseconds,
                    lane.MissedFrames,
                    GetCountedDetail(missedFrames, fastestLapEligible));
            }
        }

        public int[] GetLapCounts()
        {
            lock (_gate)
            {
                return _lanes.Select(lane => lane.Stats.getCount()).ToArray();
            }
        }

        public int?[] GetBestLapMilliseconds()
        {
            lock (_gate)
            {
                return _lanes
                    .Select(lane => lane.Stats.best_time == int.MaxValue ? (int?)null : lane.Stats.best_time)
                    .ToArray();
            }
        }

        public int AdjustLapCount(int laneIndex, int delta)
        {
            if (laneIndex < 0 || laneIndex >= _lanes.Length || delta == 0)
            {
                return 0;
            }

            lock (_gate)
            {
                return _lanes[laneIndex].Stats.AdjustLapCount(delta);
            }
        }

        public void ResetTimingForHeat(IReadOnlyList<int> lapCounts)
        {
            lock (_gate)
            {
                for (int i = 0; i < _lanes.Length; i++)
                {
                    int lapCount = i < lapCounts.Count ? Math.Max(0, lapCounts[i]) : 0;
                    _lanes[i].Stats.ResetTiming(lapCount);
                    _lanes[i].LastAcceptedTimestamp = null;
                    _lanes[i].LastRawTimestamp = null;
                    _lanes[i].LastSequence = null;
                    _lanes[i].MissedFrames = 0;
                }
            }
        }

        private static string GetCountedDetail(int missedFrames, bool fastestLapEligible)
        {
            string detail = missedFrames > 0 ? $"counted lap after {missedFrames} missed frame(s)" : "counted lap";
            return fastestLapEligible ? detail : $"{detail}; fastest lap unchanged";
        }

        public double CalculateMilesPerHour(int lapMilliseconds)
        {
            if (lapMilliseconds <= 0)
            {
                return 0.0;
            }

            double seconds = lapMilliseconds / 1000.0;
            return (_options.TrackLengthFeet / seconds) / 1.467;
        }
    }
}
