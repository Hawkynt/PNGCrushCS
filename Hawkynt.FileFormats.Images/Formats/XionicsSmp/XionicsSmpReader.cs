using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.XionicsSmp;

/// <summary>Reads Xionics SMP pages from bytes, streams, or file paths.</summary>
public static class XionicsSmpReader {

  private const int _MaxDimension = 65535;

  public static XionicsSmpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Xionics SMP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XionicsSmpFile FromStream(Stream stream) {
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

  public static XionicsSmpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static XionicsSmpFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= XionicsSmpFile.HeaderSize)
      throw new InvalidDataException(
        $"Data too small for a Xionics SMP page (more than {XionicsSmpFile.HeaderSize} bytes are needed, got {data.Length}).");

    if (!data[..XionicsSmpFile.Signature.Length].SequenceEqual(XionicsSmpFile.Signature))
      throw new InvalidDataException("Not a Xionics SMP page: it does not open with the vendor's name.");

    if (BinaryPrimitives.ReadUInt16LittleEndian(data[XionicsSmpFile.OneOffset..]) != 1)
      throw new InvalidDataException("Not a Xionics SMP page: the word at offset 18 is not a one.");

    if (BinaryPrimitives.ReadUInt16LittleEndian(data[XionicsSmpFile.EscapeOffset..]) != 0x1B
        || data[XionicsSmpFile.HorizontalTagOffset] != 0x19
        || data[XionicsSmpFile.VerticalTagOffset] != 0x1A)
      throw new InvalidDataException("Not a Xionics SMP page: the three tags the header ends with are not the format's.");

    var compression = BinaryPrimitives.ReadUInt16LittleEndian(data[XionicsSmpFile.CompressionOffset..]);
    var bytesPerRow = BinaryPrimitives.ReadUInt16LittleEndian(data[XionicsSmpFile.BytesPerRowOffset..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[XionicsSmpFile.HeightOffset..]);
    var horizontal = BinaryPrimitives.ReadUInt16LittleEndian(data[XionicsSmpFile.HorizontalResolutionOffset..]);
    var vertical = BinaryPrimitives.ReadUInt16LittleEndian(data[XionicsSmpFile.VerticalResolutionOffset..]);

    if (bytesPerRow < 1 || height < 1 || height > _MaxDimension)
      throw new InvalidDataException($"A Xionics SMP page states {bytesPerRow} bytes a row and {height} rows.");

    var width = bytesPerRow * 8;
    if (width > _MaxDimension)
      throw new InvalidDataException($"A Xionics SMP page states a row of {width} pixels.");

    var body = data[XionicsSmpFile.HeaderSize..];
    var pixelData = compression switch {
      XionicsSmpFile.CompressionNone => _Raw(body, bytesPerRow, height),
      XionicsSmpFile.CompressionGroup3 => _Fax(body, width, height, group4: false),
      XionicsSmpFile.CompressionGroup4 => _Fax(body, width, height, group4: true),
      XionicsSmpFile.CompressionGroup3TwoDimensional => throw new InvalidDataException(
        "A Xionics SMP page states Group 3 two-dimensional coding, which is not decoded here."),
      _ => throw new InvalidDataException(
        $"A Xionics SMP page states coding {compression}, which is the vendor's own run-length scheme and is not decoded here."),
    };

    return new() {
      Width = width,
      Height = height,
      Compression = compression,
      HorizontalResolution = horizontal,
      VerticalResolution = vertical,
      PixelData = pixelData,
    };
  }

  private static byte[] _Raw(ReadOnlySpan<byte> body, int bytesPerRow, int height) {
    var needed = bytesPerRow * height;
    if (body.Length < needed)
      throw new InvalidDataException(
        $"A Xionics SMP page is truncated: {height} rows of {bytesPerRow} bytes need {needed}, and {body.Length} are there.");

    return body[..needed].ToArray();
  }

  private static byte[] _Fax(ReadOnlySpan<byte> body, int width, int height, bool group4) {
    // The coding runs from the bottom bit of each byte upwards, whichever of the two it is.
    var coded = CcittFillOrder.Reverse(body);
    var pixelData = group4
      ? CcittG4Decoder.Decode(coded, width, height, out var rowsDecoded)
      : CcittG3Decoder.Decode(coded, width, height, out rowsDecoded);

    if (rowsDecoded != height)
      throw new InvalidDataException(
        $"A Xionics SMP page's coding runs out after {rowsDecoded} of the {height} rows its header states.");

    return pixelData;
  }
}
