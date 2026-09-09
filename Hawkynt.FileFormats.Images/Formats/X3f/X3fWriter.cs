using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FileFormat.X3f;

/// <summary>Assembles X3F 2.2 files using the specification's uncompressed processed-image storage.</summary>
public static class X3fWriter {

  private const uint _FileVersion = 0x00020002;
  private const uint _SectionVersion = 0x00020000;
  private const uint _ImageTypeProcessed = 2;
  private const int _DirectoryHeaderSize = 12;

  public static byte[] ToBytes(X3fFile file) {
    if (file.Width < 1)
      throw new ArgumentOutOfRangeException(nameof(file), file.Width, "X3F width must be positive.");
    if (file.Height < 1)
      throw new ArgumentOutOfRangeException(nameof(file), file.Height, "X3F height must be positive.");
    ArgumentNullException.ThrowIfNull(file.PixelData);

    var packedRow = checked((long)file.Width * 3);
    var stride = checked((packedRow + 3) & ~3L);
    var packedBytes = checked(packedRow * file.Height);
    var bodyBytes = checked(stride * file.Height);
    var sectionLength = checked((long)X3fFile.ImageSectionHeaderSize + bodyBytes);
    var directoryOffset = checked((long)X3fFile.HeaderSize + sectionLength);
    var fileSize = checked(directoryOffset + _DirectoryHeaderSize + X3fFile.DirectoryEntrySize + sizeof(uint));

    if (packedBytes > int.MaxValue || stride > uint.MaxValue || sectionLength > uint.MaxValue
        || directoryOffset > uint.MaxValue || fileSize > int.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file), "X3F image is too large for the format's 32-bit offsets and this byte-array writer.");

    if (file.PixelData.Length != (int)packedBytes)
      throw new ArgumentException(
        $"X3F RGB24 data for {file.Width} by {file.Height} must contain exactly {packedBytes} bytes, not {file.PixelData.Length}.",
        nameof(file));

    var result = new byte[(int)fileSize];

    X3fFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), _FileVersion);
    RandomNumberGenerator.Fill(result.AsSpan(8, 16));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(X3fFile.ColumnsField), (uint)file.Width);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(X3fFile.RowsField), (uint)file.Height);

    var sectionOffset = X3fFile.HeaderSize;
    var section = result.AsSpan(sectionOffset, (int)sectionLength);
    section[0] = (byte)'S';
    section[1] = (byte)'E';
    section[2] = (byte)'C';
    section[3] = (byte)'i';
    BinaryPrimitives.WriteUInt32LittleEndian(section[4..], _SectionVersion);
    BinaryPrimitives.WriteUInt32LittleEndian(section[8..], _ImageTypeProcessed);
    BinaryPrimitives.WriteUInt32LittleEndian(section[12..], (uint)X3fFile.FormatRgb24);
    BinaryPrimitives.WriteUInt32LittleEndian(section[16..], (uint)file.Width);
    BinaryPrimitives.WriteUInt32LittleEndian(section[20..], (uint)file.Height);
    BinaryPrimitives.WriteUInt32LittleEndian(section[24..], (uint)stride);

    var packedRowInt = (int)packedRow;
    var strideInt = (int)stride;
    for (var y = 0; y < file.Height; ++y)
      file.PixelData.AsSpan(y * packedRowInt, packedRowInt)
        .CopyTo(section.Slice(X3fFile.ImageSectionHeaderSize + y * strideInt, packedRowInt));

    var directoryAt = (int)directoryOffset;
    var directory = result.AsSpan(directoryAt);
    X3fFile.DirectoryMagic.CopyTo(directory);
    BinaryPrimitives.WriteUInt32LittleEndian(directory[4..], _SectionVersion);
    BinaryPrimitives.WriteUInt32LittleEndian(directory[8..], 1);
    BinaryPrimitives.WriteUInt32LittleEndian(directory[12..], (uint)sectionOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(directory[16..], (uint)sectionLength);
    directory[20] = (byte)'I';
    directory[21] = (byte)'M';
    directory[22] = (byte)'A';
    directory[23] = (byte)'G';

    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(result.Length - sizeof(uint)), (uint)directoryOffset);
    return result;
  }
}
