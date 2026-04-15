using FileFormat.Core;

namespace FileFormat.WebP;

/// <summary>The 10-byte VP8X extended header: flags, 3 reserved bytes, 3-byte canvas width, 3-byte canvas height.</summary>
[GenerateSerializer]
internal readonly partial record struct Vp8XHeader(
  byte Flags,
  byte Reserved1,
  byte Reserved2,
  byte Reserved3,
  byte CanvasWidthByte0,
  byte CanvasWidthByte1,
  byte CanvasWidthByte2,
  byte CanvasHeightByte0,
  byte CanvasHeightByte1,
  byte CanvasHeightByte2
) {

 public const int StructSize = 10;

 public int CanvasWidth => (this.CanvasWidthByte0 | (this.CanvasWidthByte1 << 8) | (this.CanvasWidthByte2 << 16)) + 1;
 public int CanvasHeight => (this.CanvasHeightByte0 | (this.CanvasHeightByte1 << 8) | (this.CanvasHeightByte2 << 16)) + 1;
 public bool HasAlpha => (this.Flags & 0x10) != 0;
 public bool IsAnimated => (this.Flags & 0x02) != 0;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Vp8XHeader>();
}
