using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.FliGraph;

/// <summary>Reads FLI Graph pictures from bytes, streams, or file paths.</summary>
public static class FliGraphReader {

  public static FliGraphFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Graph file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliGraphFile FromStream(Stream stream) {
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

  public static FliGraphFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FliGraphFile.MinimumFileSize)
      throw new InvalidDataException(
        $"An FLI Graph picture takes at least {FliGraphFile.MinimumFileSize} bytes; this file is {data.Length}.");

    var screens = new byte[FliGraphFile.ScreenBankCount * FliGraphFile.BankSize];
    for (var bank = 0; bank < FliGraphFile.ScreenBankCount; ++bank)
      data.Slice(FliGraphFile.ScreensOffset + bank * FliGraphFile.BankStride, FliGraphFile.BankSize)
        .CopyTo(screens.AsSpan(bank * FliGraphFile.BankSize));

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      ColorRam = data.Slice(FliGraphFile.ColorRamOffset, FliGraphFile.BankSize).ToArray(),
      Screens = screens,
      BitmapData = data.Slice(FliGraphFile.BitmapOffset, FliGraphFile.BitmapDataSize).ToArray(),
    };
  }

  public static FliGraphFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
