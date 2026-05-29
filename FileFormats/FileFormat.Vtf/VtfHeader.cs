using FileFormat.Core;

namespace FileFormat.Vtf;

/// <summary>The 64-byte VTF file header (v7.2 core).</summary>
[GenerateSerializer]
public readonly partial record struct VtfHeader(
  byte Sig0,
  byte Sig1,
  byte Sig2,
  byte Sig3,
  int VersionMajor,
  int VersionMinor,
  int HeaderSize,
  short Width,
  short Height,
  int Flags,
  short Frames,
  short FirstFrame,
  int Padding0,
  float ReflectivityR,
  float ReflectivityG,
  float ReflectivityB,
  int Padding1,
  float BumpmapScale,
  int HighResFormat,
  byte MipmapCount,
  int LowResFormat,
  byte LowResWidth,
  byte LowResHeight,
  byte Padding2
) {

 public const int StructSize = 64;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<VtfHeader>();
}
