using System;
using System.IO;

namespace FileFormat.NewsRoom;

/// <summary>Reads NewsRoom panels from bytes, streams, or file paths.</summary>
public static class NewsRoomReader {

  public static NewsRoomFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NewsRoom file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NewsRoomFile FromStream(Stream stream) {
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

  public static NewsRoomFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < NewsRoomFile.HeaderSize)
      throw new InvalidDataException(
        $"Data too small for a NewsRoom panel (minimum {NewsRoomFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..NewsRoomFile.Signature.Length].SequenceEqual(NewsRoomFile.Signature))
      throw new InvalidDataException("Not a NewsRoom panel: it does not open with 00 A0.");

    if (data[NewsRoomFile.LowMarkerOffset] != 0x00 || data[NewsRoomFile.HighMarkerOffset] != 0xFF)
      throw new InvalidDataException(
        "Not a NewsRoom panel: the two bytes closing its header are "
        + $"{data[NewsRoomFile.LowMarkerOffset]:X2} {data[NewsRoomFile.HighMarkerOffset]:X2} rather than 00 FF.");

    var height = data[NewsRoomFile.HeightPairOffset + 1] - data[NewsRoomFile.HeightPairOffset];
    var width = data[NewsRoomFile.WidthPairOffset + 1] - data[NewsRoomFile.WidthPairOffset] + 1;
    if (width < 1 || height < 1)
      throw new InvalidDataException($"A NewsRoom panel states a picture of {width}x{height}.");

    // Both sizes come out of a pair of coordinates and neither has to land on a byte, so both are
    // rounded up to a multiple of eight — which is what XnView does with them.
    width = (width + 7) / 8 * 8;
    height = (height + 7) / 8 * 8;

    var stride = NewsRoomFile.StrideOf(width);
    var needed = (long)stride * height;
    if (data.Length - NewsRoomFile.HeaderSize < needed)
      throw new InvalidDataException(
        $"A NewsRoom panel of {width}x{height} needs {needed} bytes of bits and the file holds "
        + $"{data.Length - NewsRoomFile.HeaderSize}.");

    var pixels = new byte[needed];
    data.Slice(NewsRoomFile.HeaderSize, (int)needed).CopyTo(pixels);

    return new() { Width = width, Height = height, PixelData = pixels };
  }

  public static NewsRoomFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
