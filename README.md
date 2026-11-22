# Game of Life

A Conway's Game of Life simulator built with Blazor WebAssembly, C# and a JavaScript canvas.

## Run locally

Install the .NET 11 SDK, then run:

```sh
dotnet run --launch-profile http
```

Open <http://localhost:5130>.

## Features

- Draw and erase cells with a mouse or touch, including while the simulation runs.
- Start, pause, step, and reset to the starting pattern at generation zero.
- Adjust speed and grid size, toggle grid lines, and use fullscreen.
- Simulate an unbounded world or enable wrapping edges.
- Enable Auto grow to expand the view without shrinking it during a run.
- Load built-in patterns or import and export RLE files.
- See both the total live population and the number of visible cells.

## Build

```sh
dotnet build
```

## Security and dependencies

Gitleaks scans Git history on pushes, pull requests and a weekly schedule. Dependabot checks NuGet packages and GitHub Actions weekly. The workflow files must be pushed to GitHub before these checks can run.

Keep credentials out of this repository. This application runs in the browser, so anything in client code or `wwwroot` is public to its users. Local environment files, private keys, certificates and `secrets.json` are excluded by `.gitignore`.

To scan local files with Gitleaks:

```sh
gitleaks dir . --redact --no-banner
```

## License and pattern sources

Application code is licensed under the [MIT License](LICENSE). Bundled Life patterns retain their original attribution; see [pattern sources](wwwroot/patterns/SOURCES.txt) and the comments in the RLE files.
