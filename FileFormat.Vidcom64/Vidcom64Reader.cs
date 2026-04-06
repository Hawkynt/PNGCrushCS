using System;
using System.IO;

namespace FileFormat.Vidcom64;

/// <summary>Reads Commodore 64 Vidcom 64 files from bytes, streams, or file paths.</summary>
public static class Vidcom64Reader {

  public static Vidcom64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Vidcom 64 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Vidcom64File FromStream(Stream stream) {
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

  public static Vidcom64File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Vidcom64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Vidcom64File.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Vidcom 64 file (expected {Vidcom64File.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != Vidcom64File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Vidcom 64 file size (expected {Vidcom64File.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += Vidcom64File.LoadAddressSize;

    var headerData = new byte[Vidcom64File.HeaderDataSize];
    data.AsSpan(offset, Vidcom64File.HeaderDataSize).CopyTo(headerData.AsSpan(0));
    offset += Vidcom64File.HeaderDataSize;

    var bitmapData = new byte[Vidcom64File.BitmapDataSize];
    data.AsSpan(offset, Vidcom64File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += Vidcom64File.BitmapDataSize;

    var screenRam = new byte[Vidcom64File.ScreenRamSize];
    data.AsSpan(offset, Vidcom64File.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += Vidcom64File.ScreenRamSize;

    var colorRam = new byte[Vidcom64File.ColorRamSize];
    data.AsSpan(offset, Vidcom64File.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += Vidcom64File.ColorRamSize;

    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      HeaderData = headerData,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor
    };
  }
}
