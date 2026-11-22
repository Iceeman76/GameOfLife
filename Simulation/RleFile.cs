using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GameOfLife.Simulation;

public static partial class RleFile
{
    public static string Save(LifeBoard board)
    {
        var liveCells = board.LiveCells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column).ToArray();
        var left = liveCells.Length is 0 ? 0 : Math.Min(0, liveCells.Min(cell => cell.Column));
        var top = liveCells.Length is 0 ? 0 : Math.Min(0, liveCells.Min(cell => cell.Row));
        var right = liveCells.Length is 0 ? board.Columns - 1 : Math.Max(board.Columns - 1, liveCells.Max(cell => cell.Column));
        var bottom = liveCells.Length is 0 ? board.Rows - 1 : Math.Max(board.Rows - 1, liveCells.Max(cell => cell.Row));
        var output = new StringBuilder();
        output.AppendLine($"#CXRLE Pos={left - board.Columns / 2},{top - board.Rows / 2} Gen={board.Generation}");

        if (board.WrapEdges is false)
        {
            output.AppendLine($"#C GameOfLife Viewport={board.Columns},{board.Rows}");
        }

        var rule = board.WrapEdges ? $"B3/S23:T{board.Columns},{board.Rows}" : "B3/S23";
        output.AppendLine($"x = {(long)right - left + 1}, y = {(long)bottom - top + 1}, rule = {rule}");
        var lineLength = 0;
        var previousRow = top;

        foreach (var row in liveCells.GroupBy(cell => cell.Row))
        {
            if (row.Key > previousRow)
            {
                AppendRun(output, row.Key - previousRow, '$', ref lineLength);
            }

            var rowCells = row.ToArray();
            var column = left;
            var index = 0;

            while (index < rowCells.Length)
            {
                var startColumn = rowCells[index].Column;

                if (startColumn > column)
                {
                    AppendRun(output, startColumn - column, 'b', ref lineLength);
                }

                var run = 1;
                index++;

                while (index < rowCells.Length && rowCells[index].Column == startColumn + run)
                {
                    run++;
                    index++;
                }

                AppendRun(output, run, 'o', ref lineLength);
                column = startColumn + run;
            }

            previousRow = row.Key;
        }

        output.AppendLine("!");

