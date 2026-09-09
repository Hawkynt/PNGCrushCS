using System;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.IffHame;

/// <summary>Reads IFF HAM-E (HAM Enhanced) images from bytes, streams, or file paths.</summary>
public static class IffHameReader {

  private const uint _LACE = 0x0004;

  public static IffHameFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HAM-E file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffHameFile FromStream(Stream stream) {
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

  public static IffHameFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IffHameFile FromSpan(ReadOnlySpan<byte> data) {
    var carrier = IlbmReader.FromSpan(data);
    if (carrier.NumPlanes != 4)
      throw new InvalidDataException($"Invalid HAM-E carrier: expected 4 bitplanes, got {carrier.NumPlanes}.");
    if ((carrier.Width & 1) != 0)
      throw new InvalidDataException("Invalid HAM-E carrier: its HIRES width must be even so two carrier pixels form one HAM-E byte.");

    var width = carrier.Width / 2;
    if (width is < IffHameFile.MinimumWidth or > IffHameFile.MaximumWidth)
      throw new InvalidDataException($"Invalid HAM-E width {width}; original HAM-E supports {IffHameFile.MinimumWidth}..{IffHameFile.MaximumWidth} logical pixels.");
    if (carrier.Height < 2)
      throw new InvalidDataException("Invalid HAM-E carrier: no displayed scanline follows the palette line.");
    if (carrier.Palette is not { Length: > 0 } carrierPalette)
      throw new InvalidDataException("Invalid HAM-E carrier: the four-plane RGBI carrier needs a CMAP.");
    if (carrier.PixelData.Length != carrier.Width * carrier.Height)
      throw new InvalidDataException("Invalid HAM-E carrier: decoded ILBM raster has the wrong size.");

    var commands = new byte[width * carrier.Height];
    try {
      for (var y = 0; y < carrier.Height; ++y) {
        var sourceRow = y * carrier.Width;
        var destinationRow = y * width;
        for (var x = 0; x < width; ++x) {
          var high = IffHameCodec.CarrierIndexToNibble(carrier.PixelData[sourceRow + x * 2], carrierPalette);
          var low = IffHameCodec.CarrierIndexToNibble(carrier.PixelData[sourceRow + x * 2 + 1], carrierPalette);
          commands[destinationRow + x] = (byte)((high << 4) | low);
        }
      }
    } catch (ArgumentException exception) {
      throw new InvalidDataException("Invalid HAM-E carrier colour map.", exception);
    }

    var paletteStorage = new byte[IffHameCodec.MaximumPaletteEntries * 3];
    var paletteLines = 0;
    for (var y = 0; y < Math.Min(4, carrier.Height); ++y) {
      var row = commands.AsSpan(y * width, width);
      if (!row.StartsWith(IffHameCodec.MagicCookie))
        break;

      var mode = row[IffHameCodec.MagicCookie.Length];
      if (mode == IffHameCodec.RegisterMode)
        throw new InvalidDataException("HAM-E register-mode pictures are not HAME hold-and-modify pictures.");
      if (mode != IffHameCodec.HoldAndModifyMode)
        throw new InvalidDataException($"Invalid HAM-E mode byte 0x{mode:X2}.");

      var paletteAt = paletteLines * IffHameCodec.PaletteEntriesPerLine * 3;
      row.Slice(IffHameCodec.MagicCookie.Length + 1, IffHameCodec.PaletteEntriesPerLine * 3)
        .CopyTo(paletteStorage.AsSpan(paletteAt));
      ++paletteLines;
    }

    if (paletteLines == 0)
      throw new InvalidDataException("Invalid HAM-E carrier: the first scanline does not contain the HAM-E magic cookie.");
    if (paletteLines >= carrier.Height)
      throw new InvalidDataException("Invalid HAM-E carrier: palette lines leave no displayed image rows.");

    var height = carrier.Height - paletteLines;
    if (height > IffHameFile.MaximumHeight)
      throw new InvalidDataException($"Invalid HAM-E height {height}; original HAM-E supports at most {IffHameFile.MaximumHeight} displayed rows.");

    var paletteCount = paletteLines * IffHameCodec.PaletteEntriesPerLine;
    return new() {
      Width = width,
      Height = height,
      Interlaced = (carrier.ViewportMode & _LACE) != 0,
      Palette = paletteStorage.AsSpan(0, paletteCount * 3).ToArray(),
      PaletteCount = paletteCount,
      PixelData = commands.AsSpan(paletteLines * width, width * height).ToArray(),
      RawData = data.ToArray(),
    };
  }
}
