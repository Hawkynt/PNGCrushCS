using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ffli;

/// <summary>Reads Flash FLI (.ffl, .ffli) files from bytes, streams, or file paths.</summary>
public static class FfliReader {

  public static FfliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Flash FLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FfliFile FromStream(Stream stream) {
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

  public static FfliFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FfliFile.FileSize)
      throw new InvalidDataException(
        $"A Flash FLI picture takes {FfliFile.FileSize} bytes; this file is {data.Length}.");

    if (data[FfliFile.SignatureOffset] != FfliFile.Signature)
      throw new InvalidDataException("A Flash FLI names itself with a lower-case f in its third byte; this file does not.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      FirstBackgrounds = data.Slice(FfliFile.FirstBackgroundsOffset, FfliFile.FixedHeight).ToArray(),
      ColorRam = data.Slice(FfliFile.ColorRamOffset, Commodore64Fli.ColorRamSize).ToArray(),
      FirstMatrices = data.Slice(FfliFile.FirstMatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
      BitmapData = data.Slice(FfliFile.BitmapOffset, Commodore64Fli.BitmapSize).ToArray(),
      SecondMatrices = data.Slice(FfliFile.SecondMatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
      SecondBackgrounds = data.Slice(FfliFile.SecondBackgroundsOffset, FfliFile.FixedHeight).ToArray(),
    };
  }

  public static FfliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
