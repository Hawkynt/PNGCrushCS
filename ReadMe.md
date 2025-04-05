# PngCrushCS - A C# PNG Optimizer

## Description

PngCrushCS is a C# console application inspired by the original `pngcrush`. Its primary goal is to reduce the size of PNG image files by trying different compression strategies and selecting the one that yields the smallest result.

This implementation focuses on the core optimization technique:
1.  Reading an existing PNG file.
2.  Decompressing the image data (IDAT chunks).
3.  Re-filtering the raw pixel data using different PNG filter types.
4.  Re-compressing the filtered data using different ZLib compression levels.
5.  Rebuilding the PNG file structure with the optimized image data, preserving other metadata chunks.
6.  Saving the version of the file that results in the smallest size.

## Features

*   Reads standard PNG files.
*   Parses PNG chunks (IHDR, IDAT, IEND, and others).
*   Preserves ancillary chunks (metadata, palettes, etc.).
*   Decompresses ZLib image data.
*   Applies PNG filter types 0 (None), 1 (Sub), 2 (Up), 3 (Average), and 4 (Paeth).
*   Re-compresses using `ZLibStream` with `CompressionLevel.Optimal` and `CompressionLevel.SmallestSize`.
*   Compares results and saves the smallest valid PNG found.

## Requirements

*   **.NET SDK**: You **must** have the .NET SDK installed to build and run this application. You can download it from the official [.NET website](https://dotnet.microsoft.com/download/dotnet).

## Building

```bash
dotnet build -c Release
```

**Locate Executable:** The compiled application will be in the `bin/Release/` subfolder.

*   Windows: `PngCrushCS.exe`
*   Linux/macOS: `PngCrushCS`

## Usage

Run the application from the command line, providing the input PNG file path and the desired output PNG file path.

**Syntax:**

```bash
# On Windows (from the output directory)
.\PngCrushCS <input.png> <output.png>

# On Linux/macOS (from the output directory)
./PngCrushCS <input.png> <output.png>
```

The application will print progress information and the final size comparison to the console. If optimization results in a smaller file, output.png will contain the optimized version. If no improvement is found, output.png will be a copy of the original input.png.

## Limitations & Caveats

* Not Feature-Complete: Does not implement all features of the original pngcrush (e.g., chunk removal, color type reduction, brute-force methods).
* Limited Strategies: Only tries filter types 0-4 and two ZLib compression levels (Optimal, SmallestSize). A full pngcrush tests many more combinations.
* Interlaced PNG Output Not Supported: The application will not produce interlaced (Adam7) PNGs.
* Basic Validation: Performs basic checks on IHDR and chunk integrity but may not handle heavily corrupted PNGs gracefully.
* Performance: Compression/decompression using C#'s ZLibStream might be slower than the highly optimized C zlib library used by the original pngcrush.

## License

* [LGPL-3.0](https://en.wikipedia.org/wiki/GNU_Lesser_General_Public_License)
