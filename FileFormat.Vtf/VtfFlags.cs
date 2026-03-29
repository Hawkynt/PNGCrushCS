using System;

namespace FileFormat.Vtf;

/// <summary>Texture flags for VTF files.</summary>
[Flags]
public enum VtfFlags {
  None = 0,
  PointSampling = 0x1,
  Trilinear = 0x2,
  ClampS = 0x4,
  ClampT = 0x8,
  Anisotropic = 0x10,
  NoMipmap = 0x100,
  NoLod = 0x200,
  Srgb = 0x40000000
}
