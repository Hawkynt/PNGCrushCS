using FileFormat.Core;

namespace FileFormat.Ktx;

/// <summary>The 64-byte KTX1 header.</summary>
[GenerateSerializer]
[HeaderFiller("Identifier", 0, 12)]
public readonly partial record struct KtxHeader(
  [property: HeaderField(12, 4)] int Endianness,
  [property: HeaderField(16, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlType,
  [property: HeaderField(20, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlTypeSize,
  [property: HeaderField(24, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlFormat,
  [property: HeaderField(28, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlInternalFormat,
  [property: HeaderField(32, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int GlBaseInternalFormat,
  [property: HeaderField(36, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int PixelWidth,
  [property: HeaderField(40, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int PixelHeight,
  [property: HeaderField(44, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int PixelDepth,
  [property: HeaderField(48, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int NumberOfArrayElements,
  [property: HeaderField(52, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int NumberOfFaces,
  [property: HeaderField(56, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int NumberOfMipmapLevels,
  [property: HeaderField(60, 4, EndianFieldName = "Endianness", EndianComputeValue = 0x01020304)] int BytesOfKeyValueData
) {

  public const int StructSize = 64;
  public const int IdentifierSize = 12;
  public const int EndiannessLE = 0x04030201;
  public const int EndiannessBE = 0x01020304;

  public static readonly byte[] Identifier = { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<KtxHeader>();
}
