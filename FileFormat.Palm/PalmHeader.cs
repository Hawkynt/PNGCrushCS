using FileFormat.Core;

namespace FileFormat.Palm;

/// <summary>The 16-byte header at the start of every Palm OS Bitmap. All multi-byte fields are big-endian.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct PalmHeader(
  ushort Width,
  ushort Height,
  ushort BytesPerRow,
  ushort Flags,
  byte BitsPerPixel,
  byte Version,
  ushort NextDepthOffset,
  byte TransparentIndex,
  byte CompressionType,
  ushort Reserved
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
