using FileFormat.Core;

namespace FileFormat.Sgi;

/// <summary>The 512-byte header at the start of every SGI image file.</summary>
[Filler(108, 404)]
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct SgiHeader( short Magic, byte Compression, byte BytesPerChannel, ushort Dimension, ushort XSize, ushort YSize, ushort ZSize, int PixMin, int PixMax, int Dummy, string ImageName, int Colormap
) {

 public const int StructSize = 512;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<SgiHeader>();
}
