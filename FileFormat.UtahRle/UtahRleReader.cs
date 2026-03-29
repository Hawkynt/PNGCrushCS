using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.UtahRle;

/// <summary>Reads Utah RLE files from bytes, streams, or file paths.</summary>
public static class UtahRleReader {

  public static UtahRleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Utah RLE file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static UtahRleFile FromStream(Stream stream) {
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

  public static UtahRleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < UtahRleHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Utah RLE file.");

    var span = data.AsSpan();
    var header = UtahRleHeader.ReadFrom(span);

    if (header.Magic != UtahRleHeader.MagicValue)
      throw new InvalidDataException("Invalid Utah RLE magic number.");

    var width = header.XSize;
    var height = header.YSize;
    var numChannels = header.NumChannels;
    var flags = header.Flags;

    var offset = UtahRleHeader.StructSize;

    // Read background color if present (flag bit 1 = no background)
    byte[]? background = null;
    if ((flags & 0x02) == 0) {
      background = new byte[numChannels];
      for (var i = 0; i < numChannels && offset < data.Length; ++i)
        background[i] = data[offset++];
    }

    // Skip color map if present
    if (header.NumColorMapChannels > 0 && offset + 2 <= data.Length) {
      var mapLength = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset));
      offset += 2;
      offset += mapLength * header.NumColorMapChannels;
    }

    // Decode scanline data
    var scanlineData = data.AsSpan(offset);
    var pixelData = UtahRleDecoder.Decode(scanlineData, width, height, numChannels, background);

    return new UtahRleFile {
      XPos = header.XPos,
      YPos = header.YPos,
      Width = width,
      Height = height,
      NumChannels = numChannels,
      PixelData = pixelData,
      BackgroundColor = background
    };
  }
}
