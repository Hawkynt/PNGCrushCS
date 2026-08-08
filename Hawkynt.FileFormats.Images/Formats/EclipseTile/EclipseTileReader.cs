using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.EclipseTile;

/// <summary>Reads Eclipse tiled rasters from bytes, streams, or file paths.</summary>
public static class EclipseTileReader {

  public static EclipseTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Eclipse tiled raster not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EclipseTileFile FromStream(Stream stream) {
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

  public static EclipseTileFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < EclipseTileFile.HeaderSize)
      throw new InvalidDataException($"Data too small for an Eclipse tiled raster (got {data.Length} bytes).");

    if (!data[..EclipseTileFile.Magic.Length].SequenceEqual(EclipseTileFile.Magic))
      throw new InvalidDataException("Not an Eclipse tiled raster: it does not open the way one does.");

    var creator = _ReadName(data.Slice(EclipseTileFile.CreatorAt, EclipseTileFile.CreatorLength));
    if (creator != EclipseTileFile.Creator)
      throw new InvalidDataException($"Not an Eclipse tiled raster: it names \"{creator}\" where the creator belongs.");

    var width = BinaryPrimitives.ReadInt32BigEndian(data[EclipseTileFile.WidthAt..]);
    var height = BinaryPrimitives.ReadInt32BigEndian(data[EclipseTileFile.HeightAt..]);
    var colorSpace = BinaryPrimitives.ReadInt32BigEndian(data[EclipseTileFile.ColorSpaceAt..]);
    var channels = BinaryPrimitives.ReadInt32BigEndian(data[EclipseTileFile.ChannelCountAt..]);
    var paddedWidth = BinaryPrimitives.ReadInt32BigEndian(data[EclipseTileFile.PaddedWidthAt..]);
    var paddedHeight = BinaryPrimitives.ReadInt32BigEndian(data[EclipseTileFile.PaddedHeightAt..]);

    if (width < 1 || height < 1 || width > EclipseTileFile.LargestSide || height > EclipseTileFile.LargestSide)
      throw new InvalidDataException($"Invalid Eclipse tiled raster size: {width}x{height}.");

    if (channels is not (EclipseTileFile.RgbChannelCount or EclipseTileFile.CmykChannelCount))
      throw new InvalidDataException($"An Eclipse tiled raster of {channels} channels is not one this reads.");

    // The colour space and the channel count say the same thing twice, and disagreeing is the sign
    // of a header being read somewhere it does not belong.
    var expectedChannels = colorSpace == EclipseTileFile.CmykColorSpace
      ? EclipseTileFile.CmykChannelCount
      : EclipseTileFile.RgbChannelCount;
    if (colorSpace is not (EclipseTileFile.RgbColorSpace or EclipseTileFile.CmykColorSpace) || channels != expectedChannels)
      throw new InvalidDataException($"An Eclipse tiled raster states colour space {colorSpace} and {channels} channels, which do not agree.");

    // The buffer the tiles fill is the size rounded up to whole tiles, and the header states it. Both
    // being what rounding gives, and the two together accounting for the file exactly, is what says
    // the header is being read as the format means it.
    if (paddedWidth != EclipseTileFile.Padded(width) || paddedHeight != EclipseTileFile.Padded(height))
      throw new InvalidDataException(
        $"An Eclipse tiled raster of {width}x{height} fills {EclipseTileFile.Padded(width)}x{EclipseTileFile.Padded(height)} and the header states {paddedWidth}x{paddedHeight}.");

    var expected = EclipseTileFile.HeaderSize + (long)paddedWidth * paddedHeight * EclipseTileFile.BytesPerPixel;
    if (expected != data.Length)
      throw new InvalidDataException(
        $"An Eclipse tiled raster of {paddedWidth}x{paddedHeight} accounts for {expected} bytes and the file is {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      ChannelCount = channels,
      Revision = BinaryPrimitives.ReadUInt16BigEndian(data[EclipseTileFile.RevisionAt..]),
      CreatorVersion = _ReadName(data.Slice(EclipseTileFile.CreatorVersionAt, EclipseTileFile.CreatorLength)),
      HorizontalResolution = _ReadDouble(data[EclipseTileFile.HorizontalResolutionAt..]),
      VerticalResolution = _ReadDouble(data[EclipseTileFile.VerticalResolutionAt..]),
      PixelData = _Untile(data[EclipseTileFile.HeaderSize..], width, height, paddedWidth, channels),
    };
  }

  /// <summary>Puts the tiles back where they belong, the right way up, as red green and blue.</summary>
  private static byte[] _Untile(ReadOnlySpan<byte> body, int width, int height, int paddedWidth, int channels) {
    var pixels = new byte[width * height * 3];
    var tilesAcross = paddedWidth / EclipseTileFile.TileSize;

    for (var y = 0; y < height; ++y) {
      // Stored bottom-up: the first row in the file is the picture's last.
      var row = height - 1 - y;
      var tileRow = row / EclipseTileFile.TileSize;
      var withinRow = row % EclipseTileFile.TileSize;
      var destination = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var tile = tileRow * tilesAcross + x / EclipseTileFile.TileSize;
        var at = (tile * EclipseTileFile.TileSize * EclipseTileFile.TileSize
                  + withinRow * EclipseTileFile.TileSize
                  + x % EclipseTileFile.TileSize) * EclipseTileFile.BytesPerPixel;

        // A big-endian word with channel n in bits 8n upward, so the last byte is the first channel.
        var third = body[at + 1];
        var second = body[at + 2];
        var first = body[at + 3];

        var to = destination + x * 3;
        if (channels == EclipseTileFile.RgbChannelCount) {
          pixels[to] = first;
          pixels[to + 1] = second;
          pixels[to + 2] = third;
        } else {
          // The light each ink leaves, scaled by what the key plane leaves — which is how the JPEG
          // decoder here turns the same four planes into three.
          var key = 255 - body[at];
          pixels[to] = (byte)((255 - first) * key / 255);
          pixels[to + 1] = (byte)((255 - second) * key / 255);
          pixels[to + 2] = (byte)((255 - third) * key / 255);
        }
      }
    }

    return pixels;
  }

  private static double _ReadDouble(ReadOnlySpan<byte> data)
    => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data));

  private static string _ReadName(ReadOnlySpan<byte> data) {
    var end = data.IndexOf((byte)0);
    return Encoding.ASCII.GetString(end < 0 ? data : data[..end]);
  }

  public static EclipseTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
