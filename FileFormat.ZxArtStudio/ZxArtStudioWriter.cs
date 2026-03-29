using System;

namespace FileFormat.ZxArtStudio;

/// <summary>Assembles ZX Spectrum Art Studio (.zas) file bytes from a <see cref="ZxArtStudioFile"/>.</summary>
public static class ZxArtStudioWriter {

  public static byte[] ToBytes(ZxArtStudioFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxArtStudioReader.FileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxArtStudioReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxArtStudioReader.BytesPerRow;
      var srcOffset = y * ZxArtStudioReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxArtStudioReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    file.AttributeData.AsSpan(0, ZxArtStudioReader.AttributeSize).CopyTo(result.AsSpan(ZxArtStudioReader.BitmapSize));

    return result;
  }
}
