using System;
using System.Buffers.Binary;

namespace FileFormat.JpegXr;

/// <summary>IFD (Image File Directory) entry for the JPEG XR TIFF-like container.</summary>
internal readonly record struct JpegXrIfdEntry(ushort Tag, ushort Type, uint Count, uint Value);

/// <summary>Pixel format information carried by the 16-byte WIC pixel-format GUID.</summary>
internal readonly record struct JpegXrPixelFormatInfo(
  int ComponentCount,
  int BytesPerPixel,
  bool BgrOrder,
  bool HasAlpha,
  bool PremultipliedAlpha
);

/// <summary>Handles parsing and writing IFD entries for the JPEG XR TIFF-like container.</summary>
internal static class JpegXrIfd {

  internal const ushort TAG_PIXEL_FORMAT = 0xBC01;
  internal const ushort TAG_SPATIAL_XFRM = 0xBC02;
  internal const ushort TAG_IMAGE_WIDTH = 0xBC80;
  internal const ushort TAG_IMAGE_HEIGHT = 0xBC81;
  internal const ushort TAG_IMAGE_OFFSET = 0xBCC0;
  internal const ushort TAG_IMAGE_BYTE_COUNT = 0xBCC1;
  internal const ushort TAG_ALPHA_OFFSET = 0xBCC2;
  internal const ushort TAG_ALPHA_BYTE_COUNT = 0xBCC3;

  internal const ushort TYPE_BYTE = 1;
  internal const ushort TYPE_SHORT = 3;
  internal const ushort TYPE_LONG = 4;

  private static readonly byte[] _WicPixelFormatPrefix = [
    0x24, 0xC3, 0xDD, 0x6F, 0x03, 0x4E, 0xFE, 0x4B,
    0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9
  ];

  internal const byte WIC_BLACK_WHITE = 0x05;
  internal const byte WIC_8BPP_GRAY = 0x08;
  internal const byte WIC_16BPP_BGR555 = 0x09;
  internal const byte WIC_16BPP_BGR565 = 0x0A;
  internal const byte WIC_16BPP_GRAY = 0x0B;
  internal const byte WIC_24BPP_BGR = 0x0C;
  internal const byte WIC_24BPP_RGB = 0x0D;
  internal const byte WIC_32BPP_BGR = 0x0E;
  internal const byte WIC_32BPP_BGRA = 0x0F;
  internal const byte WIC_32BPP_PBGRA = 0x10;

  internal static JpegXrIfdEntry[] ParseEntries(byte[] data, int ifdOffset) {
    if (ifdOffset < 0 || ifdOffset + 2 > data.Length)
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

  /// <summary>Reads the standard 16-byte WIC pixel-format GUID referenced by a BC01 entry.</summary>
  internal static JpegXrPixelFormatInfo ParsePixelFormat(byte[] data, JpegXrIfdEntry entry) {
    ArgumentNullException.ThrowIfNull(data);
    if (entry.Tag != TAG_PIXEL_FORMAT)
      throw new ArgumentException("The entry is not the JPEG XR pixel-format entry.", nameof(entry));
    if (entry.Type != TYPE_BYTE || entry.Count != 16)
      throw new InvalidOperationException("JPEG XR pixel format must be a 16-byte WIC GUID.");

    var offset = checked((int)entry.Value);
    if (offset < 0 || offset > data.Length - 16)
      throw new InvalidOperationException("JPEG XR pixel-format GUID points outside the file.");

    var guid = data.AsSpan(offset, 16);
    if (!guid[..15].SequenceEqual(_WicPixelFormatPrefix))
      throw new NotSupportedException($"JPEG XR uses an unrecognised WIC pixel-format GUID {new Guid(guid)}.");

    return guid[15] switch {
      WIC_BLACK_WHITE => new(1, 0, false, false, false),
      WIC_8BPP_GRAY => new(1, 1, false, false, false),
      WIC_24BPP_BGR => new(3, 3, true, false, false),
      WIC_24BPP_RGB => new(3, 3, false, false, false),
      WIC_32BPP_BGR => new(3, 4, true, false, false),
      WIC_32BPP_BGRA => new(4, 4, true, true, false),
      WIC_32BPP_PBGRA => new(4, 4, true, true, true),
      _ => throw new NotSupportedException($"JPEG XR WIC pixel format suffix 0x{guid[15]:X2} is outside the current RawImage model.")
    };
  }

  /// <summary>Creates the canonical 16-byte WIC GUID for the public JXR model.</summary>
  internal static byte[] CreatePixelFormatGuid(int componentCount) {
    var suffix = componentCount switch {
      1 => WIC_8BPP_GRAY,
      3 => WIC_24BPP_RGB,
      4 => WIC_32BPP_BGRA,
      _ => throw new NotSupportedException($"JPEG XR writer supports Gray8, RGB24, and RGBA32; got {componentCount} components.")
    };
    var result = new byte[16];
    _WicPixelFormatPrefix.CopyTo(result, 0);
    result[15] = suffix;
    return result;
  }

  internal static void WriteEntry(Span<byte> span, ref int pos, ushort tag, ushort type, uint count, uint value) {
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], tag);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 2)..], type);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], count);
    if (type == TYPE_SHORT && count == 1)
      BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 8)..], (ushort)value);
    else if (type == TYPE_BYTE && count == 1)
      span[pos + 8] = (byte)value;
    else
      BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value);
    pos += 12;
  }

  private static uint _ReadValue(byte[] data, int valueFieldOffset, ushort type, uint count) {
    if (count == 1) {
      return type switch {
        TYPE_BYTE => data[valueFieldOffset],
        TYPE_SHORT => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(valueFieldOffset)),
        TYPE_LONG => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(valueFieldOffset)),
        _ => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(valueFieldOffset))
      };
    }

    return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(valueFieldOffset));
  }

  internal static int TypeSize(ushort type) => type switch {
    TYPE_BYTE => 1,
    TYPE_SHORT => 2,
    TYPE_LONG => 4,
    _ => 4
  };
}
