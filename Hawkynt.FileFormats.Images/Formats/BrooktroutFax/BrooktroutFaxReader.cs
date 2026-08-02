using System;
using System.IO;

using FileFormat.Ccitt;

namespace FileFormat.BrooktroutFax;

/// <summary>Reads Brooktrout 301 fax image files from bytes, streams, or file paths.</summary>
public static class BrooktroutFaxReader {

  public static BrooktroutFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BrooktroutFax file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BrooktroutFaxFile FromStream(Stream stream) {
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

  public static BrooktroutFaxFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < BrooktroutFaxFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Brooktrout fax.");

    if (!data[..BrooktroutFaxFile.Signature.Length].SequenceEqual(BrooktroutFaxFile.Signature))
      throw new InvalidDataException("Not a Brooktrout fax: it does not begin with BB 01.");

    // The size used to be read from offsets 0 and 4 — the signature and the resolution — and a field
    // that came out wrong was replaced with 1728 by 2200 rather than refusing the file.
    var width = data[BrooktroutFaxFile.WidthOffset] | (data[BrooktroutFaxFile.WidthOffset + 1] << 8);
    var height = data[BrooktroutFaxFile.HeightOffset] | (data[BrooktroutFaxFile.HeightOffset + 1] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A Brooktrout fax states {width}x{height}, which is no size.");

    // The page is fax-coded and begins with a synchronising code on the boundary past the header.
    var coded = data[BrooktroutFaxFile.HeaderSize..].ToArray();
    var oneDimensional = CcittG3Decoder.Decode(coded, width, height, out var g3Rows);
    var twoDimensional = CcittG4Decoder.Decode(coded, width, height, out var g4Rows);

    var pixelData = g4Rows > g3Rows ? twoDimensional : oneDimensional;
    if (Math.Max(g3Rows, g4Rows) <= 0)
      throw new InvalidDataException("A Brooktrout page decoded to no scanlines at all.");

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static BrooktroutFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