        return output.ToString();
    }

    public static LifeBoard Load(string text, GridSize? viewportSize = null)
    {
        var lines = text.TrimStart('\uFEFF').Split('\n').Select(line => line.Trim()).ToArray();
        var content = lines.Where(line => line.Length > 0 && line.StartsWith('#') is false).ToArray();

        if (content.Length is 0)
        {
            throw new FormatException("The file does not contain an RLE header.");
        }

        var header = HeaderPattern().Match(content[0]);

        if (header.Success is false)
        {
            throw new FormatException("Expected an RLE header: x = width, y = height, rule = B3/S23.");
        }

        var width = ReadNumber(header.Groups[1].Value);
        var height = ReadNumber(header.Groups[2].Value);
        var rule = Regex.Replace(header.Groups[3].Value, @"\s+", "").ToUpperInvariant();
        var ruleParts = rule.Split(':');

        if (ruleParts[0] is not ("" or "B3/S23" or "S23/B3" or "23/3") || ruleParts.Length > 2)
        {
            throw new FormatException("Only Conway's Game of Life (B3/S23) is supported.");
        }

        var columns = Math.Max(40, width);
        var rows = Math.Max(24, height);
        var bounded = ruleParts.Length is 2;
        var wrapEdges = false;
        var viewport = ViewportPattern().Match(text);

        if (bounded is false && viewport.Success)
        {
            columns = ReadNumber(viewport.Groups[1].Value);
            rows = ReadNumber(viewport.Groups[2].Value);
        }

        if (bounded is false && viewportSize is not null)
        {
            columns = viewportSize.Columns;
            rows = viewportSize.Rows;
        }

        if (bounded)
        {
            var topology = GridPattern().Match(ruleParts[1]);

            if (topology.Success is false)
            {
                throw new FormatException("Only finite planes (:Pwidth,height) and unshifted tori (:Twidth,height) are supported.");
            }

            wrapEdges = topology.Groups[1].Value is "T";
            columns = ReadNumber(topology.Groups[2].Value);
            rows = topology.Groups[3].Success ? ReadNumber(topology.Groups[3].Value) : columns;
        }

        if (columns < 1 || rows < 1)
        {
            throw new FormatException($"The file declares a {columns} × {rows} grid. Both grid dimensions must be greater than zero.");
        }

        if (bounded && (width > columns || height > rows))
        {
            throw new FormatException(FormattableString.Invariant($"The pattern requires {width} × {height} cells ({(long)width * height:N0} grid cells), but the file declares a {columns} × {rows} grid."));
        }

        var cellCount = (long)columns * rows;

        if (columns > GridSize.MaximumDimension || rows > GridSize.MaximumDimension || cellCount > GridSize.MaximumCellCount)
        {
            var exceededLimits = new List<string>();

            if (columns > GridSize.MaximumDimension || rows > GridSize.MaximumDimension)
            {
                exceededLimits.Add(FormattableString.Invariant($"the per-side limit of {GridSize.MaximumDimension:N0} cells"));
            }

            if (cellCount > GridSize.MaximumCellCount)
            {
                exceededLimits.Add(FormattableString.Invariant($"the total limit of {GridSize.MaximumCellCount:N0} cell positions"));
            }

            throw new FormatException(FormattableString.Invariant($"The {columns} × {rows} grid needs {cellCount:N0} cell positions, exceeding {string.Join(" and ", exceededLimits)}."));
        }

        var left = (columns - width) / 2;
        var top = (rows - height) / 2;
        var generation = 0;

        foreach (var line in lines.Where(line => line.StartsWith("#CXRLE", StringComparison.Ordinal)))
        {
            var generationMatch = GenerationPattern().Match(line);

            if (generationMatch.Success)
            {
                generation = ReadNumber(generationMatch.Groups[1].Value);
            }

            var position = PositionPattern().Match(line);

            if ((bounded || viewport.Success) && position.Success)
            {
                var positionColumn = (long)ReadCoordinate(position.Groups[1].Value) + columns / 2;
                var positionRow = (long)ReadCoordinate(position.Groups[2].Value) + rows / 2;

                if (bounded && wrapEdges is false && (positionColumn < 0 || positionRow < 0 || positionColumn + width > columns || positionRow + height > rows))
                {
                    throw new FormatException($"The {width} × {height} pattern at Pos={position.Groups[1].Value},{position.Groups[2].Value} extends outside the file's {columns} × {rows} plane.");
                }

                left = wrapEdges ? Wrap(positionColumn, columns) : (int)positionColumn;
                top = wrapEdges ? Wrap(positionRow, rows) : (int)positionRow;
            }
        }

        if ((long)left + width > int.MaxValue || (long)top + height > int.MaxValue)
        {
            throw new FormatException("The pattern coordinates are too large.");
        }

        var board = new LifeBoard(columns, rows, wrapEdges);
        ReadCells(board, string.Concat(content.Skip(1)), width, height, left, top);
        board.RestoreGeneration(generation);

        return board;
    }

    private static void ReadCells(LifeBoard board, string body, int width, int height, int left, int top)
    {
        var column = 0;
        var row = 0;
        var count = 0;
        var hasCount = false;
        var ended = false;

        foreach (var symbol in body)
        {
            if (char.IsWhiteSpace(symbol))
            {
                if (hasCount)
                {
                    throw new FormatException("A run count must be directly followed by its symbol.");
                }

                continue;
            }

            if (ended)
            {
                throw new FormatException("Unexpected data after the RLE end marker.");
            }

            if (symbol is >= '0' and <= '9')
            {
                if ((hasCount is false && symbol is '0') || count > (int.MaxValue - (symbol - '0')) / 10)
                {
                    throw new FormatException("Invalid or oversized RLE run count.");
                }

                count = count * 10 + symbol - '0';
                hasCount = true;

                continue;
            }

            var run = hasCount ? count : 1;

            if (symbol is '!')
            {
                if (hasCount)
                {
                    throw new FormatException("The end marker cannot have a run count.");
                }

                ended = true;
            }
            else if (symbol is '$')
            {
                if ((long)row + run > height)
                {
                    throw new FormatException("The pattern exceeds its declared height.");
                }

                row += run;
                column = 0;
            }
            else if (symbol is 'o' or 'b')
            {
                if (row >= height || (long)column + run > width)
                {
                    throw new FormatException("The pattern exceeds its declared dimensions.");
                }

                if (symbol is 'o')
                {
                    if ((long)board.Population + run > 2_000_000)
                    {
                        throw new FormatException("The file contains more than 2,000,000 live cells.");
                    }

                    for (var offset = 0; offset < run; offset++)
                    {
                        var targetColumn = left + column + offset;
                        var targetRow = top + row;
                        board.Toggle(board.WrapEdges ? Wrap(targetColumn, board.Columns) : targetColumn, board.WrapEdges ? Wrap(targetRow, board.Rows) : targetRow);
                    }
                }

                column += run;
            }
            else
            {
                throw new FormatException($"Unsupported RLE symbol: {symbol}.");
            }

            count = 0;
            hasCount = false;
        }

        if (ended is false)
        {
            throw new FormatException("The RLE end marker (!) is missing.");
        }
    }

    private static void AppendRun(StringBuilder output, int count, char symbol, ref int lineLength)
    {
        var token = count is 1 ? symbol.ToString() : count.ToString(CultureInfo.InvariantCulture) + symbol;

        if (lineLength + token.Length > 69)
        {
            output.AppendLine();
            lineLength = 0;
        }

        output.Append(token);
        lineLength += token.Length;
    }

    private static int ReadNumber(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) is false)
        {
            throw new FormatException("An RLE number is invalid or too large.");
        }

        return number;
    }

    private static int ReadCoordinate(string value)
    {
        if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) is false)
        {
            throw new FormatException("An RLE position is invalid or too large.");
        }

        return number;
    }

    private static int Wrap(long coordinate, int size) => (int)((coordinate % size + size) % size);

    [GeneratedRegex(@"^x\s*=\s*(\d+)\s*,\s*y\s*=\s*(\d+)\s*(?:,\s*rule\s*=\s*(.+?))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^([PT])(\d+)(?:,(\d+))?$")]
    private static partial Regex GridPattern();

    [GeneratedRegex(@"\bGen=(\d+)\b")]
    private static partial Regex GenerationPattern();

    [GeneratedRegex(@"\bPos=([+-]?\d+),([+-]?\d+)\b")]
    private static partial Regex PositionPattern();

    [GeneratedRegex(@"^#C GameOfLife Viewport=(\d+),(\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex ViewportPattern();
}

