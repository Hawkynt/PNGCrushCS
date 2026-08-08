using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.EclipseTile;

/// <summary>Assembles an Eclipse tiled raster: the header, then the picture in tiles.</summary>
/// <remarks>
/// Three channels only. Turning a picture into the four the CMYK files carry is a colour separation
/// with black generation, not a formula — the RGB and CMYK versions of the one picture that exists
/// as both agree on only a seventh of their pixels exactly — so writing one would be inventing a
/// separation rather than recording what was read.
/// </remarks>
public static class EclipseTileWriter {

  public static byte[] ToBytes(EclipseTileFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException($"Invalid Eclipse tiled raster size: {width}x{height}.", nameof(file));

    if (file.ChannelCount == EclipseTileFile.CmykChannelCount)
      throw new NotSupportedException("An Eclipse tiled raster of four channels is a colour separation this cannot make.");

    var pixels = file.PixelData ?? new byte[width * height * 3];
    var paddedWidth = EclipseTileFile.Padded(width);
    var paddedHeight = EclipseTileFile.Padded(height);

    var result = new byte[EclipseTileFile.HeaderSize + paddedWidth * paddedHeight * EclipseTileFile.BytesPerPixel];

    EclipseTileFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(EclipseTileFile.RevisionAt), (ushort)file.Revision);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(EclipseTileFile.WidthAt), width);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(EclipseTileFile.HeightAt), height);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(EclipseTileFile.ColorSpaceAt), EclipseTileFile.RgbColorSpace);
    Encoding.ASCII.GetBytes(EclipseTileFile.Creator).CopyTo(result, EclipseTileFile.CreatorAt);
    Encoding.ASCII.GetBytes(file.CreatorVersion ?? "1.0").CopyTo(result, EclipseTileFile.CreatorVersionAt);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(EclipseTileFile.ChannelCountAt), EclipseTileFile.RgbChannelCount);
    _WriteDouble(result.AsSpan(EclipseTileFile.HorizontalResolutionAt), file.HorizontalResolution);
    _WriteDouble(result.AsSpan(EclipseTileFile.VerticalResolutionAt), file.VerticalResolution);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(EclipseTileFile.PaddedWidthAt), paddedWidth);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(EclipseTileFile.PaddedHeightAt), paddedHeight);

    var body = result.AsSpan(EclipseTileFile.HeaderSize);
    var tilesAcross = paddedWidth / EclipseTileFile.TileSize;

    for (var y = 0; y < height; ++y) {
      var row = height - 1 - y;
      var tileRow = row / EclipseTileFile.TileSize;
      var withinRow = row % EclipseTileFile.TileSize;
      var source = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var tile = tileRow * tilesAcross + x / EclipseTileFile.TileSize;
        var at = (tile * EclipseTileFile.TileSize * EclipseTileFile.TileSize
                  + withinRow * EclipseTileFile.TileSize
                  + x % EclipseTileFile.TileSize) * EclipseTileFile.BytesPerPixel;

        var from = source + x * 3;
        body[at + 1] = pixels[from + 2];
        body[at + 2] = pixels[from + 1];
        body[at + 3] = pixels[from];
      }
    }

    return result;
  }

  private static void _WriteDouble(Span<byte> data, double value)
    => BinaryPrimitives.WriteInt64BigEndian(data, BitConverter.DoubleToInt64Bits(value <= 0 ? 11.811023622047244 : value));
}
