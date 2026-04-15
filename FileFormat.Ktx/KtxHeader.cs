using FileFormat.Core;

namespace FileFormat.Ktx;

/// <summary>The 64-byte KTX1 header.</summary>
[GenerateSerializer]
[Filler(0, 12)]
public readonly partial record struct KtxHeader(
  [property: Field(12, 4)] int Endianness,
  [property: Field(16, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlType,
  [property: Field(20, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlTypeSize,
  [property: Field(24, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlFormat,
  [property: Field(28, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlInternalFormat,
  [property: Field(32, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlBaseInternalFormat,
  [property: Field(36, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int PixelWidth,
  [property: Field(40, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int PixelHeight,
  [property: Field(44, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int PixelDepth,
  [property: Field(48, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int NumberOfArrayElements,
  [property: Field(52, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int NumberOfFaces,
  [property: Field(56, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int NumberOfMipmapLevels,
  [property: Field(60, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int BytesOfKeyValueData
) {

  public const int StructSize = 64;
  public const int IdentifierSize = 12;
  public const int EndiannessLE = 0x04030201;
  public const int EndiannessBE = 0x01020304;

  public static readonly byte[] Identifier = { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<KtxHeader>();
}
