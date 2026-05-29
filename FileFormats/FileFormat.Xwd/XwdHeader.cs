using FileFormat.Core;

namespace FileFormat.Xwd;

/// <summary>The 100-byte fixed header at the start of every XWD file (version 7).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct XwdHeader(
  uint HeaderSize,
  uint FileVersion,
  uint PixmapFormat,
  uint PixmapDepth,
  uint PixmapWidth,
  uint PixmapHeight,
  uint XOffset,
  uint ByteOrder,
  uint BitmapUnit,
  uint BitmapBitOrder,
  uint BitmapPad,
  uint BitsPerPixel,
  uint BytesPerLine,
  uint VisualClass,
  uint RedMask,
  uint GreenMask,
  uint BlueMask,
  uint BitsPerRgb,
  uint ColormapEntries,
  uint NumColors,
  uint WindowWidth,
  uint WindowHeight,
  int WindowX,
  int WindowY,
  uint WindowBorderWidth
) {

 public const int StructSize = 100;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<XwdHeader>();
}
