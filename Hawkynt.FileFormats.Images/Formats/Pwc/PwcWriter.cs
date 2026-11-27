using System;
using System.Collections.Generic;

namespace FileFormat.Pwc;

/// <summary>Assembles PWC file bytes from a <see cref="PwcFile"/>.</summary>
/// <remarks>
/// The models run the same code in both directions, so given the same palette and options this produces the
/// bytes <c>pwcencode</c> 1.5.1 produces.
/// </remarks>
public static class PwcWriter {

  public static byte[] ToBytes(PwcFile file) {
    var width = file.Width;
    var height = file.Height;
    var depth = file.BitsPerPixel;
    if (width is < 1 or > 0xFFFF || height is < 1 or > 0xFFFF)
      throw new ArgumentException($"PWC dimensions must lie in 1..65535, not {width}x{height}.", nameof(file));

    if (depth is not (1 or 4 or 8))
      throw new ArgumentException($"PWC codes 1, 4 or 8 bits per pixel, not {depth}.", nameof(file));

    if (depth == 4 && (file.CodingFlags & PwcCodingFlags.Gray) != 0)
      throw new ArgumentException("PWC's 4-bit model has no grey mode: the reference decoder turns such a file into index zero throughout.", nameof(file));

    if (file.MaxLps is < 0 or > 255)
      throw new ArgumentException($"PWC adaptation limit must fit a byte, not {file.MaxLps}.", nameof(file));

    var pixels = file.PixelData ?? throw new ArgumentException("PWC needs pixel data.", nameof(file));
    if (pixels.Length < width * height)
      throw new ArgumentException("PWC pixel data is shorter than the picture.", nameof(file));

    var entries = file.StoresPalette ? (file.Palette?.Length ?? 0) / 3 : 1 << depth;
    if (file.StoresPalette && (entries > 256 || (depth == 4 && entries > 16)))
      throw new ArgumentException($"A {depth}-bit PWC picture cannot store a palette of {entries} entries.", nameof(file));

    var limit = 1 << depth;
    for (var i = 0; i < width * height; ++i)
      if (pixels[i] >= limit || (depth > 1 && pixels[i] >= Math.Max(entries, 1)))
        throw new ArgumentException($"Pixel index {pixels[i]} is outside what a {depth}-bit PWC picture with {entries} colours can hold.", nameof(file));

    var output = new List<byte>();
    _Add16(output, 0x7934);
    _Add16(output, 0x6156);
    _Add16(output, height);
    _Add16(output, width);
    output.Add((byte)depth);
    output.Add((byte)((file.StoresPalette ? 3 : 0) | (file.BottomUp ? 8 : 0)));
    _Add16(output, 0);

    if (file.StoresPalette) {
      _Add16(output, entries);
      for (var i = 0; i < entries; ++i) {
        output.Add(file.Palette![i * 3 + 2]);
        output.Add(file.Palette[i * 3 + 1]);
        output.Add(file.Palette[i * 3]);
      }
    }

    output.Add((byte)file.CodingFlags);
    output.Add((byte)file.MaxLps);
    output.Add(0x10);
    output.Add(0);

    var picture = pixels[..(width * height)];
    if (file.BottomUp)
      picture = PwcReader.FlipRows(picture, width, height);

    output.AddRange(PwcCodec.Encode(picture, width, height, depth, entries, file.CodingFlags, file.MaxLps));
    return [.. output];
  }

  private static void _Add16(List<byte> output, int value) {
    output.Add((byte)value);
    output.Add((byte)(value >> 8));
  }
}
