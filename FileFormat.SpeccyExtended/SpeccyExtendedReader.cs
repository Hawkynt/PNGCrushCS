using System;
using System.IO;

namespace FileFormat.SpeccyExtended;

/// <summary>Reads Speccy eXtended Graphics (SXG) files from bytes, streams, or file paths.</summary>
public static class SpeccyExtendedReader {

  /// <summary>Magic bytes: "SXG".</summary>
  internal static readonly byte[] Magic = [0x53, 0x58, 0x47];

  /// <summary>Header size: 3 bytes magic + 1 byte version.</summary>
  internal const int HeaderSize = 4;

  /// <summary>Bitmap data size in bytes.</summary>
  internal const int BitmapSize = 6144;

  /// <summary>Attribute data size in bytes.</summary>
  internal const int AttributeSize = 768;

  /// <summary>Total file size: header + bitmap + standard attributes + extended attributes.</summary>
  internal const int FileSize = HeaderSize + BitmapSize + AttributeSize + AttributeSize;

  /// <summary>Bytes per pixel row (256 pixels / 8 bits per pixel).</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  public static SpeccyExtendedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SXG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SpeccyExtendedFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static SpeccyExtendedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FileSize)
      throw new InvalidDataException($"SXG file must be at least {FileSize} bytes, got {data.Length}.");

    // Validate magic bytes
    if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2])
      throw new InvalidDataException($"Invalid SXG magic: expected 'SXG', got '{(char)data[0]}{(char)data[1]}{(char)data[2]}'.");

    var version = data[3];

    var bitmapOffset = HeaderSize;
    var linearBitmap = new byte[BitmapSize];

    // Deinterleave from ZX Spectrum memory layout to linear row order
    for (var y = 0; y < RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var srcOffset = bitmapOffset + third * 2048 + pixelLine * 256 + characterRow * BytesPerRow;
      var dstOffset = y * BytesPerRow;
      data.AsSpan(srcOffset, BytesPerRow).CopyTo(linearBitmap.AsSpan(dstOffset));
    }

    var stdAttrOffset = bitmapOffset + BitmapSize;
    var attributes = new byte[AttributeSize];
    data.AsSpan(stdAttrOffset, AttributeSize).CopyTo(attributes);

    var extAttrOffset = stdAttrOffset + AttributeSize;
    var extAttributes = new byte[AttributeSize];
    data.AsSpan(extAttrOffset, AttributeSize).CopyTo(extAttributes);

    return new SpeccyExtendedFile {
      Version = version,
      BitmapData = linearBitmap,
      AttributeData = attributes,
      ExtendedAttributeData = extAttributes,
    };
  }
}
