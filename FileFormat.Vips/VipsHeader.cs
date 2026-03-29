using FileFormat.Core;

namespace FileFormat.Vips;

/// <summary>The 64-byte VIPS native image file header.</summary>
[GenerateSerializer]
public readonly partial record struct VipsHeader(
  [property: HeaderField(0, 4)] int Magic,
  [property: HeaderField(4, 4)] int Width,
  [property: HeaderField(8, 4)] int Height,
  [property: HeaderField(12, 4)] int Bands,
  [property: HeaderField(16, 4)] int Unused1,
  [property: HeaderField(20, 4)] int BandFormat,
  [property: HeaderField(24, 4)] int Coding,
  [property: HeaderField(28, 4)] int Type,
  [property: HeaderField(32, 4)] float XRes,
  [property: HeaderField(36, 4)] float YRes,
  [property: HeaderField(40, 4)] int XOffset,
  [property: HeaderField(44, 4)] int YOffset,
  [property: HeaderField(48, 4)] int Length,
  [property: HeaderField(52, 2)] short Compression,
  [property: HeaderField(54, 2)] short Level,
  [property: HeaderField(56, 4)] int BBits,
  [property: HeaderField(60, 4)] int Unused2
) {

  /// <summary>Expected magic value for VIPS native images: 0x08F2A6B6.</summary>
  public const int MagicValue = unchecked((int)0x08F2A6B6);

  public const int StructSize = 64;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<VipsHeader>();
}
