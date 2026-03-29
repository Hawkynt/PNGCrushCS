using System;
using System.IO;

namespace FileFormat.Pfm;

/// <summary>Reads PFM files from bytes, streams, or file paths.</summary>
public static class PfmReader {

  public static PfmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PFM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PfmFile FromStream(Stream stream) {
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

  public static PfmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PfmHeaderParser.MinHeaderSize)
      throw new InvalidDataException("Data too small for a valid PFM file.");

    var header = PfmHeaderParser.Parse(data);
    var channelsPerPixel = header.ColorMode == PfmColorMode.Rgb ? 3 : 1;
    var totalFloats = header.Width * header.Height * channelsPerPixel;
    var expectedDataBytes = totalFloats * 4;

    if (data.Length - header.DataOffset < expectedDataBytes)
      throw new InvalidDataException("Data too small for the declared image dimensions.");

    var pixelData = new float[totalFloats];
    var needSwap = header.IsLittleEndian != BitConverter.IsLittleEndian;

    // Read float data — rows are stored bottom-to-top in PFM, we convert to top-to-bottom
    var floatsPerRow = header.Width * channelsPerPixel;
    Span<byte> swapBuf = stackalloc byte[4];
    for (var row = 0; row < header.Height; ++row) {
      var srcRow = header.Height - 1 - row; // bottom-to-top -> top-to-bottom
      var srcOffset = header.DataOffset + srcRow * floatsPerRow * 4;
      var dstOffset = row * floatsPerRow;

      for (var i = 0; i < floatsPerRow; ++i) {
        var byteOffset = srcOffset + i * 4;
        if (needSwap) {
          swapBuf[0] = data[byteOffset + 3];
          swapBuf[1] = data[byteOffset + 2];
          swapBuf[2] = data[byteOffset + 1];
          swapBuf[3] = data[byteOffset];
          pixelData[dstOffset + i] = BitConverter.ToSingle(swapBuf);
        } else
          pixelData[dstOffset + i] = BitConverter.ToSingle(data, byteOffset);
      }
    }

    return new PfmFile {
      Width = header.Width,
      Height = header.Height,
      ColorMode = header.ColorMode,
      Scale = header.Scale,
      IsLittleEndian = header.IsLittleEndian,
      PixelData = pixelData
    };
  }
}
