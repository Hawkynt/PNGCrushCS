using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiresManager;

/// <summary>Reads Hires Manager (.him) files from bytes, streams, or file paths.</summary>
/// <remarks>
/// The eighth video matrix runs into the end of the bank, so it is read for as far as the file goes
/// rather than for a whole page. Only 960 of its thousand entries are ever asked for — 192 rows is
/// twenty-four character rows — so the tail that is missing is address space and not picture.
/// </remarks>
public static class HiresManagerReader {

  public static HiresManagerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires Manager file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiresManagerFile FromStream(Stream stream) {
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

  public static HiresManagerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HiresManagerFile.FileSize)
      throw new InvalidDataException(
        $"A Hires Manager picture takes {HiresManagerFile.FileSize} bytes; this file is {data.Length}.");

    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line) {
      var at = HiresManagerFile.MatricesOffset + line * Commodore64Fli.MatrixStride;
      data.Slice(at, HiresManagerFile.MatrixEntries).CopyTo(matrices.AsSpan(line * Commodore64Fli.MatrixStride));
    }

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      BitmapData = data.Slice(HiresManagerFile.BitmapOffset, HiresManagerFile.BitmapSize).ToArray(),
      Matrices = matrices,
    };
  }

  public static HiresManagerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
