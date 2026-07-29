using System;
using System.IO;

namespace FileFormat.FunGraphicsMachine;

/// <summary>Reads Commodore 64 Fun Graphics Machine files from bytes, streams, or file paths.</summary>
public static class FunGraphicsMachineReader {

  public static FunGraphicsMachineFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fun Graphics Machine file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FunGraphicsMachineFile FromStream(Stream stream) {
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

  public static FunGraphicsMachineFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < FunGraphicsMachineFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Fun Graphics Machine file (expected {FunGraphicsMachineFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != FunGraphicsMachineFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Fun Graphics Machine file size (expected {FunGraphicsMachineFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += FunGraphicsMachineFile.LoadAddressSize;

    var screenRam = new byte[FunGraphicsMachineFile.ScreenRamSize];
    data.Slice(offset, FunGraphicsMachineFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += FunGraphicsMachineFile.ScreenRamSize;

    var bitmapData = new byte[FunGraphicsMachineFile.BitmapDataSize];
    data.Slice(offset, FunGraphicsMachineFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      ScreenRam = screenRam,
      BitmapData = bitmapData,
    };
    }

  public static FunGraphicsMachineFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FunGraphicsMachineFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Fun Graphics Machine file (expected {FunGraphicsMachineFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != FunGraphicsMachineFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Fun Graphics Machine file size (expected {FunGraphicsMachineFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += FunGraphicsMachineFile.LoadAddressSize;

    var screenRam = new byte[FunGraphicsMachineFile.ScreenRamSize];
    data.AsSpan(offset, FunGraphicsMachineFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += FunGraphicsMachineFile.ScreenRamSize;

    var bitmapData = new byte[FunGraphicsMachineFile.BitmapDataSize];
    data.AsSpan(offset, FunGraphicsMachineFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      ScreenRam = screenRam,
      BitmapData = bitmapData,
    };
  }
}
