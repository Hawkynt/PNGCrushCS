using FileFormat.Core;

namespace FileFormat.Vtf;

/// <summary>The 64-byte VTF file header (v7.2 core).</summary>
[GenerateSerializer]
public readonly partial record struct VtfHeader(
  [property: HeaderField(0, 1)] byte Sig0,
  [property: HeaderField(1, 1)] byte Sig1,
  [property: HeaderField(2, 1)] byte Sig2,
  [property: HeaderField(3, 1)] byte Sig3,
  [property: HeaderField(4, 4)] int VersionMajor,
  [property: HeaderField(8, 4)] int VersionMinor,
  [property: HeaderField(12, 4)] int HeaderSize,
  [property: HeaderField(16, 2)] short Width,
  [property: HeaderField(18, 2)] short Height,
  [property: HeaderField(20, 4)] int Flags,
  [property: HeaderField(24, 2)] short Frames,
  [property: HeaderField(26, 2)] short FirstFrame,
  [property: HeaderField(28, 4)] int Padding0,
  [property: HeaderField(32, 4)] float ReflectivityR,
  [property: HeaderField(36, 4)] float ReflectivityG,
  [property: HeaderField(40, 4)] float ReflectivityB,
  [property: HeaderField(44, 4)] int Padding1,
  [property: HeaderField(48, 4)] float BumpmapScale,
  [property: HeaderField(52, 4)] int HighResFormat,
  [property: HeaderField(56, 1)] byte MipmapCount,
  [property: HeaderField(57, 4)] int LowResFormat,
  [property: HeaderField(61, 1)] byte LowResWidth,
  [property: HeaderField(62, 1)] byte LowResHeight,
  [property: HeaderField(63, 1)] byte Padding2
) {

  public const int StructSize = 64;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<VtfHeader>();
}
