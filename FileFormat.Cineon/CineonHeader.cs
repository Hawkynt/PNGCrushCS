using FileFormat.Core;

namespace FileFormat.Cineon;

/// <summary>The 1024-byte Cineon file header (big-endian).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(156, 36)]
[Filler(194, 2)]
[Filler(199, 1)]
[Filler(224, 800)]
public readonly partial record struct CineonHeader(
 int Magic,
 int ImageDataOffset,
 int GenericHeaderLength,
 int IndustryHeaderLength,
 int UserDataLength,
 int FileSize,
 string Version,
 string FileName,
 string CreateDate,
 string CreateTime,
 [property: FieldOffset(192)] byte Orientation,
 byte NumElements,
 [property: FieldOffset(196)] byte DesignatorCode1,
 byte DesignatorCode2,
 byte BitsPerSample,
 [property: FieldOffset(200)] int PixelsPerLine,
 int LinesPerElement,
 float MinData,
 float MinQuantity,
 float MaxData,
 float MaxQuantity
) {

 public const int StructSize = 1024;
 public const int MagicNumber = unchecked((int)0x802A5FD7);

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<CineonHeader>();
}
