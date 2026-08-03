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

  public static Vidcom64File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Vidcom64File.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Vidcom 64 file (expected {Vidcom64File.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != Vidcom64File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Vidcom 64 file size (expected {Vidcom64File.ExpectedFileSize} bytes, got {data.Length}).");

    // The sections come colour, screen, bitmap after the load address, and the first two sit in a
    // kilobyte each rather than the thousand bytes they use — 2 plus 1024 plus 1024 plus 8000 is
    // 10050, which is the file to the byte. This read a 47-byte header and then bitmap, screen,
    // colour, so nothing landed where it belonged; the giveaway is that the file opens with values
    // no higher than 15, which is colour RAM, and ends with the bitmap.
    //
    // Established against RECOIL and XnView, which agree with each other: read this way every pixel
    // of the sample falls in the same region as theirs, and on no other arrangement tried does more
    // than three quarters.
    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var colorRam = data.Slice(Vidcom64File.ColorRamOffset, Vidcom64File.ColorRamSize).ToArray();
    var screenRam = data.Slice(Vidcom64File.ScreenRamOffset, Vidcom64File.ScreenRamSize).ToArray();
    var bitmapData = data.Slice(Vidcom64File.BitmapOffset, Vidcom64File.BitmapDataSize).ToArray();

    // What the padding after each of the first two sections holds is not established; it is all
    // zeros in the sample, and so is the background the picture is drawn against.
    var headerData = data.Slice(Vidcom64File.ColorRamOffset + Vidcom64File.ColorRamSize, Vidcom64File.HeaderDataSize).ToArray();
    const byte backgroundColor = 0;

    return new() {
      LoadAddress = loadAddress,
      HeaderData = headerData,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor
    };
    }

  public static Vidcom64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
