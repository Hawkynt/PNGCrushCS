using System;
using System.IO;

namespace FileFormat.Astc;

/// <summary>Reads ASTC files from bytes, streams, or file paths.</summary>
public static class AstcReader {

  public static AstcFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ASTC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AstcFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AstcFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AstcHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid ASTC file.");

    var span = data;
    var header = AstcHeader.ReadFrom(span);

    if (header.Magic != AstcHeader.MagicValue)
      throw new InvalidDataException("Invalid ASTC magic number.");

    var compressedDataLength = data.Length - AstcHeader.StructSize;
    var compressedData = new byte[compressedDataLength];
    data.Slice(AstcHeader.StructSize, compressedDataLength).CopyTo(compressedData.AsSpan(0));

    return new AstcFile {
      Width = header.Width,
      Height = header.Height,
      Depth = header.Depth,
      BlockDimX = header.BlockDimX,
      BlockDimY = header.BlockDimY,
      BlockDimZ = header.BlockDimZ,
      CompressedData = compressedData
    };
    }

  public static AstcFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
