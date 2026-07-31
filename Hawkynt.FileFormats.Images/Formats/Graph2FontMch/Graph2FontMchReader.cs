using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Graph2FontMch;

/// <summary>Reads Graph2Font MCH pictures from bytes, streams, or file paths.</summary>
public static class Graph2FontMchReader {

  public static Graph2FontMchFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Graph2FontMchFile FromStream(Stream stream) {
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

  public static Graph2FontMchFile FromSpan(ReadOnlySpan<byte> data) {
    // The width and whether sprites are present both follow from the length alone.
    var columns = data.Length switch {
      9840 or 28673 => 32,
      12000 or 30833 => 40,
      14160 or 32993 => 48,
      _ => throw new InvalidDataException($"Not a Graph2Font MCH picture: {data.Length} bytes."),
    };

    var bitmapLength = columns * Graph2FontMchFile.BytesPerCell * Graph2FontMchFile.CellRows;
    var sprites = data.Length > bitmapLength + 1200;

    var mode = (data[0] & 3) switch {
      0 or 1 => AnticMode.FiveColor,
      2 => AnticMode.FourColor,
      _ => throw new InvalidDataException("A Graph2Font MCH picture names no character mode."),
    };

    // Two of the three character modes also carry a raster program, and a picture using it is not
    // a picture at all but an animation — the display changes between frames, so there is no single
    // image to produce. Only the mode that cannot carry one is unconditionally readable.
    if (sprites && (data[0] & 3) != 1 && _HasRaster(data, bitmapLength + 6080))
      throw new InvalidDataException("A Graph2Font MCH picture with a raster program has no single frame.");

    // The upper bits override that with a high-resolution mode and a GTIA one together.
    var gtiaMode = 0;
    switch (data[0] & 60) {
      case 0:
        mode = AnticMode.HiRes;
        break;

      case 4:
        break;

      case 8:
        mode = AnticMode.HiRes;
        gtiaMode = 64;
        break;

      case 24:
        mode = AnticMode.HiRes;
        gtiaMode = 128;
        break;

      case 40:
        mode = AnticMode.HiRes;
        gtiaMode = 192;
        break;

      default:
        throw new InvalidDataException($"A Graph2Font MCH picture names no display mode: {data[0] & 60}.");
    }

    return new() {
      Data = data.ToArray(),
      Columns = columns,
      Mode = mode,
      GtiaMode = gtiaMode,
      HasSprites = sprites,
    };
  }

  /// <summary>
  /// Whether the raster block does anything, rather than being the do-nothing program a still
  /// picture carries.
  /// </summary>
  /// <remarks>
  /// The block is a run of two-byte instructions. A handful are harmless — waits and the write that
  /// clears the collision registers, which every frame does — and anything else changes the display
  /// as it is drawn.
  /// </remarks>
  private static bool _HasRaster(ReadOnlySpan<byte> data, int offset) {
    const byte collisionClear = 30;

    for (var i = 0; i < 6960; ++i, offset += 2) {
      if (offset + 1 >= data.Length)
        return true;

      switch (data[offset]) {
        case 0 or 1 or 2 or 3 or 65 or 66 or 67 or 97 or 98 or 99:
          break;

        case 129 or 130 or 131:
          if (data[offset + 1] != collisionClear)
            return true;

          break;

        default:
          return true;
      }
    }

    return false;
  }

  public static Graph2FontMchFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
