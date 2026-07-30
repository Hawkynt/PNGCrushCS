using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariHardInterlace;

/// <summary>Reads Atari 8-bit Hard Interlace Pictures from bytes, streams, or file paths.</summary>
public static class AtariHardInterlaceReader {

  public static AtariHardInterlaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hard Interlace Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariHardInterlaceFile FromStream(Stream stream) {
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

  public static AtariHardInterlaceFile FromSpan(ReadOnlySpan<byte> data) {
    if (SfdnDecompressor.IsSfdn(data)) {
      var unpacked = SfdnDecompressor.TryUnpack(data, SfdnDecompressor.UnpackedLength(data))
        ?? throw new InvalidDataException("Not a Hard Interlace Picture: the SFDN data does not unpack.");

      return FromSpan((ReadOnlySpan<byte>)unpacked);
    }

    var height = data.Length / AtariHardInterlaceFile.PairStride;
    if (height < 1 || height > AtariHardInterlaceFile.MaxHeight)
      throw new InvalidDataException(
        $"A Hard Interlace Picture is up to {AtariHardInterlaceFile.MaxHeight} scanlines, got {height}.");

    var fieldSize = height * AtariHardInterlaceFile.RowStride;
    var luminances = new byte[fieldSize];
    var colors = new byte[fieldSize];
    data.Slice(0, fieldSize).CopyTo(luminances);
    data.Slice(fieldSize, fieldSize).CopyTo(colors);

    // Whatever is left over past the two fields is the colour registers; most files omit them and
    // take a plain luminance ramp instead.
    var registers = new byte[AtariHardInterlaceFile.RegisterBlockSize];
    if (data.Length % AtariHardInterlaceFile.PairStride == AtariHardInterlaceFile.RegisterBlockSize)
      data.Slice(data.Length - AtariHardInterlaceFile.RegisterBlockSize, AtariHardInterlaceFile.RegisterBlockSize)
        .CopyTo(registers);
    else
      AtariHardInterlaceFile.DefaultRegisters.CopyTo(registers);

    return new() { Height = height, Luminances = luminances, Colors = colors, Registers = registers };
  }

  public static AtariHardInterlaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
