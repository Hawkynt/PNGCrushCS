using FileFormat.Core;

namespace FileFormat.WebP;

/// <summary>The 10-byte VP8X extended header: flags, 3 reserved bytes, 3-byte canvas width, 3-byte canvas height.</summary>
[GenerateSerializer]
internal readonly partial record struct Vp8XHeader(
  [property: HeaderField(0, 1)] byte Flags,
  [property: HeaderField(1, 1)] byte Reserved1,
  [property: HeaderField(2, 1)] byte Reserved2,
  [property: HeaderField(3, 1)] byte Reserved3,
  [property: HeaderField(4, 1)] byte CanvasWidthByte0,
  [property: HeaderField(5, 1)] byte CanvasWidthByte1,
  [property: HeaderField(6, 1)] byte CanvasWidthByte2,
  [property: HeaderField(7, 1)] byte CanvasHeightByte0,
  [property: HeaderField(8, 1)] byte CanvasHeightByte1,
  [property: HeaderField(9, 1)] byte CanvasHeightByte2
) {

  public const int StructSize = 10;

  public int CanvasWidth => (this.CanvasWidthByte0 | (this.CanvasWidthByte1 << 8) | (this.CanvasWidthByte2 << 16)) + 1;
  public int CanvasHeight => (this.CanvasHeightByte0 | (this.CanvasHeightByte1 << 8) | (this.CanvasHeightByte2 << 16)) + 1;
  public bool HasAlpha => (this.Flags & 0x10) != 0;
  public bool IsAnimated => (this.Flags & 0x02) != 0;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<Vp8XHeader>();
}
