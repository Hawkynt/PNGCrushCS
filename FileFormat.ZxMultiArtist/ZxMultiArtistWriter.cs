using System;

namespace FileFormat.ZxMultiArtist;

/// <summary>Assembles ZX Spectrum MultiArtist file bytes from a <see cref="ZxMultiArtistFile"/>.</summary>
public static class ZxMultiArtistWriter {

  public static byte[] ToBytes(ZxMultiArtistFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var fileSize = ZxMultiArtistFile.GetFileSize(file.Mode);
    var attributeSize = ZxMultiArtistFile.GetAttributeSize(file.Mode);
    var result = new byte[fileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxMultiArtistReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxMultiArtistReader.BytesPerRow;
      var srcOffset = y * ZxMultiArtistReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxMultiArtistReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Copy attribute data after bitmap
    file.AttributeData.AsSpan(0, attributeSize).CopyTo(result.AsSpan(ZxMultiArtistReader.BitmapSize));

    return result;
  }
}
