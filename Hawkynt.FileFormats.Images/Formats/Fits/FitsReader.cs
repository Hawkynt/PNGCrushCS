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

  public static FitsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _BLOCK_SIZE)
      throw new InvalidDataException("Data too small for a valid FITS file.");

    // Validate SIMPLE keyword
    var firstCard = Encoding.ASCII.GetString(data[..Math.Min(80, data.Length)]);
    if (!firstCard.StartsWith("SIMPLE"))
      throw new InvalidDataException("Invalid FITS file: missing SIMPLE keyword.");

    var equalsPos = firstCard.IndexOf('=');
    if (equalsPos < 0)
      throw new InvalidDataException("Invalid FITS file: SIMPLE keyword has no value.");

    var simpleValue = firstCard[(equalsPos + 1)..].Trim();
    if (!simpleValue.StartsWith('T') && !simpleValue.StartsWith("T"))
      throw new InvalidDataException("Invalid FITS file: SIMPLE is not T.");

    // Parse header
    var (keywords, headerLength) = FitsHeaderParser.Parse(data.ToArray());

    var bitpix = FitsHeaderParser.GetBitpix(keywords);
    var naxis = FitsHeaderParser.GetIntValue(keywords, "NAXIS");

    var width = 0;
    var height = 0;
    var channels = 1;
    if (naxis >= 1)
      width = FitsHeaderParser.GetIntValue(keywords, "NAXIS1");
    if (naxis >= 2)
      height = FitsHeaderParser.GetIntValue(keywords, "NAXIS2");
    if (naxis >= 3)
      channels = FitsHeaderParser.GetIntValue(keywords, "NAXIS3");

    if (width < 0 || height < 0 || channels < 1)
      throw new InvalidDataException("Invalid FITS axis size.");

    // This image API maps a conventional NAXIS3=3/4 colour cube onto RGB(A). Higher-dimensional
    // scientific arrays are still represented by their first image plane rather than silently
    // multiplying arbitrary axes into a bogus colour count.
    if (naxis > 3)
      channels = 1;

    // Read pixel data
    var bytesPerSample = Math.Abs((int)bitpix) / 8;
    var sampleCount = checked((long)width * height * channels);
    var dataSize64 = checked(sampleCount * bytesPerSample);
    if (dataSize64 > int.MaxValue)
      throw new InvalidDataException("FITS image data exceeds the supported in-memory size.");

    var dataSize = (int)dataSize64;
    var availableData = Math.Min(dataSize, data.Length - headerLength);

    var pixelData = new byte[availableData > 0 ? availableData : 0];
    if (availableData > 0)
      data.Slice(headerLength, availableData).CopyTo(pixelData);

    return new FitsFile {
      Width = width,
      Height = height,
      Channels = channels,
      Bitpix = bitpix,
      Keywords = keywords,
      PixelData = pixelData
    };
  }

  public static FitsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
