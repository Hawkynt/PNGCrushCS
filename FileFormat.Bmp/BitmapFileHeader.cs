using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>The 14-byte BITMAPFILEHEADER at the start of every BMP file.</summary>
public readonly record struct BitmapFileHeader(byte Sig1, byte Sig2, int FileSize, short Reserved1, short Reserved2, int PixelDataOffset) {

  public const int StructSize = 14;

  public static BitmapFileHeader ReadFrom(ReadOnlySpan<byte> source) => new(
    source[0],
    source[1],
    BinaryPrimitives.ReadInt32LittleEndian(source[2..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[6..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[8..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[10..])
  );

  public void WriteTo(Span<byte> destination) {
    destination[0] = this.Sig1;
    destination[1] = this.Sig2;
    BinaryPrimitives.WriteInt32LittleEndian(destination[2..], this.FileSize);
    BinaryPrimitives.WriteInt16LittleEndian(destination[6..], this.Reserved1);
    BinaryPrimitives.WriteInt16LittleEndian(destination[8..], this.Reserved2);
    BinaryPrimitives.WriteInt32LittleEndian(destination[10..], this.PixelDataOffset);
  }

  public static HeaderFieldDescriptor[] GetFieldMap() => [
    new("Sig1", 0, 1),
    new("Sig2", 1, 1),
    new("FileSize", 2, 4),
    new("Reserved1", 6, 2),
    new("Reserved2", 8, 2),
    new("PixelDataOffset", 10, 4)
  ];
}
