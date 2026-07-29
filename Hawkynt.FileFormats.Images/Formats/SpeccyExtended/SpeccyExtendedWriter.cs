using System;

namespace FileFormat.SpeccyExtended;

/// <summary>Assembles Speccy eXtended Graphics (SXG) file bytes from a <see cref="SpeccyExtendedFile"/>.</summary>
public static class SpeccyExtendedWriter {

  public static byte[] ToBytes(SpeccyExtendedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SpeccyExtendedReader.FileSize];

    // Write header: "SXG" + version
    result[0] = SpeccyExtendedReader.Magic[0];
    result[1] = SpeccyExtendedReader.Magic[1];
    result[2] = SpeccyExtendedReader.Magic[2];
    result[3] = file.Version;

    var bitmapOffset = SpeccyExtendedReader.HeaderSize;

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < SpeccyExtendedReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = bitmapOffset + third * 2048 + pixelLine * 256 + characterRow * SpeccyExtendedReader.BytesPerRow;
      var srcOffset = y * SpeccyExtendedReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, SpeccyExtendedReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Copy standard attribute data
    var stdAttrOffset = bitmapOffset + SpeccyExtendedReader.BitmapSize;
    file.AttributeData.AsSpan(0, SpeccyExtendedReader.AttributeSize).CopyTo(result.AsSpan(stdAttrOffset));

    // Copy extended attribute data
    var extAttrOffset = stdAttrOffset + SpeccyExtendedReader.AttributeSize;
    file.ExtendedAttributeData.AsSpan(0, SpeccyExtendedReader.AttributeSize).CopyTo(result.AsSpan(extAttrOffset));

    return result;
  }
}
