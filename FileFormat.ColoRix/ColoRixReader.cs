using System;
using System.IO;

namespace FileFormat.ColoRix;

/// <summary>Reads ColoRIX VGA paint files from bytes, streams, or file paths.</summary>
public static class ColoRixReader {

  private static readonly byte[] _MAGIC = [(byte)'R', (byte)'I', (byte)'X', (byte)'3'];

  public static ColoRixFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ColoRIX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ColoRixFile FromStream(Stream stream) {
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

  public static ColoRixFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ColoRixFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid ColoRIX file.");

    if (data[0] != _MAGIC[0] || data[1] != _MAGIC[1] || data[2] != _MAGIC[2] || data[3] != _MAGIC[3])
      throw new InvalidDataException("Invalid ColoRIX magic bytes (expected 'RIX3').");

    var storedWidth = (ushort)(data[4] | (data[5] << 8));
    var storedHeight = (ushort)(data[6] | (data[7] << 8));
    var width = storedWidth + 1;
    var height = storedHeight + 1;

    var paletteType = data[8];
    var storageType = (ColoRixCompression)data[9];

    var offset = ColoRixFile.HeaderSize;

    var palette = new byte[ColoRixFile.PaletteSize];
    if (paletteType == ColoRixFile.VgaPaletteType) {
      if (data.Length < offset + ColoRixFile.PaletteSize)
        throw new InvalidDataException("Data too small: palette extends beyond file.");

      data.AsSpan(offset, ColoRixFile.PaletteSize).CopyTo(palette.AsSpan(0));
      offset += ColoRixFile.PaletteSize;
    }

    var pixelCount = width * height;
    byte[] pixelData;

    if (storageType == ColoRixCompression.Rle)
      pixelData = _DecompressRle(data, offset, pixelCount);
    else {
      pixelData = new byte[pixelCount];
      var available = Math.Min(data.Length - offset, pixelCount);
      data.AsSpan(offset, available).CopyTo(pixelData.AsSpan(0));
    }

    return new ColoRixFile {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData,
      StorageType = storageType,
    };
  }

  private static byte[] _DecompressRle(byte[] data, int offset, int expectedSize) {
    var output = new byte[expectedSize];
    var inIdx = offset;
    var outIdx = 0;

    while (inIdx < data.Length && outIdx < expectedSize) {
      if (inIdx + 1 >= data.Length)
        break;

      var count = (int)data[inIdx++];
      var value = data[inIdx++];

      if (count == 0)
        count = 1;

      for (var i = 0; i < count && outIdx < expectedSize; ++i)
        output[outIdx++] = value;
    }

    return output;
  }
}
