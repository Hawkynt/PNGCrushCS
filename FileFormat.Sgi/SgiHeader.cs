using FileFormat.Core;

namespace FileFormat.Sgi;

/// <summary>The 512-byte header at the start of every SGI image file.</summary>
[HeaderFiller("Padding", 108, 404)]
[GenerateSerializer]
public readonly partial record struct SgiHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] short Magic,
  [property: HeaderField(2, 1)] byte Compression,
  [property: HeaderField(3, 1)] byte BytesPerChannel,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] ushort Dimension,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] ushort XSize,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] ushort YSize,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] ushort ZSize,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] int PixMin,
  [property: HeaderField(16, 4, Endianness = Endianness.Big)] int PixMax,
  [property: HeaderField(20, 4, Endianness = Endianness.Big)] int Dummy,
  [property: HeaderField(24, 80)] string ImageName,
  [property: HeaderField(104, 4, Endianness = Endianness.Big)] int Colormap
) {

  public const int StructSize = 512;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<SgiHeader>();
}
