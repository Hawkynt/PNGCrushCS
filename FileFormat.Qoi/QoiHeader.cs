using FileFormat.Core;

namespace FileFormat.Qoi;

/// <summary>The 14-byte header at the start of every QOI file.</summary>
[GenerateSerializer]
public readonly partial record struct QoiHeader(
  [property: HeaderField(0, 1)] byte Magic1,
  [property: HeaderField(1, 1)] byte Magic2,
  [property: HeaderField(2, 1)] byte Magic3,
  [property: HeaderField(3, 1)] byte Magic4,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] uint Width,
  [property: HeaderField(8, 4, Endianness = Endianness.Big)] uint Height,
  [property: HeaderField(12, 1)] QoiChannels Channels,
  [property: HeaderField(13, 1)] QoiColorSpace ColorSpace
) {

  public const int StructSize = 14;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<QoiHeader>();
}
