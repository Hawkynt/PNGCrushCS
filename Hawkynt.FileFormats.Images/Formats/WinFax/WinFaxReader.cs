using System;
using System.IO;

using FileFormat.Ccitt;

namespace FileFormat.WinFax;

/// <summary>Reads WinFAX fax image files from bytes, streams, or file paths.</summary>
public static class WinFaxReader {

  public static WinFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WinFax file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WinFaxFile FromStream(Stream stream) {
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

  public static WinFaxFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < WinFaxFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid WinFax file.");

    if (!data[..WinFaxFile.Signature.Length].SequenceEqual(WinFaxFile.Signature))
      throw new InvalidDataException("Not a WinFax file: it does not begin with 0B 23.");

    // The size was read from offsets 0 and 4, which hold the signature and part of the height, and a
    // field that came out wrong was replaced with 1728 by 2200 rather than refusing the file. A
    // 283 KB fax was reported as 8971 by 38918 — 349 megapixels — as a successful read.
    var width = data[WinFaxFile.WidthOffset] | (data[WinFaxFile.WidthOffset + 1] << 8);
    var height = data[WinFaxFile.HeightOffset] | (data[WinFaxFile.HeightOffset + 1] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A WinFax page states {width}x{height}, which is no size.");

    // The page is fax-coded, which is the whole point of the format: a 1728 by 2200 page is 475200
    // bytes flat and these files are a fraction of that.
    // Nothing in the header says which coding a page uses — three samples differ only in size and
    // resolution, yet one is Group 3 and the others are not — so both are tried and whichever
    // produces more of the page wins.
    var coded = data[WinFaxFile.HeaderSize..].ToArray();
    var oneDimensional = CcittG3Decoder.Decode(coded, width, height, out var g3Rows);
    var twoDimensional = CcittG4Decoder.Decode(coded, width, height, out var g4Rows);

    var pixelData = g4Rows > g3Rows ? twoDimensional : oneDimensional;
    if (Math.Max(g3Rows, g4Rows) <= 0)
      throw new InvalidDataException("A WinFax page decoded to no scanlines at all.");

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }

  public static WinFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
