using System;

namespace FileFormat.ZxSpectrum;

/// <summary>Assembles ZX Spectrum screen file bytes from a <see cref="ZxSpectrumFile"/>.</summary>
public static class ZxSpectrumWriter {

  public static byte[] ToBytes(ZxSpectrumFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxSpectrumReader.FileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxSpectrumReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxSpectrumReader.BytesPerRow;
      var srcOffset = y * ZxSpectrumReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxSpectrumReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Copy attribute data directly after bitmap
    file.AttributeData.AsSpan(0, ZxSpectrumReader.AttributeSize).CopyTo(result.AsSpan(ZxSpectrumReader.BitmapSize));

    return result;
  }
}
