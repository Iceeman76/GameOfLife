using GameOfLife.Simulation;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace GameOfLife.Pages;

public sealed partial class Home
{
    private LifeBoard board = new(80, 48);
    private LifeBoard? initialBoard;
    private PeriodicTimer? timer;
    private string gridSize = "80x48";
    private int patternIndex = 9;
    private int speed = 6;
    private bool showGrid = true;
    private bool autoGrow;
    private byte[] outsideCoordinates = [];
    private ElementReference boardCanvas;
    private ElementReference simulationPanel;
    private IJSInProcessObjectReference? boardModule;
    private IJSInProcessObjectReference? canvasRenderer;
    private DotNetObjectReference<Home>? canvasCallbacks;
    private string? renderingError;
    private string? fileStatus;
    private bool fileFailed;
    private string? fullscreenError;
    private int patternRequest;
    private int viewRevision;

    [Inject]
    private IJSRuntime JavaScript { get; init; } = default!;

    [Inject]
    private HttpClient Http { get; init; } = default!;

    private bool IsRunning => timer is not null;
    private int VisibleCells => autoGrow && board.WrapEdges is false ? board.Population : board.VisiblePopulation;
    private TimeSpan TickInterval => TimeSpan.FromSeconds(1d / speed);
    private bool IsCustomGridSize => GridSize.Presets.Any(size => size.Columns == board.Columns && size.Rows == board.Rows) is false;

    protected override Task OnInitializedAsync() => LoadPatternAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (renderingError is not null)
        {
            return;
        }

