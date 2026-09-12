using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Flimatic;

/// <summary>Reads Flimatic (.flm) files from bytes, streams, or file paths.</summary>
public static class FlimaticReader {

  public static FlimaticFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Flimatic file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FlimaticFile FromStream(Stream stream) {
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

  public static FlimaticFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FlimaticFile.FileSize)
      throw new InvalidDataException(
        $"A Flimatic picture takes {FlimaticFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      ColorRam = data.Slice(FlimaticFile.ColorRamOffset, Commodore64Fli.ColorRamSize).ToArray(),
      Matrices = data.Slice(FlimaticFile.MatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
      BitmapData = data.Slice(FlimaticFile.BitmapOffset, Commodore64Fli.BitmapSize).ToArray(),
      Background = data[FlimaticFile.BackgroundOffset],
      Trailer = data.Slice(FlimaticFile.PictureSize, FlimaticFile.FileSize - FlimaticFile.PictureSize).ToArray(),
    };
  }

  public static FlimaticFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
