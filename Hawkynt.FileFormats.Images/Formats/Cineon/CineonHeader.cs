using FileFormat.Core;

namespace FileFormat.Cineon;

/// <summary>The Cineon file header (big-endian): a 1024-byte generic part and a 1024-byte image part.</summary>
/// <remarks>
/// Each colour is its own "element", described by a 28-byte record of its own starting at offset 196,
/// and <see cref="NumElements"/> says how many of those records are filled in. Only the first was
/// modelled, so the writer described a one-channel image and left the other two records zero while
/// writing three channels' worth of pixels — ImageMagick read the result as a single channel and
/// returned a cyan ramp.
/// </remarks>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(156, 36)]
[Filler(194, 2)]
[Filler(199, 1)]
[Filler(227, 1)]
[Filler(255, 1)]
[Filler(280, 744)]
public readonly partial record struct CineonHeader(
 int Magic,
 int ImageDataOffset,
 int GenericHeaderLength,
 int IndustryHeaderLength,
 int UserDataLength,
 int FileSize,
 [property: SeqField(Size = 8)] string Version,
 [property: SeqField(Size = 100)] string FileName,
 [property: SeqField(Size = 12)] string CreateDate,
 [property: SeqField(Size = 12)] string CreateTime,
 [property: FieldOffset(192)] byte Orientation,
 /// <summary>How many of the element records below are filled in: 3 for RGB, 1 for greyscale.</summary>
 byte NumElements,
 [property: FieldOffset(196)] byte DesignatorCode1,
 byte DesignatorCode2,
 byte BitsPerSample,
 [property: FieldOffset(200)] int PixelsPerLine,
 int LinesPerElement,
 float MinData,
 float MinQuantity,
 float MaxData,
 float MaxQuantity,
 [property: FieldOffset(224)] byte DesignatorCode1Green,
 byte DesignatorCode2Green,
 byte BitsPerSampleGreen,
 [property: FieldOffset(228)] int PixelsPerLineGreen,
 int LinesPerElementGreen,
 float MinDataGreen,
 float MinQuantityGreen,
 float MaxDataGreen,
 float MaxQuantityGreen,
 [property: FieldOffset(252)] byte DesignatorCode1Blue,
 byte DesignatorCode2Blue,
 byte BitsPerSampleBlue,
 [property: FieldOffset(256)] int PixelsPerLineBlue,
 int LinesPerElementBlue,
 float MinDataBlue,
 float MinQuantityBlue,
 float MaxDataBlue,
 float MaxQuantityBlue
) {

 /// <summary>The generic header alone. The image data of a real file starts after the image part too.</summary>
 public const int StructSize = 1024;

 /// <summary>Where a Cineon file's pixels begin: past both header parts.</summary>
 public const int ImageDataStart = 2048;

 public const int MagicNumber = unchecked((int)0x802A5FD7);

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<CineonHeader>();
}
