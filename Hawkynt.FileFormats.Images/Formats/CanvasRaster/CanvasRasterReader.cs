using System;
using System.IO;

namespace FileFormat.CanvasRaster;

/// <summary>Reads Canvas raster pictures from bytes, streams, or file paths.</summary>
public static class CanvasRasterReader {

  /// <summary>The run count that ends the list.</summary>
  private const int _END = 65535;

  public static CanvasRasterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CanvasRasterFile FromStream(Stream stream) {
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

  public static CanvasRasterFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 1544 || !CanvasRasterFile._HasPalette(data, 0))
      throw new InvalidDataException("Not a Canvas raster picture.");

    // The palettes are written backwards, so where they end is fixed and where they begin depends
    // on how many bands have one.
    var cursor = CanvasRasterFile.PaletteEnd;
    for (var band = 1; band < CanvasRasterFile.BandCount; ++band) {
      if (CanvasRasterFile._HasPalette(data, band))
        cursor += CanvasRasterFile.PaletteSize;
    }

    if (cursor > data.Length)
      throw new InvalidDataException("A Canvas picture's palettes run past the end of the file.");

    var at = cursor + CanvasRasterFile.HeaderGap;
    if (at + 40 > data.Length || data[at + 32] != 0)
      throw new InvalidDataException("A Canvas picture has no screen header.");

    // The screen header allows a third mode but Canvas never wrote one, so a file claiming it is
    // some other program's picture that happens to have got this far.
    var mode = data[at + 33];
    if (mode > 1)
      throw new InvalidDataException($"Screen mode {mode} is not one Canvas wrote.");

    var bitplanes = 4 >> mode;
    var bitmap = new byte[32000];
    var filled = new bool[CanvasRasterFile.GroupCount];
    at += 34;

    // The runs: each names where it starts, how many groups follow it, and the group to repeat.
    for (;;) {
      var next = at + 4 + bitplanes * 2;
      if (next > data.Length)
        throw new InvalidDataException("A Canvas picture's runs end before the list does.");

      var count = (data[at] << 8) | data[at + 1];
      if (count == _END) {
        at = next;
        break;
      }

      var group = ((data[at + 2] << 8) | data[at + 3]) * bitplanes;

      // The count is one less than the number of groups, so a count of zero still fills one.
      do {
        if (group >= CanvasRasterFile.GroupCount)
          throw new InvalidDataException("A Canvas run reaches past the end of the screen.");

        data.Slice(at + 4, bitplanes * 2).CopyTo(bitmap.AsSpan(group * 2));
        filled[group] = true;
        group += bitplanes;
      } while (--count >= 0);

      at = next;
    }

    // Then everything the runs did not touch, in scan order.
    for (var group = 0; group < CanvasRasterFile.GroupCount; group += bitplanes) {
      if (filled[group])
        continue;

      var next = at + bitplanes * 2;
      if (next > data.Length)
        throw new InvalidDataException("A Canvas picture ends before its screen is filled.");

      data.Slice(at, bitplanes * 2).CopyTo(bitmap.AsSpan(group * 2));
      at = next;
    }

    return new() {
      Data = data.ToArray(),
      Bitmap = bitmap,
      PaletteCursor = cursor,
      Bitplanes = bitplanes,
      Mode = mode,
    };
  }

  public static CanvasRasterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
