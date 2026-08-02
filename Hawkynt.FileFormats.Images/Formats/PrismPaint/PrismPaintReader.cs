using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PrismPaint;

/// <summary>Reads Atari Falcon Prism Paint images from bytes, streams, or file paths.</summary>
public static class PrismPaintReader {

  public static PrismPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Prism Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrismPaintFile FromStream(Stream stream) {
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

  public static PrismPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PrismPaintFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid Prism Paint file (minimum {PrismPaintFile.MinFileSize} bytes, got {data.Length}).");

    if (!data[..PrismPaintFile.Signature.Length].SequenceEqual(PrismPaintFile.Signature))
      throw new InvalidDataException("Not a Prism Paint picture: it does not begin with PNT.");

    // The size is two big-endian words a little way in, with the plane count after them. Reading the
    // first four bytes instead read the signature, which is 20048 by 84 taken as two words.
    var width = (data[PrismPaintFile.WidthOffset] << 8) | data[PrismPaintFile.WidthOffset + 1];
    var height = (data[PrismPaintFile.HeightOffset] << 8) | data[PrismPaintFile.HeightOffset + 1];
    var planes = (data[PrismPaintFile.PlanesOffset] << 8) | data[PrismPaintFile.PlanesOffset + 1];

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid Prism Paint dimensions: {width}x{height}.");

    if (planes is < 1 or > 8)
      throw new InvalidDataException($"A Prism Paint picture has one to eight bitplanes, not {planes}.");

    // Three words an entry on the VDI's nought-to-a-thousand scale, not the Falcon's packed bytes.
    var colors = 1 << planes;
    var rgbPalette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      var at = PrismPaintFile.PaletteOffset + i * PrismPaintFile.PaletteEntryBytes;
      if (at + 5 >= data.Length)
        break;

      // The entries are in the VDI's order, which is not the order the pixels index. Leaving them
      // where they lie draws the picture in the right colours put on the wrong shapes: the sample's
      // white outline came out purple while both palettes held exactly the same sixteen colours.
      var slot = AtariStGraphics.VdiToHardwareIndex(i, planes) * 3;
      for (var channel = 0; channel < 3; ++channel) {
        var value = (data[at + channel * 2] << 8) | data[at + channel * 2 + 1];
        rgbPalette[slot + channel] = (byte)(value * 255 / PrismPaintFile.PaletteChannelMaximum);
      }
    }

    var pixelOffset = PrismPaintFile.PaletteOffset + colors * PrismPaintFile.PaletteEntryBytes;
    var screenBytes = (width + 15) / 16 * 2 * planes * height;
    if (pixelOffset + screenBytes > data.Length)
      throw new InvalidDataException($"A Prism Paint picture of {width}x{height} in {planes} planes needs {screenBytes} bytes; the file holds {data.Length - pixelOffset}.");

    var chunky = PlanarConverter.AtariStToChunky(data.Slice(pixelOffset, screenBytes).ToArray(), width, height, planes);

    return new PrismPaintFile {
      Width = width,
      Height = height,
      Palette = rgbPalette,
      PixelData = chunky,
    };
    }

  /// <summary>
  /// Reads a picture from a byte array.
  /// </summary>
  /// <remarks>
  /// This used to be a second copy of the whole parse rather than a call into the first, and the two
  /// drifted exactly as such pairs do: a correction to one left the other reading the signature as
  /// the picture's size.
  /// </remarks>
  public static PrismPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
