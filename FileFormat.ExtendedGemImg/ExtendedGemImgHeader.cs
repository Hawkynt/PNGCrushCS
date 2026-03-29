using FileFormat.Core;

namespace FileFormat.ExtendedGemImg;

/// <summary>The 16-byte standard GEM IMG header (big-endian), shared with the XIMG extension.</summary>
[GenerateSerializer]
internal readonly partial record struct ExtendedGemImgHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] short Version,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] short HeaderLength,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] short NumPlanes,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] short PatternLength,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] short PixelWidth,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] short PixelHeight,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] short ScanWidth,
  [property: HeaderField(14, 2, Endianness = Endianness.Big)] short ScanLines
) {

  public const int StructSize = 16;

  /// <summary>The XIMG marker word "XIMG" = 0x58494D47, stored as two big-endian shorts.</summary>
  public const short XimgMarker1 = 0x5849; // "XI"
  public const short XimgMarker2 = 0x4D47; // "MG"

  /// <summary>Size of the XIMG extension: 4 bytes marker + 2 bytes color model.</summary>
  public const int XimgExtensionFixedSize = 6;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ExtendedGemImgHeader>();
}
