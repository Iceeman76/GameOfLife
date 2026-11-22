namespace GameOfLife.Simulation;

public sealed record GridSize(int Columns, int Rows)
{
    public const int MaximumDimension = 1024;
    public const int MaximumCellCount = 262144;

    public string Value => $"{Columns}x{Rows}";

    public static IReadOnlyList<GridSize> Presets { get; } =
    [
        new(40, 24),
        new(60, 36),
        new(80, 48),
        new(120, 72),
        new(160, 96),
        new(200, 120),
        new(240, 144),
        new(320, 192),
        new(400, 240),
        new(480, 288),
        new(560, 336),
        new(640, 384),
        new(660, 396),
        new(512, 512),
        new(MaximumDimension, MaximumCellCount / MaximumDimension),
        new(MaximumCellCount / MaximumDimension, MaximumDimension)
    ];
}
