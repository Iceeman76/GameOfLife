using System.Runtime.InteropServices;

namespace GameOfLife.Simulation;

public sealed class LifeBoard(int columns, int rows, bool wrapEdges = false)
{
    private static readonly CoordinateComparer CellComparer = new();
    private byte[] cells = new byte[columns * rows];
    private byte[] nextCells = new byte[columns * rows];
    private HashSet<long> outsideCells = new(CellComparer);
    private HashSet<long> nextOutsideCells = new(CellComparer);
    private readonly Dictionary<long, byte> outsideNeighbors = new(CellComparer);
    private bool wrapping = wrapEdges;

    public int Columns { get; } = columns;
    public int Rows { get; } = rows;
    public int Generation { get; private set; }
    public int Population { get; private set; }
    public int VisiblePopulation => Population - outsideCells.Count;
    public int OutsidePopulation => outsideCells.Count;
    public byte[] Cells => cells;
    public bool WrapEdges
    {
        get => wrapping;
        set
        {
            wrapping = value;

            if (value)
            {
                Population -= outsideCells.Count;
                outsideCells.Clear();
            }
        }
    }

    public IEnumerable<(int Column, int Row)> LiveCells
    {
        get
        {
            for (var index = 0; index < cells.Length; index++)
            {
                if (cells[index] is 1)
                {
                    yield return (index % Columns, index / Columns);
                }
            }

            foreach (var cell in OutsideLiveCells)
            {
                yield return cell;
            }
        }
    }

    public IEnumerable<(int Column, int Row)> OutsideLiveCells
    {
        get
        {
            foreach (var cell in outsideCells)
            {
                yield return ((int)(cell >> 32), (int)cell);
            }
        }
    }

    internal void RestoreGeneration(int generation) => Generation = generation;

    public LifeBoard Copy()
    {
        var copy = new LifeBoard(Columns, Rows, WrapEdges);

        foreach (var cell in LiveCells)
        {
            copy.Toggle(cell.Column, cell.Row);
        }

        copy.RestoreGeneration(Generation);

        return copy;
    }

    public bool IsAlive(int column, int row) => IsVisible(column, row) ? cells[row * Columns + column] is 1 : outsideCells.Contains(CellKey(column, row));

    public void Toggle(int column, int row)
    {
        if (IsVisible(column, row) is false)
        {
            if (outsideCells.Remove(CellKey(column, row)))
            {
                Population--;
            }
            else
            {
                outsideCells.Add(CellKey(column, row));
                Population++;
            }

            return;
        }

        var index = row * Columns + column;
        cells[index] ^= 1;
        Population += cells[index] is 1 ? 1 : -1;
    }

    public void Clear()
    {
        Array.Clear(cells);
        outsideCells.Clear();
        Generation = 0;
        Population = 0;
    }

    public void Randomize(Random random)
    {
        Clear();

        for (var index = 0; index < cells.Length; index++)
        {
            cells[index] = random.NextDouble() < 0.28 ? (byte)1 : (byte)0;
            Population += cells[index];
        }
    }

    public void Load(Pattern pattern)
    {
        Clear();
        var left = (Columns - pattern.Rows[0].Length) / 2;
        var top = (Rows - pattern.Rows.Count) / 2;

        for (var row = 0; row < pattern.Rows.Count; row++)
        {
            var patternRow = pattern.Rows[row];

            for (var column = 0; column < patternRow.Length; column++)
            {
                if (patternRow[column] is 'O')
                {
                    Toggle(left + column, top + row);
                }
            }
        }
    }

    public void Advance()
    {
        var population = 0;
        outsideNeighbors.Clear();

        foreach (var cell in outsideCells)
        {
            CountOutsideNeighbors((int)(cell >> 32), (int)cell);
        }

        for (var row = 0; row < Rows; row++)
        {
            var rowStart = row * Columns;
            var aboveStart = (row is 0 ? Rows - 1 : row - 1) * Columns;
            var belowStart = (row == Rows - 1 ? 0 : row + 1) * Columns;

            for (var column = 0; column < Columns; column++)
            {
                var left = column is 0 ? Columns - 1 : column - 1;
                var right = column == Columns - 1 ? 0 : column + 1;
                var index = rowStart + column;
                var neighbors = WrapEdges || (row > 0 && row < Rows - 1 && column > 0 && column < Columns - 1)
                    ? cells[aboveStart + left] + cells[aboveStart + column] + cells[aboveStart + right]
                    + cells[rowStart + left] + cells[rowStart + right]
                    + cells[belowStart + left] + cells[belowStart + column] + cells[belowStart + right]
                    : CountUnboundedNeighbors(column, row);

                if (WrapEdges is false && cells[index] is 1 && (row is 0 || row == Rows - 1 || column is 0 || column == Columns - 1))
                {
                    CountOutsideNeighbors(column, row);
                }

                var survives = neighbors is 3 || (neighbors is 2 && cells[index] is 1);
                nextCells[index] = survives ? (byte)1 : (byte)0;
                population += nextCells[index];
            }
        }

        (cells, nextCells) = (nextCells, cells);
        nextOutsideCells.Clear();

        foreach (var (cell, neighbors) in outsideNeighbors)
        {
            if (neighbors is 3 || (neighbors is 2 && outsideCells.Contains(cell)))
            {
                nextOutsideCells.Add(cell);
            }
        }

        (outsideCells, nextOutsideCells) = (nextOutsideCells, outsideCells);
        Generation++;
        Population = population + outsideCells.Count;
    }

    private int CountUnboundedNeighbors(int column, int row)
    {
        var neighbors = 0;

        for (var neighborRow = row - 1; neighborRow <= row + 1; neighborRow++)
        {
            for (var neighborColumn = column - 1; neighborColumn <= column + 1; neighborColumn++)
            {
                if (neighborColumn == column && neighborRow == row)
                {
                    continue;
                }

                neighbors += IsAlive(neighborColumn, neighborRow) ? 1 : 0;
            }
        }

        return neighbors;
    }

    private void CountOutsideNeighbors(int column, int row)
    {
        for (var rowOffset = -1; rowOffset <= 1; rowOffset++)
        {
            for (var columnOffset = -1; columnOffset <= 1; columnOffset++)
            {
                var neighbor = (Column: column + columnOffset, Row: row + rowOffset);

                if ((columnOffset is 0 && rowOffset is 0) || IsVisible(neighbor.Column, neighbor.Row))
                {
                    continue;
                }

                ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(outsideNeighbors, CellKey(neighbor.Column, neighbor.Row), out _);
                count++;
            }
        }
    }

    private bool IsVisible(int column, int row) => column >= 0 && column < Columns && row >= 0 && row < Rows;

    private static long CellKey(int column, int row) => ((long)column << 32) | (uint)row;

    private sealed class CoordinateComparer : IEqualityComparer<long>
    {
        public bool Equals(long first, long second) => first == second;

        public int GetHashCode(long coordinate) => unchecked((int)(coordinate >> 32) * 73856093 ^ (int)coordinate * 19349663);
    }
}
