using System.Globalization;

namespace YATSS
{
    public enum RaceFormat { HeatRace, CombinedDistance }

    public sealed record DistanceApproval(
        int CompletedLaps, int? EstimatedPartialHundredths, int ApprovedPartialHundredths,
        DateTimeOffset RecordedAt, string Reason)
    {
        public long TotalHundredths => checked(CompletedLaps * 100L + ApprovedPartialHundredths);
    }

    public sealed record DistanceStanding(
        int RacerId, string RacerName, int Group, string Lane, long QualifyingHundredths,
        int RaceLaps, int? FinalPartialHundredths, long CombinedHundredths,
        long? ProjectedHundredths, int? BestLapMilliseconds, int? DirectorTieOrder);

    public static class DistanceScoring
    {
        public static string Format(long hundredths) =>
            (hundredths / 100m).ToString("0.00", CultureInfo.InvariantCulture);

        public static int? EstimatePartial(long sinceLastCrossing, IEnumerable<int> validLapTimes)
        {
            int[] recent = validLapTimes.Where(time => time > 0).TakeLast(5).ToArray();
            if (recent.Length == 0) return null;
            return (int)Math.Clamp(Math.Floor(Math.Max(0, sinceLastCrossing) * 100d / recent.Average()), 0, 99);
        }

        public static DistanceApproval Approve(int laps, int? estimate, int partial, string reason)
        {
            if (laps < 0 || partial is < 0 or > 99 || estimate is < 0 or > 99)
                throw new ArgumentOutOfRangeException(nameof(partial));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A confirmation reason is required.");
            return new(laps, estimate, partial, DateTimeOffset.Now, reason.Trim());
        }

        public static IReadOnlyList<DistanceStanding> Rank(IEnumerable<DistanceStanding> standings) =>
            standings.OrderByDescending(row => row.CombinedHundredths)
                .ThenBy(row => row.BestLapMilliseconds ?? int.MaxValue)
                .ThenBy(row => row.DirectorTieOrder ?? int.MaxValue)
                .ThenBy(row => row.RacerId).ToArray();
    }
}
