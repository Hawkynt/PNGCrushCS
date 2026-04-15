using FileFormat.Core;

namespace FileFormat.QuantumPaint;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct QuantumPaintHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
