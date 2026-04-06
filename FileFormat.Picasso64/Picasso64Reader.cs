using System;
using System.IO;

namespace FileFormat.Picasso64;

/// <summary>Reads Commodore 64 Picasso 64 files from bytes, streams, or file paths.</summary>
public static class Picasso64Reader {

  public static Picasso64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picasso 64 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Picasso64File FromStream(Stream stream) {
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

  public static Picasso64File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Picasso64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Picasso64File.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Picasso 64 file (expected {Picasso64File.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != Picasso64File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Picasso 64 file size (expected {Picasso64File.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += Picasso64File.LoadAddressSize;

    var bitmapData = new byte[Picasso64File.BitmapDataSize];
    data.AsSpan(offset, Picasso64File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += Picasso64File.BitmapDataSize;

    var screenRam = new byte[Picasso64File.ScreenRamSize];
    data.AsSpan(offset, Picasso64File.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += Picasso64File.ScreenRamSize;

    var colorRam = new byte[Picasso64File.ColorRamSize];
    data.AsSpan(offset, Picasso64File.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += Picasso64File.ColorRamSize;

    var backgroundColor = data[offset++];
    var borderColor = data[offset++];

    var extraData = new byte[Picasso64File.ExtraDataSize];
    data.AsSpan(offset, Picasso64File.ExtraDataSize).CopyTo(extraData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
      BorderColor = borderColor,
      ExtraData = extraData,
    };
  }
}
