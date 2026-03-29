using FileFormat.Core;

namespace FileFormat.Xwd;

/// <summary>The 100-byte fixed header at the start of every XWD file (version 7).</summary>
[GenerateSerializer]
public readonly partial record struct XwdHeader(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] uint HeaderSize,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] uint FileVersion,
  [property: HeaderField(8, 4, Endianness = Endianness.Big)] uint PixmapFormat,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] uint PixmapDepth,
  [property: HeaderField(16, 4, Endianness = Endianness.Big)] uint PixmapWidth,
  [property: HeaderField(20, 4, Endianness = Endianness.Big)] uint PixmapHeight,
  [property: HeaderField(24, 4, Endianness = Endianness.Big)] uint XOffset,
  [property: HeaderField(28, 4, Endianness = Endianness.Big)] uint ByteOrder,
  [property: HeaderField(32, 4, Endianness = Endianness.Big)] uint BitmapUnit,
  [property: HeaderField(36, 4, Endianness = Endianness.Big)] uint BitmapBitOrder,
  [property: HeaderField(40, 4, Endianness = Endianness.Big)] uint BitmapPad,
  [property: HeaderField(44, 4, Endianness = Endianness.Big)] uint BitsPerPixel,
  [property: HeaderField(48, 4, Endianness = Endianness.Big)] uint BytesPerLine,
  [property: HeaderField(52, 4, Endianness = Endianness.Big)] uint VisualClass,
  [property: HeaderField(56, 4, Endianness = Endianness.Big)] uint RedMask,
  [property: HeaderField(60, 4, Endianness = Endianness.Big)] uint GreenMask,
  [property: HeaderField(64, 4, Endianness = Endianness.Big)] uint BlueMask,
  [property: HeaderField(68, 4, Endianness = Endianness.Big)] uint BitsPerRgb,
  [property: HeaderField(72, 4, Endianness = Endianness.Big)] uint ColormapEntries,
  [property: HeaderField(76, 4, Endianness = Endianness.Big)] uint NumColors,
  [property: HeaderField(80, 4, Endianness = Endianness.Big)] uint WindowWidth,
  [property: HeaderField(84, 4, Endianness = Endianness.Big)] uint WindowHeight,
  [property: HeaderField(88, 4, Endianness = Endianness.Big)] int WindowX,
  [property: HeaderField(92, 4, Endianness = Endianness.Big)] int WindowY,
  [property: HeaderField(96, 4, Endianness = Endianness.Big)] uint WindowBorderWidth
) {

  public const int StructSize = 100;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<XwdHeader>();
}
