using FileFormat.Core;

namespace FileFormat.ExtendedGemImg;

/// <summary>The 16-byte standard GEM IMG header (big-endian), shared with the XIMG extension.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct ExtendedGemImgHeader(
  short Version,
  short HeaderLength,
  short NumPlanes,
  short PatternLength,
  short PixelWidth,
  short PixelHeight,
  short ScanWidth,
  short ScanLines
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
