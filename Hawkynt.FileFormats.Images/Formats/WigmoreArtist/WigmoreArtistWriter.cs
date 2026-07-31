using System;

namespace FileFormat.WigmoreArtist;

/// <summary>Assembles Wigmore Artist picture bytes from a <see cref="WigmoreArtistFile"/>.</summary>
public static class WigmoreArtistWriter {

  public static byte[] ToBytes(WigmoreArtistFile file) {
    var result = new byte[WigmoreArtistFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    var bitmap = file.BitmapData ?? [];
    var matrix = file.VideoMatrix ?? [];
    var colors = file.ColorRam ?? [];

    bitmap.AsSpan(0, Math.Min(bitmap.Length, WigmoreArtistFile.BitmapDataSize))
      .CopyTo(result.AsSpan(WigmoreArtistFile.BitmapOffset));
    matrix.AsSpan(0, Math.Min(matrix.Length, WigmoreArtistFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(WigmoreArtistFile.VideoMatrixOffset));
    colors.AsSpan(0, Math.Min(colors.Length, WigmoreArtistFile.ColorRamSize))
      .CopyTo(result.AsSpan(WigmoreArtistFile.ColorRamOffset));

    if (WigmoreArtistFile.BackgroundOffset >= 0)
      result[WigmoreArtistFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
