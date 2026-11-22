export async function toggleFullscreen(element) {
    if (document.fullscreenElement === element) {
        await document.exitFullscreen();
    } else {
        await element.requestFullscreen();
    }
}

export function downloadRle(filename, content) {
    const url = URL.createObjectURL(new Blob([content], { type: "text/plain;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = filename;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function createBoard(canvas, callbacks) {
    const context = canvas.getContext("2d", { alpha: false });
    const events = new AbortController();
    const viewSizeOutput = canvas.closest(".simulation").querySelector("[data-view-size]");
    let cells = new Uint8Array();
    let columns = 0;
    let rows = 0;
    let showGrid = true;
    let outside = new DataView(new ArrayBuffer(0));
    let autoGrow = false;
    let viewLeft = 0;
    let viewTop = 0;
    let viewColumns = 0;
    let viewRows = 0;
    let viewRevision = -1;
    let drawing = false;
    let drawingAlive = true;
    let previousCell;
    let pendingPoints = [];
    let paintFrame = 0;
    let drawFrame = 0;
    let disposed = false;
    let pendingPaint = Promise.resolve();

    function draw() {
        drawFrame = 0;
        let left = autoGrow ? Math.min(0, viewLeft) : 0;
        let top = autoGrow ? Math.min(0, viewTop) : 0;
        let right = autoGrow ? Math.max(columns - 1, viewLeft + viewColumns - 1) : columns - 1;
        let bottom = autoGrow ? Math.max(rows - 1, viewTop + viewRows - 1) : rows - 1;

        if (autoGrow) {
            for (let index = 0; index < outside.byteLength; index += 8) {
                const column = outside.getInt32(index, true);
                const row = outside.getInt32(index + 4, true);
                left = Math.min(left, column - 2);
                top = Math.min(top, row - 2);
                right = Math.max(right, column + 2);
                bottom = Math.max(bottom, row + 2);
            }
        }

        viewLeft = left;
        viewTop = top;
        viewColumns = right - left + 1;
        viewRows = bottom - top + 1;
        viewSizeOutput.textContent = `View: ${viewColumns} × ${viewRows}`;
        canvas.style.width = `min(100cqw, calc(100cqh * ${viewColumns} / ${viewRows}))`;
        canvas.style.aspectRatio = `${viewColumns} / ${viewRows}`;
        const scale = window.devicePixelRatio || 1;
        const width = Math.max(1, Math.round(canvas.getBoundingClientRect().width * scale));
        const height = Math.max(1, Math.round(width * viewRows / viewColumns));

        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }

        const cellSize = width / viewColumns;
        const drawGrid = showGrid && cellSize >= 3 * scale;
        const gap = drawGrid ? Math.min(scale, cellSize * 0.2) : 0;
        context.fillStyle = "#0c1625";
        context.fillRect(0, 0, width, height);
        context.fillStyle = "#66d9c9";

        for (let index = 0; index < cells.length; index++) {
            if (cells[index] === 1) {
                const column = index % columns;
                const row = Math.floor(index / columns);
                context.fillRect((column - viewLeft) * cellSize + gap / 2, (row - viewTop) * cellSize + gap / 2, cellSize - gap, cellSize - gap);
            }
        }

        if (autoGrow) {
            for (let index = 0; index < outside.byteLength; index += 8) {
                const column = outside.getInt32(index, true);
                const row = outside.getInt32(index + 4, true);
                context.fillRect((column - viewLeft) * cellSize + gap / 2, (row - viewTop) * cellSize + gap / 2, cellSize - gap, cellSize - gap);
            }
        }

        if (drawGrid) {
            context.beginPath();
            context.strokeStyle = "#1f3249";
            context.lineWidth = gap;

            for (let column = 1; column < viewColumns; column++) {
                context.moveTo(column * cellSize, 0);
                context.lineTo(column * cellSize, height);
            }

            for (let row = 1; row < viewRows; row++) {
                context.moveTo(0, row * cellSize);
                context.lineTo(width, row * cellSize);
            }

            context.stroke();
        }

    }

    function scheduleDraw() {
        if (drawFrame === 0 && columns > 0) {
            drawFrame = requestAnimationFrame(draw);
        }
    }

    function cellAt(event) {
        const bounds = canvas.getBoundingClientRect();

        return [viewLeft + Math.max(0, Math.min(viewColumns - 1, Math.floor((event.clientX - bounds.left) / bounds.width * viewColumns))), viewTop + Math.max(0, Math.min(viewRows - 1, Math.floor((event.clientY - bounds.top) / bounds.height * viewRows)))];
    }

    function isAlive(column, row) {
        if (column >= 0 && column < columns && row >= 0 && row < rows) {
            return cells[row * columns + column] === 1;
        }

        for (let index = 0; index < outside.byteLength; index += 8) {
            if (outside.getInt32(index, true) === column && outside.getInt32(index + 4, true) === row) {
                return true;
            }
        }

        return false;
    }

    function flushPaint() {
        cancelAnimationFrame(paintFrame);
        paintFrame = 0;

        if (pendingPoints.length === 0) {
            return;
        }

        const points = pendingPoints;
        const alive = drawingAlive;
        pendingPoints = [];
        pendingPaint = pendingPaint.then(() => disposed ? undefined : callbacks.invokeMethodAsync("PaintStroke", points, alive));
    }

    function queuePaint(cell) {
        pendingPoints.push(...previousCell, ...cell);
        previousCell = cell;

        if (paintFrame === 0) {
            paintFrame = requestAnimationFrame(flushPaint);
        }
    }

    canvas.addEventListener("pointerdown", event => {
        if (event.button !== 0 || columns === 0) {
            return;
        }

        event.preventDefault();
        flushPaint();
        canvas.setPointerCapture(event.pointerId);
        previousCell = cellAt(event);
        drawingAlive = !isAlive(previousCell[0], previousCell[1]);
        drawing = true;
        queuePaint(previousCell);
    }, { signal: events.signal });

    canvas.addEventListener("pointermove", event => {
        if (drawing) {
            queuePaint(cellAt(event));
        }
    }, { signal: events.signal });

    function endDrawing() {
        drawing = false;
        flushPaint();
    }

    canvas.addEventListener("pointerup", endDrawing, { signal: events.signal });
    canvas.addEventListener("pointercancel", endDrawing, { signal: events.signal });
    canvas.addEventListener("lostpointercapture", endDrawing, { signal: events.signal });
    window.addEventListener("resize", scheduleDraw, { signal: events.signal });
    const observer = new ResizeObserver(scheduleDraw);
    observer.observe(canvas);
    document.addEventListener("fullscreenchange", () => {
        const panel = canvas.closest(".simulation");
        panel.querySelector("[data-fullscreen-toggle]").textContent = document.fullscreenElement === panel ? "Exit fullscreen" : "Fullscreen";
        scheduleDraw();
    }, { signal: events.signal });

    return {
        render(nextCells, nextColumns, nextRows, nextShowGrid, nextOutside, nextAutoGrow, nextViewRevision) {
            if (columns !== nextColumns || rows !== nextRows || viewRevision !== nextViewRevision) {
                drawing = false;
                pendingPoints = [];
                viewLeft = 0;
                viewTop = 0;
                viewColumns = nextColumns;
                viewRows = nextRows;
            }

            viewRevision = nextViewRevision;
            cells = nextCells;
            columns = nextColumns;
            rows = nextRows;
            showGrid = nextShowGrid;
            outside = new DataView(nextOutside.buffer, nextOutside.byteOffset, nextOutside.byteLength);
            autoGrow = nextAutoGrow;
            scheduleDraw();
        },
        dispose() {
            disposed = true;
            events.abort();
            observer.disconnect();
            cancelAnimationFrame(drawFrame);
            cancelAnimationFrame(paintFrame);
        }
    };
}

