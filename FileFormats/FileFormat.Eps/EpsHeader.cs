using FileFormat.Core;

namespace FileFormat.Eps;

/// <summary>The 30-byte binary header of a DOS EPS file (all fields little-endian).</summary>
[GenerateSerializer]
internal readonly partial record struct EpsHeader(
  uint Magic,
  uint PsOffset,
  uint PsLength,
  uint WmfOffset,
  uint WmfLength,
  uint TiffOffset,
  uint TiffLength,
  ushort Checksum
) {
  public const int StructSize = 30;

  /// <summary>Expected magic value: C5 D0 D3 C6 (read as LE uint32).</summary>
  public const uint ExpectedMagic = 0xC6D3D0C5;
}
