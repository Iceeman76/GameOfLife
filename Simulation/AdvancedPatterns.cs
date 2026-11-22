namespace GameOfLife.Simulation;

public static class AdvancedPatterns
{
    public static IReadOnlyList<Pattern> Presets { get; } =
    [
        new("Primer", [], "Advanced · Engineered", "primer.rle", new GridSize(440, 294)),
        new("Gunstar", [], "Advanced · Gun", "gunstar.rle", new GridSize(149, 149)),
        new("Infinite glider hotel", [], "Advanced · Growth", "infinitegliderhotel.rle", new GridSize(566, 572)),
        new("Infinite LWSS hotel", [], "Advanced · Spaceship", "infinitelwsshotel.rle", new GridSize(515, 298)),
        new("Turing machine", [], "Advanced · Computation", "turingmachine.rle", new GridSize(1714, 1647)),
        new("Breeder 1", [], "Advanced · Growth", "breeder1.rle", new GridSize(749, 338)),
        new("3-engine Cordership gun", [], "Advanced · Gun", "3enginecordershipgun.rle", new GridSize(279, 258)),
        new("Tethered rake", [], "Advanced · Growth", "tetheredrake.rle", new GridSize(134, 92)),
        new("Hacksaw", [], "Advanced · Engineered", "hacksaw.rle", new GridSize(199, 102)),
        new("c/5 greyship", [], "Advanced · Spaceship", "c5greyship.rle", new GridSize(286, 199)),
        new("P94S", [], "Advanced · Spaceship", "p94s.rle", new GridSize(607, 155)),
        new("log(t)^2 growth", [], "Advanced · Growth", "logt2growth.rle", new GridSize(290, 218)),
        new("t log(t) growth", [], "Advanced · Growth", "tlogtgrowth.rle", new GridSize(635, 377))
    ];
}
