using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.CompuServeRle;

/// <summary>Writes standard CompuServe RLE terminal graphics.</summary>
public static class CompuServeRleWriter {

  private const byte _Escape = 0x1B;
  private const int _MaximumRun = 94;

  public static byte[] ToBytes(CompuServeRleFile file) {
    CompuServeRleFile.Validate(file, nameof(file));

    var mode = (file.Width, file.Height) switch {
      (CompuServeRleFile.MediumWidth, CompuServeRleFile.MediumHeight) => (byte)'M',
      (CompuServeRleFile.HighWidth, CompuServeRleFile.HighHeight) => (byte)'H',
      _ => throw new ArgumentOutOfRangeException(nameof(file)),
    };

    var result = new List<byte>(file.RasterData.Length / 2) {
      _Escape, (byte)'G', mode,
    };

    var totalPixels = checked(file.Width * file.Height);
    var expectedWhite = false;
    var pixel = 0;

    while (pixel < totalPixels) {
      var white = _IsWhite(file.RasterData, file.Width, pixel);
      if (white != expectedWhite)
        _EmitRun(result, 0, ref expectedWhite);

      var runStart = pixel;
      do
        ++pixel;
      while (pixel < totalPixels && _IsWhite(file.RasterData, file.Width, pixel) == white);

      var runLength = pixel - runStart;
      while (runLength > _MaximumRun) {
        _EmitRun(result, _MaximumRun, ref expectedWhite);
        _EmitRun(result, 0, ref expectedWhite);
        runLength -= _MaximumRun;
      }

      _EmitRun(result, runLength, ref expectedWhite);
    }

    // The documented data grammar is a sequence of background/foreground pairs. If the raster ends
    // in a background run, complete its final pair with a zero-length foreground run.
    if (expectedWhite)
      _EmitRun(result, 0, ref expectedWhite);

    result.Add(_Escape);
    result.Add((byte)'G');
    result.Add((byte)'N');
    return [.. result];
  }

  public static void ToStream(CompuServeRleFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  public static void ToFile(CompuServeRleFile file, FileInfo destination) {
    ArgumentNullException.ThrowIfNull(destination);
    File.WriteAllBytes(destination.FullName, ToBytes(file));
  }

  private static void _EmitRun(List<byte> output, int count, ref bool expectedWhite) {
    if ((uint)count > _MaximumRun)
      throw new ArgumentOutOfRangeException(nameof(count));

    output.Add((byte)(0x20 + count));
    expectedWhite = !expectedWhite;
  }

  private static bool _IsWhite(ReadOnlySpan<byte> raster, int width, int pixel) {
    var stride = CompuServeRleFile.GetRowStride(width);
    var y = pixel / width;
    var x = pixel - y * width;
    return (raster[y * stride + (x >> 3)] & (0x80 >> (x & 7))) != 0;
  }
}
