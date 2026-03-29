using FileFormat.Core;

namespace FileFormat.Cineon;

/// <summary>The 1024-byte Cineon file header (big-endian).</summary>
[GenerateSerializer]
[HeaderFiller("Reserved1", 156, 36)]
[HeaderFiller("Unused1", 194, 2)]
[HeaderFiller("Unused2", 199, 1)]
[HeaderFiller("Padding", 224, 800)]
public readonly partial record struct CineonHeader(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] int Magic,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int ImageDataOffset,
  [property: HeaderField(8, 4, Endianness = Endianness.Big)] int GenericHeaderLength,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] int IndustryHeaderLength,
  [property: HeaderField(16, 4, Endianness = Endianness.Big)] int UserDataLength,
  [property: HeaderField(20, 4, Endianness = Endianness.Big)] int FileSize,
  [property: HeaderField(24, 8)] string Version,
  [property: HeaderField(32, 100)] string FileName,
  [property: HeaderField(132, 12)] string CreateDate,
  [property: HeaderField(144, 12)] string CreateTime,
  [property: HeaderField(192, 1)] byte Orientation,
  [property: HeaderField(193, 1)] byte NumElements,
  [property: HeaderField(196, 1)] byte DesignatorCode1,
  [property: HeaderField(197, 1)] byte DesignatorCode2,
  [property: HeaderField(198, 1)] byte BitsPerSample,
  [property: HeaderField(200, 4, Endianness = Endianness.Big)] int PixelsPerLine,
  [property: HeaderField(204, 4, Endianness = Endianness.Big)] int LinesPerElement,
  [property: HeaderField(208, 4, Endianness = Endianness.Big)] float MinData,
  [property: HeaderField(212, 4, Endianness = Endianness.Big)] float MinQuantity,
  [property: HeaderField(216, 4, Endianness = Endianness.Big)] float MaxData,
  [property: HeaderField(220, 4, Endianness = Endianness.Big)] float MaxQuantity
) {

  public const int StructSize = 1024;
  public const int MagicNumber = unchecked((int)0x802A5FD7);

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<CineonHeader>();
}
