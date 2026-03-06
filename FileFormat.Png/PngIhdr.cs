using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>The 13-byte IHDR chunk data in a PNG file.</summary>
public readonly record struct PngIhdr(int Width, int Height, byte BitDepth, byte ColorType, byte CompressionMethod, byte FilterMethod, byte InterlaceMethod) {

  public const int StructSize = 13;

  public static PngIhdr ReadFrom(ReadOnlySpan<byte> source) => new(
    BinaryPrimitives.ReadInt32BigEndian(source),
    BinaryPrimitives.ReadInt32BigEndian(source[4..]),
    source[8],
    source[9],
    source[10],
    source[11],
    source[12]
  );

  public void WriteTo(Span<byte> destination) {
    BinaryPrimitives.WriteInt32BigEndian(destination, this.Width);
    BinaryPrimitives.WriteInt32BigEndian(destination[4..], this.Height);
    destination[8] = this.BitDepth;
    destination[9] = this.ColorType;
    destination[10] = this.CompressionMethod;
    destination[11] = this.FilterMethod;
    destination[12] = this.InterlaceMethod;
  }

  public static HeaderFieldDescriptor[] GetFieldMap() => [
    new("Width", 0, 4),
    new("Height", 4, 4),
    new("BitDepth", 8, 1),
    new("ColorType", 9, 1),
    new("CompressionMethod", 10, 1),
    new("FilterMethod", 11, 1),
    new("InterlaceMethod", 12, 1)
  ];
}
