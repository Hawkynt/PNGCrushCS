using System;

namespace FileFormat.SamCoupe;

/// <summary>Assembles SAM Coupe screen file bytes from a <see cref="SamCoupeFile"/>.</summary>
public static class SamCoupeWriter {

  public static byte[] ToBytes(SamCoupeFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SamCoupeReader.FileSize];

    // Interleave: even lines (0,2,4,...) to first page, odd lines (1,3,5,...) to second page
    for (var i = 0; i < SamCoupeReader.LinesPerPage; ++i) {
      var evenRow = i * 2;
      var oddRow = i * 2 + 1;

      file.PixelData.AsSpan(evenRow * SamCoupeReader.BytesPerRow, SamCoupeReader.BytesPerRow).CopyTo(result.AsSpan(i * SamCoupeReader.BytesPerRow));
      file.PixelData.AsSpan(oddRow * SamCoupeReader.BytesPerRow, SamCoupeReader.BytesPerRow).CopyTo(result.AsSpan(SamCoupeReader.PageSize + i * SamCoupeReader.BytesPerRow));
    }

    return result;
  }
}
