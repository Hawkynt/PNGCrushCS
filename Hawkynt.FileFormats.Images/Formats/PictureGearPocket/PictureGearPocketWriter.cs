using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PictureGearPocket;

/// <summary>Assembles Sony PictureGear Pocket databases from a <see cref="PictureGearPocketFile"/>.</summary>
/// <remarks>
/// The resources come in the order Sony's library writes them — the thumbnail's info, frame list and
/// single tile, then the picture's info, frame list, palette and tiles — with the same constants in
/// the fields nobody has found a use for, so a picture read from a file Sony's software wrote is
/// written back byte for byte.
/// </remarks>
public static class PictureGearPocketWriter {

  private const int _DatabaseHeaderSize = 78;
  private const int _ResourceEntrySize = 10;
  private const ushort _PictureId = 1100;
  private const ushort _FirstPictureTile = 1001;

  /// <summary>Resource databases set the low attribute bit.</summary>
  private const ushort _ResourceDatabase = 1;

  /// <summary>The database version Sony's library writes for an eight-bit picture, and for a sixteen-bit one.</summary>
  private const ushort _IndexedVersion = 1;
  private const ushort _HighColorVersion = 0x0110;

  public static byte[] ToBytes(PictureGearPocketFile file) {
    var (width, height, depth) = (file.Width, file.Height, file.BitsPerPixel);
    if (depth is not (8 or 16))
      throw new ArgumentException($"A PictureGear Pocket picture is eight or sixteen bits deep, not {depth}.", nameof(file));
    if (width < 1 || height < 1 || width > PictureGearPocketFile.MaximumExtent || height > PictureGearPocketFile.MaximumExtent)
      throw new ArgumentException($"A PictureGear Pocket picture is from 1x1 to {PictureGearPocketFile.MaximumExtent}x{PictureGearPocketFile.MaximumExtent}, not {width}x{height}.", nameof(file));

    var bytesPerPixel = depth / 8;
    if (file.PixelData is not { } pixels || pixels.Length < width * height * bytesPerPixel)
      throw new ArgumentException($"A {width}x{height} picture of {depth} bits needs {width * height * bytesPerPixel} bytes of pixels.", nameof(file));

    var palette = depth == 8 ? file.Palette : null;
    if (palette is { } p && (p.Length % 3 != 0 || p.Length > 256 * 3))
      throw new ArgumentException("The palette must be whole entries of red, green and blue, at most 256 of them.", nameof(file));
    if (depth == 8 && palette is { } entries) {
      var count = entries.Length / 3;
      for (var i = 0; i < width * height; ++i)
        if (pixels[i] >= count)
          throw new ArgumentException($"Pixel {i} names palette entry {pixels[i]} of {count}.", nameof(file));
    }

    var tileSize = file.TileSize is >= 1 and <= 128 ? file.TileSize : TileSizeFor(width, height);
    var across = (width + tileSize - 1) / tileSize;
    var down = (height + tileSize - 1) / tileSize;
    if (_FirstPictureTile + across * down - 1 > ushort.MaxValue)
      throw new ArgumentException($"A {width}x{height} picture in {tileSize}-pixel tiles needs tile ids past 65535.", nameof(file));

    var thumbnail = file.Thumbnail is { Length: PictureGearPocketFile.ThumbnailSize * PictureGearPocketFile.ThumbnailSize } given
      ? given
      : MakeThumbnail(PictureGearPocketFile.ToRawImage(file).EnsureFormat(PixelFormat.Rgb24).PixelData, width, height);
    var resources = new List<(string Type, ushort Id, byte[] Data)> {
      ("iINF", PictureGearPocketReader.ThumbnailId, _Info(PictureGearPocketFile.ThumbnailSize, PictureGearPocketFile.ThumbnailSize, 8,
        PictureGearPocketFile.ThumbnailSize, PictureGearPocketCodec.Uncompressed, PictureGearPocketReader.ThumbnailId, 0)),
      ("iFRI", PictureGearPocketReader.ThumbnailId, _FrameList(PictureGearPocketReader.ThumbnailId)),
      ("iTIL", PictureGearPocketReader.ThumbnailId, _Tile(thumbnail)),
      // Sony names palette 1100 even at sixteen bits, where there is none; an eight-bit picture in
      // the system palette names none at all.
      ("iINF", _PictureId, _Info(width, height, depth, tileSize,
        file.IsCompressed ? PictureGearPocketCodec.RunLengthCoded : PictureGearPocketCodec.Uncompressed, _PictureId,
        depth == 8 && palette == null ? (ushort)0 : _PictureId)),
      ("iFRI", _PictureId, _FrameList(_FirstPictureTile)),
    };

    if (palette != null)
      resources.Add(("iPLT", _PictureId, _Palette(palette)));

    // Tiles past the right and bottom edges are filled with index nought, or with white at sixteen
    // bits, which is what Sony's library leaves there.
    var tile = new byte[tileSize * tileSize * bytesPerPixel];
    for (var t = 0; t < across * down; ++t) {
      if (depth == 16)
        tile.AsSpan().Fill(0xFF);
      else
        tile.AsSpan().Clear();

      var left = t % across * tileSize;
      var top = t / across * tileSize;
      var span = Math.Min(tileSize, width - left) * bytesPerPixel;
      for (var row = 0; row < tileSize && top + row < height; ++row)
        pixels.AsSpan(((top + row) * width + left) * bytesPerPixel, span).CopyTo(tile.AsSpan(row * tileSize * bytesPerPixel));

      resources.Add(("iTIL", (ushort)(_FirstPictureTile + t), _Tile(file.IsCompressed ? PictureGearPocketCodec.Encode(tile, bytesPerPixel) : tile)));
    }

    return _Assemble(file, depth == 16 ? _HighColorVersion : _IndexedVersion, resources);
  }

