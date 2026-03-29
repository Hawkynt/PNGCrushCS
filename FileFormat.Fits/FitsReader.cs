using System;
using System.IO;
using System.Text;

namespace FileFormat.Fits;

/// <summary>Reads FITS files from bytes, streams, or file paths.</summary>
public static class FitsReader {

  private const int _BLOCK_SIZE = 2880;

  public static FitsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FITS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FitsFile FromStream(Stream stream) {
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

  public static FitsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _BLOCK_SIZE)
      throw new InvalidDataException("Data too small for a valid FITS file.");

    // Validate SIMPLE keyword
    var firstCard = Encoding.ASCII.GetString(data, 0, Math.Min(80, data.Length));
    if (!firstCard.StartsWith("SIMPLE"))
      throw new InvalidDataException("Invalid FITS file: missing SIMPLE keyword.");

    var equalsPos = firstCard.IndexOf('=');
    if (equalsPos < 0)
      throw new InvalidDataException("Invalid FITS file: SIMPLE keyword has no value.");

    var simpleValue = firstCard[(equalsPos + 1)..].Trim();
    if (!simpleValue.StartsWith('T') && !simpleValue.StartsWith("T"))
      throw new InvalidDataException("Invalid FITS file: SIMPLE is not T.");

    // Parse header
    var (keywords, headerLength) = FitsHeaderParser.Parse(data);

    var bitpix = FitsHeaderParser.GetBitpix(keywords);
    var naxis = FitsHeaderParser.GetIntValue(keywords, "NAXIS");

    var width = 0;
    var height = 0;
    if (naxis >= 1)
      width = FitsHeaderParser.GetIntValue(keywords, "NAXIS1");
    if (naxis >= 2)
      height = FitsHeaderParser.GetIntValue(keywords, "NAXIS2");

    // Read pixel data
    var bytesPerPixel = Math.Abs((int)bitpix) / 8;
    var pixelCount = (long)width * height;
    var dataSize = (int)(pixelCount * bytesPerPixel);
    var availableData = Math.Min(dataSize, data.Length - headerLength);

    var pixelData = new byte[availableData > 0 ? availableData : 0];
    if (availableData > 0)
      data.AsSpan(headerLength, availableData).CopyTo(pixelData.AsSpan(0));

    return new FitsFile {
      Width = width,
      Height = height,
      Bitpix = bitpix,
      Keywords = keywords,
      PixelData = pixelData
    };
  }
}
