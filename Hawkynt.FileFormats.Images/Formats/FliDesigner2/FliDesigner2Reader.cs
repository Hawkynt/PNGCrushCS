using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliDesigner2;

/// <summary>Reads FLI Designer 2 (.fd2) files from bytes, streams, or file paths.</summary>
public static class FliDesigner2Reader {

  public static FliDesigner2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Designer 2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliDesigner2File FromStream(Stream stream) {
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

  public static FliDesigner2File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FliDesigner2File.PictureSize)
      throw new InvalidDataException(
        $"A FLI Designer 2 picture takes {FliDesigner2File.FileSize} bytes; this file is {data.Length}.");

    var trailer = Math.Min(data.Length, FliDesigner2File.FileSize) - FliDesigner2File.PictureSize;

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      ColorRam = data.Slice(FliDesigner2File.ColorRamOffset, Commodore64Fli.ColorRamSize).ToArray(),
      Matrices = data.Slice(FliDesigner2File.MatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
      BitmapData = data.Slice(FliDesigner2File.BitmapOffset, Commodore64Fli.BitmapSize).ToArray(),
      Trailer = data.Slice(FliDesigner2File.PictureSize, trailer).ToArray(),
    };
  }

  public static FliDesigner2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