  /// <summary>Sony's choice of tile: 64 pixels once both sides reach 320, 32 below that.</summary>
  internal static int TileSizeFor(int width, int height) => width >= 320 && height >= 320 ? 64 : 32;

  private static byte[] _Assemble(PictureGearPocketFile file, ushort version, List<(string Type, ushort Id, byte[] Data)> resources) {
    var dataStart = _DatabaseHeaderSize + resources.Count * _ResourceEntrySize;
    var total = dataStart;
    foreach (var resource in resources)
      total += resource.Data.Length;

    var result = new byte[total];
    var span = result.AsSpan();

    var name = Encoding.Latin1.GetBytes(file.Name ?? string.Empty);
    name.AsSpan(0, Math.Min(name.Length, 31)).CopyTo(span);
    BinaryPrimitives.WriteUInt16BigEndian(span[32..], _ResourceDatabase);
    BinaryPrimitives.WriteUInt16BigEndian(span[34..], version);
    BinaryPrimitives.WriteUInt32BigEndian(span[36..], PictureGearPocketCodec.ToPalmSeconds(file.Created));
    BinaryPrimitives.WriteUInt32BigEndian(span[40..], PictureGearPocketCodec.ToPalmSeconds(file.Modified));
    BinaryPrimitives.WriteUInt32BigEndian(span[44..], PictureGearPocketCodec.ToPalmSeconds(file.LastBackup));
    BinaryPrimitives.WriteUInt32BigEndian(span[48..], file.ModificationNumber);
    "IMVSIMVS"u8.CopyTo(span[60..]);
    BinaryPrimitives.WriteUInt32BigEndian(span[68..], file.UniqueIdSeed);
    BinaryPrimitives.WriteUInt16BigEndian(span[76..], (ushort)resources.Count);

    var offset = dataStart;
    for (var i = 0; i < resources.Count; ++i) {
      var entry = span.Slice(_DatabaseHeaderSize + i * _ResourceEntrySize, _ResourceEntrySize);
      Encoding.ASCII.GetBytes(resources[i].Type).CopyTo(entry);
      BinaryPrimitives.WriteUInt16BigEndian(entry[4..], resources[i].Id);
      BinaryPrimitives.WriteUInt32BigEndian(entry[6..], (uint)offset);
      resources[i].Data.CopyTo(span[offset..]);
      offset += resources[i].Data.Length;
    }

    return result;
  }

