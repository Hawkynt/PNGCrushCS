using System;
using System.IO;

namespace FileFormat.Phm;

/// <summary>Reads PHM (Portable Half Map) files from bytes, streams, or file paths.</summary>
public static class PhmReader {

  public static PhmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PHM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PhmFile FromStream(Stream stream) {
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

  public static PhmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PhmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PhmHeaderParser.MinHeaderSize)
      throw new InvalidDataException("Data too small for a valid PHM file.");

    var header = PhmHeaderParser.Parse(data.ToArray());
    var channelsPerPixel = header.ColorMode == PhmColorMode.Rgb ? 3 : 1;
    var totalHalves = header.Width * header.Height * channelsPerPixel;
    var expectedDataBytes = totalHalves * 2;

    if (data.Length - header.DataOffset < expectedDataBytes)
      throw new InvalidDataException("Data too small for the declared image dimensions.");

    var pixelData = new Half[totalHalves];
    var needSwap = header.IsLittleEndian != BitConverter.IsLittleEndian;

    var halvesPerRow = header.Width * channelsPerPixel;
    for (var row = 0; row < header.Height; ++row) {
      var srcRow = header.Height - 1 - row;
      var srcOffset = header.DataOffset + srcRow * halvesPerRow * 2;
      var dstOffset = row * halvesPerRow;

      for (var i = 0; i < halvesPerRow; ++i) {
        var byteOffset = srcOffset + i * 2;
        byte b0 = data[byteOffset], b1 = data[byteOffset + 1];
        if (needSwap)
          (b0, b1) = (b1, b0);

        pixelData[dstOffset + i] = BitConverter.ToHalf(new[] { b0, b1 });
      }
    }

    return new() {
      Width = header.Width,
      Height = header.Height,
      ColorMode = header.ColorMode,
      Scale = header.Scale,
      IsLittleEndian = header.IsLittleEndian,
      PixelData = pixelData,
    };
  }
}
