using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiresFliCrest;

/// <summary>Reads Hires FLI Designer (.hfc, .hfd) files from bytes, streams, or file paths.</summary>
public static class HiresFliCrestReader {

  public static HiresFliCrestFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires FLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiresFliCrestFile FromStream(Stream stream) {
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

  public static HiresFliCrestFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HiresFliCrestFile.FileSize)
      throw new InvalidDataException(
        $"A Hires FLI picture takes {HiresFliCrestFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      BitmapData = data.Slice(HiresFliCrestFile.BitmapOffset, HiresFliCrestFile.BitmapAreaSize).ToArray(),
      Matrices = data.Slice(HiresFliCrestFile.MatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
    };
  }

  public static HiresFliCrestFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
