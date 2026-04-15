using FileFormat.Core;

namespace FileFormat.ExtendedGemImg;

/// <summary>The 6-byte fixed portion of the XIMG extension: "XIMG" marker (2 big-endian shorts) + color model (big-endian short).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct ExtendedGemImgExtensionHeader(
  short Marker1,
  short Marker2,
  short ColorModel
) {

 public const int StructSize = 6;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<ExtendedGemImgExtensionHeader>();
}
