using FileFormat.Core;

namespace FileFormat.Dpx;

/// <summary>The 32-byte DPX file information header core fields.</summary>
[GenerateSerializer]
public readonly partial record struct DpxHeader(
  [property: Field(0, 4, Endianness = Endianness.Big)] int Magic,
  [property: Field(4, 4, EndianFieldName = "Magic", EndianComputeValue = 0x53445058)] int ImageDataOffset,
  [property: Field(8, 8)] string Version,
  [property: Field(16, 4, EndianFieldName = "Magic", EndianComputeValue = 0x53445058)] int FileSize,
  [property: Field(20, 4, EndianFieldName = "Magic", EndianComputeValue = 0x53445058)] int DittoKey,
  [property: Field(24, 4, EndianFieldName = "Magic", EndianComputeValue = 0x53445058)] int GenericHeaderSize,
  [property: Field(28, 4, EndianFieldName = "Magic", EndianComputeValue = 0x53445058)] int IndustryHeaderSize
) {

  public const int StructSize = 32;
  public const int MagicBigEndian = 0x53445058; // "SDPX"
  public const int MagicLittleEndian = 0x58504453; // "XPDS"

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<DpxHeader>();
}
