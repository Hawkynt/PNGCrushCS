using System;
using System.IO;
using FileFormat.Ccitt;
using FileFormat.Core;

namespace FileFormat.RicohFax;

/// <summary>Reads Ricoh Fax pages from bytes, streams, or file paths.</summary>
public static class RicohFaxReader {

  public static RicohFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ricoh Fax file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RicohFaxFile FromStream(Stream stream) {
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

  public static RicohFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static RicohFaxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < RicohFaxFile.MinFileSize)
      throw new InvalidDataException(
        $"Data too small for a Ricoh Fax page (at least {RicohFaxFile.MinFileSize} bytes are needed, got {data.Length}).");

    if (!data.Slice(RicohFaxFile.SignatureOffset, RicohFaxFile.Signature.Length).SequenceEqual(RicohFaxFile.Signature))
      throw new InvalidDataException("Not a Ricoh Fax page: the fourteen characters at offset 2 do not name it.");

    // The bits run from the bottom of each byte upwards, so every byte is turned over first.
    var coded = CcittFillOrder.Reverse(data[RicohFaxFile.HeaderSize..]);

    // The page states no height, so the decoder is given a ceiling and asked how far it got. The
    // ceiling is bounded by the coding's own length as well as by the converter's limit, since a row
    // costs at least the twelve bits of its separator.
    var ceiling = Math.Min(RicohFaxFile.MaxRows, coded.Length + 1);
    var page = CcittG3Decoder.Decode(coded, RicohFaxFile.PageWidth, ceiling, out var rows);
    if (rows < 1)
      throw new InvalidDataException("A Ricoh Fax page carries no row this can decode.");

    var stride = BilevelRows.Stride(RicohFaxFile.PageWidth);
    return new() { Height = rows, PixelData = page.AsSpan(0, stride * rows).ToArray() };
  }
}
