using FileFormat.Core;

namespace FileFormat.Pcx;

/// <summary>The 128-byte header at the start of every PCX file.</summary>
[GenerateSerializer]
internal readonly partial record struct PcxHeader(
  byte Manufacturer,
  byte Version,
  byte Encoding,
  byte BitsPerPixel,
  short XMin,
  short YMin,
  short XMax,
  short YMax,
  short HDpi,
  short VDpi,
  byte[] EgaPalette,
  byte Reserved,
  byte NumPlanes,
  short BytesPerLine,
  short PaletteInfo,
  short HScreenSize,
  short VScreenSize,
  byte[] Padding
) {

 public const int StructSize = 128;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PcxHeader>();
}
