using System;

namespace FileFormat.MovieMakerBackground;

/// <summary>Assembles Movie Maker background (.bkg) file bytes.</summary>
public static class MovieMakerBackgroundWriter {

  public static byte[] ToBytes(MovieMakerBackgroundFile file) {
    var result = new byte[MovieMakerBackgroundFile.FileSize];

    var bitmap = file.BitmapData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, MovieMakerBackgroundFile.BitmapDataSize)).CopyTo(result);

    var colors = file.Colors ?? [];
    colors.AsSpan(0, Math.Min(colors.Length, MovieMakerBackgroundFile.ColorCount))
      .CopyTo(result.AsSpan(MovieMakerBackgroundFile.ColorOffset));

    return result;
  }
}