  /// <summary>Width, height, nought, depth, nought, tile size, compression, frame list id, palette id.</summary>
  private static byte[] _Info(int width, int height, int depth, int tileSize, ushort compression, ushort frameId, ushort paletteId) {
    var info = new byte[18];
    BinaryPrimitives.WriteUInt16BigEndian(info, (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(2), (ushort)height);
    BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(6), (ushort)depth);
    BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(10), (ushort)tileSize);
    BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(12), compression);
    BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(14), frameId);
    BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(16), paletteId);
    return info;
  }

  /// <summary>One frame, then the 1 and 0 Sony writes and nothing reads, then the frame's first tile.</summary>
  private static byte[] _FrameList(ushort firstTile) {
    var list = new byte[8];
    BinaryPrimitives.WriteUInt16BigEndian(list, 1);
    BinaryPrimitives.WriteUInt16BigEndian(list.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(list.AsSpan(6), firstTile);
    return list;
  }

  /// <summary>A count, then each entry as a nought and red, green and blue.</summary>
  private static byte[] _Palette(byte[] rgb) {
    var count = rgb.Length / 3;
    var table = new byte[2 + count * 4];
    BinaryPrimitives.WriteUInt16BigEndian(table, (ushort)count);
    for (var i = 0; i < count; ++i)
      rgb.AsSpan(i * 3, 3).CopyTo(table.AsSpan(2 + i * 4 + 1));

    return table;
  }

  private static byte[] _Tile(ReadOnlySpan<byte> body) {
    if (body.Length > ushort.MaxValue)
      throw new InvalidDataException($"A tile of {body.Length} bytes does not fit its sixteen-bit length.");

    var tile = new byte[2 + body.Length];
    BinaryPrimitives.WriteUInt16BigEndian(tile, (ushort)body.Length);
    body.CopyTo(tile.AsSpan(2));
    return tile;
  }

  /// <summary>Averages the picture down to 44x44 and error-diffuses it onto the Palm system palette.</summary>
  /// <remarks>
  /// Sony's library does the same thing in outline — an average over each cell, then Floyd–Steinberg
  /// onto the 231 system colours — and the handheld only ever shows the result as an icon, so this
  /// one is not held to Sony's arithmetic.
  /// </remarks>
  internal static byte[] MakeThumbnail(byte[] rgb, int width, int height) {
    const int size = PictureGearPocketFile.ThumbnailSize;
    var cells = new int[size * size * 3];
    for (var cy = 0; cy < size; ++cy) {
      var y0 = cy * height / size;
      var y1 = Math.Max((cy + 1) * height / size, y0 + 1);
      for (var cx = 0; cx < size; ++cx) {
        var x0 = cx * width / size;
        var x1 = Math.Max((cx + 1) * width / size, x0 + 1);
        long r = 0, g = 0, b = 0;
        for (var y = y0; y < y1; ++y)
          for (var x = x0; x < x1; ++x) {
            var at = (y * width + x) * 3;
            r += rgb[at];
            g += rgb[at + 1];
            b += rgb[at + 2];
          }

        var n = (y1 - y0) * (x1 - x0);
        var cell = (cy * size + cx) * 3;
        cells[cell] = (int)(r / n);
        cells[cell + 1] = (int)(g / n);
        cells[cell + 2] = (int)(b / n);
      }
    }

    var system = PictureGearPocketCodec.SystemPaletteRgb();
    var colors = PictureGearPocketCodec.SystemPaletteCount;
    var result = new byte[size * size];
    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        var cell = (y * size + x) * 3;
        int r = Math.Clamp(cells[cell], 0, 255), g = Math.Clamp(cells[cell + 1], 0, 255), b = Math.Clamp(cells[cell + 2], 0, 255);
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < colors && bestDistance > 0; ++i) {
          int dr = r - system[i * 3], dg = g - system[i * 3 + 1], db = b - system[i * 3 + 2];
          var distance = dr * dr + dg * dg + db * db;
          if (distance < bestDistance) {
            bestDistance = distance;
            best = i;
          }
        }

        result[y * size + x] = (byte)best;
        _Spread(cells, x, y, size, r - system[best * 3], g - system[best * 3 + 1], b - system[best * 3 + 2]);
      }

    return result;
  }

  private static void _Spread(int[] cells, int x, int y, int size, int er, int eg, int eb) {
    void Add(int dx, int dy, int weight) {
      int nx = x + dx, ny = y + dy;
      if (nx < 0 || nx >= size || ny >= size)
        return;

      var at = (ny * size + nx) * 3;
      cells[at] += er * weight / 16;
      cells[at + 1] += eg * weight / 16;
      cells[at + 2] += eb * weight / 16;
    }

    Add(1, 0, 7);
    Add(-1, 1, 3);
    Add(0, 1, 5);
    Add(1, 1, 1);
  }
}
