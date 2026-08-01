using System;
using System.IO;

namespace FileFormat.FaxG3;

/// <summary>Reads Raw Group 3 fax image files from bytes, streams, or file paths.</summary>
public static class FaxG3Reader {

  public static FaxG3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FaxG3 file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FaxG3File FromStream(Stream stream) {
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

  public static FaxG3File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < FaxG3File.HeaderSize)
      throw new InvalidDataException("Data too small for a valid FaxG3 file.");

    // Nothing in a bare fax stream says how wide the page is, so the standard scan line is assumed
    // and the rows are counted by decoding until the coding runs out. Reading a size out of the
    // first four bytes, as this used to, reads two Huffman codes as a page size.
    var pixelData = FileFormat.Ccitt.CcittG3Decoder.Decode(
      data.ToArray(), FaxG3File.StandardWidth, FaxG3File.MaximumRows, out var height);

    if (height <= 0)
      throw new InvalidDataException("No fax rows could be decoded.");

    var stride = (FaxG3File.StandardWidth + 7) / 8;

    return new() {
      Width = FaxG3File.StandardWidth,
      Height = height,
      PixelData = pixelData[..(stride * height)],
    };
    }

  public static FaxG3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
