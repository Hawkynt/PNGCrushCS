using FileFormat.Core;

namespace FileFormat.MegaPaint;

/// <summary>The 8-byte header of a MegaPaint file: Width (ushort BE), Height (ushort BE), Reserved (4 bytes).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct MegaPaintHeader(
  ushort Width,
  ushort Height,
  uint Reserved
) {
  public const int StructSize = 8;
}
