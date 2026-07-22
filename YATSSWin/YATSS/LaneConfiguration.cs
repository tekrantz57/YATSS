namespace YATSS
{
    public sealed record LaneConfiguration(string Name, int ColorArgb)
    {
        public Color Color => Color.FromArgb(ColorArgb);

        public static IReadOnlyList<LaneConfiguration> CreateDefaults() =>
            new[]
            {
                new LaneConfiguration("Red", Color.Red.ToArgb()),
                new LaneConfiguration("White", Color.White.ToArgb()),
                new LaneConfiguration("Green", Color.LimeGreen.ToArgb()),
                new LaneConfiguration("Orange", Color.Orange.ToArgb()),
                new LaneConfiguration("Blue", Color.CornflowerBlue.ToArgb()),
                new LaneConfiguration("Yellow", Color.Yellow.ToArgb()),
                new LaneConfiguration("Purple", Color.Violet.ToArgb()),
                new LaneConfiguration("Black", Color.LightGray.ToArgb())
            };
    }
}
