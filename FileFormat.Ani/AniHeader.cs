using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Ani;

/// <summary>ANI header data from the 'anih' chunk (36 bytes).</summary>
public readonly record struct AniHeader(
  int CbSize,
  int NumFrames,
  int NumSteps,
  int Width,
  int Height,
  int BitCount,
  int NumPlanes,
  int DisplayRate,
  int Flags
) {

  public const int StructSize = 36;

  /// <summary>Whether the ANI has a sequence chunk (flag bit 0).</summary>
  public bool HasSequence => (this.Flags & 1) != 0;

  public static AniHeader ReadFrom(ReadOnlySpan<byte> source) => new(
    BinaryPrimitives.ReadInt32LittleEndian(source),
    BinaryPrimitives.ReadInt32LittleEndian(source[4..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[8..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[12..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[16..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[20..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[24..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[28..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[32..])
  );

  public void WriteTo(Span<byte> destination) {
    BinaryPrimitives.WriteInt32LittleEndian(destination, this.CbSize);
    BinaryPrimitives.WriteInt32LittleEndian(destination[4..], this.NumFrames);
    BinaryPrimitives.WriteInt32LittleEndian(destination[8..], this.NumSteps);
    BinaryPrimitives.WriteInt32LittleEndian(destination[12..], this.Width);
    BinaryPrimitives.WriteInt32LittleEndian(destination[16..], this.Height);
    BinaryPrimitives.WriteInt32LittleEndian(destination[20..], this.BitCount);
    BinaryPrimitives.WriteInt32LittleEndian(destination[24..], this.NumPlanes);
    BinaryPrimitives.WriteInt32LittleEndian(destination[28..], this.DisplayRate);
    BinaryPrimitives.WriteInt32LittleEndian(destination[32..], this.Flags);
  }

  public static HeaderFieldDescriptor[] GetFieldMap() => [
    new("CbSize", 0, 4),
    new("NumFrames", 4, 4),
    new("NumSteps", 8, 4),
    new("Width", 12, 4),
    new("Height", 16, 4),
    new("BitCount", 20, 4),
    new("NumPlanes", 24, 4),
    new("DisplayRate", 28, 4),
    new("Flags", 32, 4)
  ];
}
