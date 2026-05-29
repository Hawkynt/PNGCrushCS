using FileFormat.Core;

namespace FileFormat.Psd;

/// <summary>The 26-byte file header at the start of every PSD/PSB file (all big-endian).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct PsdHeader(
  byte Sig0,
  byte Sig1,
  byte Sig2,
  byte Sig3,
  short Version,
  byte Reserved0,
  byte Reserved1,
  byte Reserved2,
  byte Reserved3,
  byte Reserved4,
  byte Reserved5,
  short Channels,
  int Height,
  int Width,
  short Depth,
  short ColorMode
) {

 public const int StructSize = 26;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PsdHeader>();
}
