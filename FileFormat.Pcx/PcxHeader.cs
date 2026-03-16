using FileFormat.Core;

namespace FileFormat.Pcx;

/// <summary>The 128-byte header at the start of every PCX file.</summary>
[GenerateSerializer]
internal readonly partial record struct PcxHeader(
  [property: HeaderField(0, 1)] byte Manufacturer,
  [property: HeaderField(1, 1)] byte Version,
  [property: HeaderField(2, 1)] byte Encoding,
  [property: HeaderField(3, 1)] byte BitsPerPixel,
  [property: HeaderField(4, 2)] short XMin,
  [property: HeaderField(6, 2)] short YMin,
  [property: HeaderField(8, 2)] short XMax,
  [property: HeaderField(10, 2)] short YMax,
  [property: HeaderField(12, 2)] short HDpi,
  [property: HeaderField(14, 2)] short VDpi,
  [property: HeaderField(16, 48)] byte[] EgaPalette,
  [property: HeaderField(64, 1)] byte Reserved,
  [property: HeaderField(65, 1)] byte NumPlanes,
  [property: HeaderField(66, 2)] short BytesPerLine,
  [property: HeaderField(68, 2)] short PaletteInfo,
  [property: HeaderField(70, 2)] short HScreenSize,
  [property: HeaderField(72, 2)] short VScreenSize,
  [property: HeaderField(74, 54)] byte[] Padding
) {

  public const int StructSize = 128;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<PcxHeader>();
}
