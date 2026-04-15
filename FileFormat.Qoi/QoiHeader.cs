using FileFormat.Core;

namespace FileFormat.Qoi;

/// <summary>The 14-byte header at the start of every QOI file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct QoiHeader(
  byte Magic1,
  byte Magic2,
  byte Magic3,
  byte Magic4,
  uint Width,
  uint Height,
  QoiChannels Channels,
  QoiColorSpace ColorSpace
) {

 public const int StructSize = 14;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<QoiHeader>();
}
