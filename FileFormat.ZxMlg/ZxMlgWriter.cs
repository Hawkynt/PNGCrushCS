using System;

namespace FileFormat.ZxMlg;

/// <summary>Assembles ZX Spectrum MLG editor (.mlg) file bytes from a <see cref="ZxMlgFile"/>.</summary>
public static class ZxMlgWriter {

  public static byte[] ToBytes(ZxMlgFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxMlgReader.FileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxMlgReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxMlgReader.BytesPerRow;
      var srcOffset = y * ZxMlgReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxMlgReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    file.AttributeData.AsSpan(0, ZxMlgReader.AttributeSize).CopyTo(result.AsSpan(ZxMlgReader.BitmapSize));

    return result;
  }
}