        try
        {
            if (firstRender)
            {
                boardModule = await JavaScript.InvokeAsync<IJSInProcessObjectReference>("import", "./board.js");
                canvasCallbacks = DotNetObjectReference.Create(this);
                canvasRenderer = boardModule.Invoke<IJSInProcessObjectReference>("createBoard", boardCanvas, canvasCallbacks);
            }

            UpdateOutsideCoordinates();
            canvasRenderer?.InvokeVoid("render", board.Cells, board.Columns, board.Rows, showGrid, outsideCoordinates, autoGrow && board.WrapEdges is false, viewRevision);

            if (firstRender)
            {
                StateHasChanged();
            }
        }
        catch (JSException exception)
        {
            Console.Error.WriteLine(exception);
            Stop();
            renderingError = "The grid could not be rendered. Reload the page to try again.";
            StateHasChanged();
        }
    }

    private void UpdateOutsideCoordinates()
    {
        if ((outsideCoordinates.Length == board.OutsidePopulation * 8) is false)
        {
            outsideCoordinates = new byte[board.OutsidePopulation * 8];
        }

        var coordinates = MemoryMarshal.Cast<byte, int>(outsideCoordinates);
        var index = 0;

        foreach (var cell in board.OutsideLiveCells)
        {
            coordinates[index++] = cell.Column;
            coordinates[index++] = cell.Row;
        }
    }

    private async Task ToggleRunningAsync()
    {
        if (IsRunning)
        {
            Stop();

            return;
        }

        await RunAsync();
    }

    private async Task RunAsync()
    {
        CaptureConstruction();
        using var runningTimer = new PeriodicTimer(TickInterval);
        timer = runningTimer;

        while (await runningTimer.WaitForNextTickAsync() && ReferenceEquals(timer, runningTimer))
        {
            board.Advance();
            StateHasChanged();
        }
    }

    private void CaptureConstruction()
    {
        if (board.Generation is 0)
        {
            RememberInitialState();
        }
    }

    private void RememberInitialState()
    {
        initialBoard = board.Copy();
        initialBoard.RestoreGeneration(0);
    }

    private void Advance()
    {
        CaptureConstruction();
        board.Advance();
    }

    private void Reset()
    {
        Stop();
        patternRequest++;

        if (board.Generation > 0)
        {
            board = initialBoard!.Copy();
            gridSize = new GridSize(board.Columns, board.Rows).Value;
        }

        viewRevision++;
    }

    private void Stop()
    {
        var runningTimer = timer;
        timer = null;
        runningTimer?.Dispose();
    }

    private void UpdateSpeed()
    {
        if (timer is not null)
        {
            timer.Period = TickInterval;
        }
    }

    private async Task LoadFileAsync(InputFileChangeEventArgs change)
    {
        patternRequest++;
        Stop();
        fileStatus = null;
        fileFailed = false;

        try
        {
            await using var stream = change.File.OpenReadStream(2 * 1024 * 1024);
            using var reader = new StreamReader(stream);
            var loadedBoard = RleFile.Load(await reader.ReadToEndAsync());
            board = loadedBoard;
            RememberInitialState();
            viewRevision++;
            gridSize = new GridSize(board.Columns, board.Rows).Value;
            fileStatus = $"Loaded {change.File.Name}. The simulation is paused.";
        }
        catch (Exception exception) when (exception is FormatException or IOException)
        {
            fileFailed = true;
            fileStatus = $"Could not load the file. {exception.Message}";
        }
    }

    private void SaveFile()
    {
        fileFailed = false;

        try
        {
            boardModule?.InvokeVoid("downloadRle", $"game-of-life-{board.Columns}x{board.Rows}-gen-{board.Generation}.rle", RleFile.Save(board));
            fileStatus = "The RLE file is ready to save.";
        }
        catch (JSException)
        {
            fileFailed = true;
            fileStatus = "The file could not be downloaded. Try again.";
        }
    }

    private void Clear()
    {
        patternRequest++;
        Stop();
        board.Clear();
        RememberInitialState();
        viewRevision++;
    }

    private void Randomize()
    {
        patternRequest++;
        Stop();
        board.Randomize(Random.Shared);
        RememberInitialState();
        viewRevision++;
    }

    private async Task LoadPatternAsync()
    {
        Stop();
        var request = ++patternRequest;
        var pattern = Pattern.Presets[patternIndex];

        if (pattern.FileName is not null)
        {
            try
            {
                var text = await Http.GetStringAsync($"patterns/{pattern.FileName}");

                if (request == patternRequest)
                {
                    Stop();
                    var size = pattern.Size!;
                    var fits = size.Columns <= GridSize.MaximumDimension && size.Rows <= GridSize.MaximumDimension && (long)size.Columns * size.Rows <= GridSize.MaximumCellCount;
                    board = RleFile.Load(text, fits ? size : new GridSize(512, 512));
                    RememberInitialState();
                    viewRevision++;
                    gridSize = new GridSize(board.Columns, board.Rows).Value;
                    fileFailed = false;
                    fileStatus = fits ? $"Loaded {pattern.Name}." : $"Loaded {pattern.Name} ({size.Columns} × {size.Rows}). {(autoGrow ? "Auto grow shows the entire pattern." : "Enable Auto grow to see the entire pattern.")}";
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or FormatException)
            {
                if (request == patternRequest)
                {
                    fileFailed = true;
                    fileStatus = $"Could not load {pattern.Name}. {exception.Message}";
                }
            }

            return;
        }

        if (pattern.Rows[0].Length > board.Columns || pattern.Rows.Count > board.Rows)
        {
            gridSize = GridSize.Presets.First(size => size.Columns >= pattern.Rows[0].Length && size.Rows >= pattern.Rows.Count).Value;
            ResizeGrid();
        }

        board.Load(pattern);
        RememberInitialState();
        viewRevision++;
    }

    private async Task ToggleFullscreenAsync()
    {
        fullscreenError = null;

        try
        {
            await boardModule!.InvokeVoidAsync("toggleFullscreen", simulationPanel);
        }
        catch (JSException)
        {
            fullscreenError = "Fullscreen could not be opened in this browser.";
        }
    }

    private void ResizeGrid()
    {
        patternRequest++;
        Stop();
        var dimensions = gridSize.Split('x');
        var resizedBoard = new LifeBoard(int.Parse(dimensions[0]), int.Parse(dimensions[1]), board.WrapEdges);
        var columnOffset = (resizedBoard.Columns - board.Columns) / 2;
        var rowOffset = (resizedBoard.Rows - board.Rows) / 2;

        foreach (var cell in board.LiveCells)
        {
            var targetColumn = cell.Column + columnOffset;
            var targetRow = cell.Row + rowOffset;

            if (board.WrapEdges is false || (targetColumn >= 0 && targetColumn < resizedBoard.Columns && targetRow >= 0 && targetRow < resizedBoard.Rows))
            {
                resizedBoard.Toggle(targetColumn, targetRow);
            }
        }

        board = resizedBoard;
        RememberInitialState();
        viewRevision++;
    }

    [JSInvokable]
    public void PaintStroke(int[] points, bool alive)
    {
        for (var point = 0; point < points.Length; point += 4)
        {
            var startColumn = board.WrapEdges ? Math.Clamp(points[point], 0, board.Columns - 1) : points[point];
            var startRow = board.WrapEdges ? Math.Clamp(points[point + 1], 0, board.Rows - 1) : points[point + 1];
            var endColumn = board.WrapEdges ? Math.Clamp(points[point + 2], 0, board.Columns - 1) : points[point + 2];
            var endRow = board.WrapEdges ? Math.Clamp(points[point + 3], 0, board.Rows - 1) : points[point + 3];
            var steps = Math.Max(1, Math.Max(Math.Abs(endColumn - startColumn), Math.Abs(endRow - startRow)));

            for (var step = 0; step <= steps; step++)
            {
                var column = startColumn + (int)Math.Round((endColumn - startColumn) * (double)step / steps);
                var row = startRow + (int)Math.Round((endRow - startRow) * (double)step / steps);

                if (board.IsAlive(column, row) == alive)
                {
                    continue;
                }

                board.Toggle(column, row);
            }
        }

        StateHasChanged();
    }

    public void Dispose()
    {
        patternRequest++;
        Stop();

        try
        {
            canvasRenderer?.InvokeVoid("dispose");
        }
        catch (JSDisconnectedException)
        {
        }
        finally
        {
            canvasRenderer?.Dispose();
            boardModule?.Dispose();
            canvasCallbacks?.Dispose();
        }
    }
}

