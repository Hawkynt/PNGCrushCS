using System;
using System.Buffers.Binary;

namespace FileFormat.JpegXr;

/// <summary>IFD (Image File Directory) entry for the JPEG XR TIFF-like container.</summary>
internal readonly record struct JpegXrIfdEntry(ushort Tag, ushort Type, uint Count, uint Value);

/// <summary>Handles parsing and writing IFD entries for the JPEG XR TIFF-like container.</summary>
internal static class JpegXrIfd {

  // JPEG XR IFD tag IDs
  internal const ushort TAG_PIXEL_FORMAT = 0xBC01;
  internal const ushort TAG_SPATIAL_XFRM = 0xBC02;
  internal const ushort TAG_IMAGE_WIDTH = 0xBC80;
  internal const ushort TAG_IMAGE_HEIGHT = 0xBC81;
  internal const ushort TAG_IMAGE_OFFSET = 0xBCE0;
  internal const ushort TAG_IMAGE_BYTE_COUNT = 0xBCE1;
  internal const ushort TAG_ALPHA_OFFSET = 0xBCE2;
  internal const ushort TAG_ALPHA_BYTE_COUNT = 0xBCE3;

  // IFD field types
  internal const ushort TYPE_BYTE = 1;
  internal const ushort TYPE_SHORT = 3;
  internal const ushort TYPE_LONG = 4;

  // Pixel format identifier bytes (first 2 bytes of the GUID encode bpp)
  internal const byte PIXEL_FORMAT_8BPP_GRAY = 0x08;
  internal const byte PIXEL_FORMAT_24BPP_RGB = 0x0C;

  /// <summary>Parses all IFD entries from the given data starting at <paramref name="ifdOffset"/>.</summary>
  internal static JpegXrIfdEntry[] ParseEntries(byte[] data, int ifdOffset) {
    if (ifdOffset + 2 > data.Length)
      throw new InvalidOperationException("IFD offset extends beyond data.");

    var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ifdOffset));
    var entries = new JpegXrIfdEntry[entryCount];
    var pos = ifdOffset + 2;

    for (var i = 0; i < entryCount; ++i) {
      if (pos + 12 > data.Length)
        throw new InvalidOperationException("IFD entry extends beyond data.");

      var tag = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2));
      var count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
      var value = _ReadValue(data, pos + 8, type, count);
      entries[i] = new(tag, type, count, value);
      pos += 12;
    }

    return entries;
  }

  /// <summary>Writes a single IFD entry at the current position and advances <paramref name="pos"/>.</summary>
  internal static void WriteEntry(Span<byte> span, ref int pos, ushort tag, ushort type, uint count, uint value) {
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], tag);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 2)..], type);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], count);
    if (type == TYPE_SHORT && count == 1)
      BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 8)..], (ushort)value);
    else
      BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value);
    pos += 12;
  }

  /// <summary>Reads a tag value from the 4-byte value/offset field.</summary>
  private static uint _ReadValue(byte[] data, int valueFieldOffset, ushort type, uint count) {
    if (count == 1) {
      return type switch {
        TYPE_BYTE => data[valueFieldOffset],
        TYPE_SHORT => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(valueFieldOffset)),
        TYPE_LONG => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(valueFieldOffset)),
        _ => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(valueFieldOffset))
      };
    }

    // Multi-value: the 4 bytes are an offset to the actual data
    return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(valueFieldOffset));
  }

  /// <summary>Returns the element size in bytes for the given IFD field type.</summary>
  internal static int TypeSize(ushort type) => type switch {
    TYPE_BYTE => 1,
    TYPE_SHORT => 2,
    TYPE_LONG => 4,
    _ => 4
  };
}
