using FileFormat.Core;

namespace FileFormat.Palm;

/// <summary>The 16-byte header at the start of every Palm OS Bitmap. All multi-byte fields are big-endian.</summary>
[GenerateSerializer]
public readonly partial record struct PalmHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] ushort Width,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] ushort Height,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] ushort BytesPerRow,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] ushort Flags,
  [property: HeaderField(8, 1)] byte BitsPerPixel,
  [property: HeaderField(9, 1)] byte Version,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] ushort NextDepthOffset,
  [property: HeaderField(12, 1)] byte TransparentIndex,
  [property: HeaderField(13, 1)] byte CompressionType,
  [property: HeaderField(14, 2, Endianness = Endianness.Big)] ushort Reserved
) {

  public const int StructSize = 16;

  /// <summary>Bit 13 of Flags: compressed.</summary>
  public const ushort FlagCompressed = 1 << 13;

  /// <summary>Bit 14 of Flags: has color table.</summary>
  public const ushort FlagHasColorTable = 1 << 14;

  /// <summary>Bit 15 of Flags: has transparency.</summary>
  public const ushort FlagHasTransparency = 1 << 15;

  public bool IsCompressed => (this.Flags & FlagCompressed) != 0;
  public bool HasColorTable => (this.Flags & FlagHasColorTable) != 0;
  public bool HasTransparency => (this.Flags & FlagHasTransparency) != 0;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<PalmHeader>();
}
