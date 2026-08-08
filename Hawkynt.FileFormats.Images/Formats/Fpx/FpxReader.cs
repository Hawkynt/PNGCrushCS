using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Jpeg;

namespace FileFormat.Fpx;

/// <summary>Reads FlashPix pictures, and the Picture It! and PhotoDraw documents that are made of them.</summary>
/// <remarks>
/// A FlashPix picture is a compound file holding a pyramid: the same photograph at half the size
/// again and again, each level cut into 64-pixel tiles, each tile a JPEG whose quantisation and
/// Huffman tables are kept once for the whole picture in the Image Contents property set rather than
/// repeated in every tile.
/// <para/>
/// Two things are easy to get wrong and both were checked against a file. The tile offsets are
/// stated from behind a short preamble on the data stream rather than from its start, so a reader
/// that takes them literally lands twenty-eight bytes early and reads the tail of the tile before.
/// And the tiles carry four components, not three: luma, two chromas and an opacity. A decoder that
/// sees four components and takes them for ink — which is what a JPEG without an Adobe segment
/// otherwise means — returns the photograph in colours it never had.
/// </remarks>
public static class FpxReader {

  /// <summary>Tiles are square and this is the side the format uses.</summary>
  private const int _TileSide = 64;

  /// <summary>The fixed part of a subimage header, in front of the tile table.</summary>
  private const int _SubimageHeaderSize = 64;

  /// <summary>One tile table entry: an offset, a length, and two words describing the coding.</summary>
  private const int _TileEntrySize = 16;

  private const int _SubimageWidth = 32, _SubimageHeight = 36, _SubimageTiles = 40, _SubimageTileSide = 44;

  /// <summary>The compression type of a tile that holds a JPEG.</summary>
  private const int _TileJpeg = 2;

  /// <summary>The compression type of a tile that is a single colour.</summary>
  private const int _TileSingleColour = 1;

  /// <summary>The compression type of a tile stored as it is.</summary>
  private const int _TileUncompressed = 0;

  /// <summary>A property holding a length and that many bytes.</summary>
  private const int _TypeBlob = 65;

  /// <summary>The property identifier class the JPEG table sets live in.</summary>
  private const int _JpegTablesClass = 0x03;

  private const string _ContentsName = "Image Contents";
  private const string _HeaderName = "Subimage 0000 Header";

  /// <summary>The last part of a stream's path, with any leading control characters taken off.</summary>
  private static string _LastPart(string path) {
    var name = path[(path.LastIndexOf('/') + 1)..];
    var at = 0;
    while (at < name.Length && name[at] < ' ')
      ++at;

    return name[at..];
  }

  public static FpxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FlashPix file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FpxFile FromStream(Stream stream) {
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

  public static FpxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static FpxFile FromSpan(ReadOnlySpan<byte> data) {

    if (!CompoundFile.HasSignature(data))
      throw new InvalidDataException(
        "Not a FlashPix picture: it does not open with the compound file signature. What used to be "
        + "read here was a four-byte \"FPX\\0\" header with raw pixels behind it, which is a structure "
        + "FlashPix has never had.");

    var container = new CompoundFile(data);
    var streams = container.Streams().ToArray();

    // A property set's name opens with a control character — 0x05 for the ones the format defines —
    // so the last part of the path is compared with that taken off rather than literally.
    var stores = streams
      .Where(entry => _LastPart(entry.Key) == _ContentsName && entry.Value.Type == CompoundFile.EntryStream)
      .ToArray();

    if (stores.Length == 0)
      throw new InvalidDataException("Not a FlashPix picture: the compound file holds no Image Contents stream.");

    // A FlashPix picture holds one object. A Picture It! or PhotoDraw document holds several, laid
    // out by transforms this does not apply, so what is returned is the largest of them: the page or
    // the photograph rather than a piece of trim. That is a choice and it is stated rather than
    // hidden, but the alternative — the first in the directory — has no meaning at all.
    KeyValuePair<string, CompoundFile.Entry> best = default;
    KeyValuePair<string, CompoundFile.Entry> bestHeader = default;
    var bestPixels = -1L;

    foreach (var contents in stores) {
      var store = contents.Key[..contents.Key.LastIndexOf('/')];

      // The pyramid names its levels in order and the last is the whole picture. Taking any other is
      // returning a thumbnail as though it were the photograph.
      var header = streams
        .Where(entry => entry.Key.StartsWith(store + "/Resolution ", StringComparison.Ordinal)
                        && _LastPart(entry.Key) == _HeaderName
                        && entry.Value.Type == CompoundFile.EntryStream)
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .LastOrDefault();

      if (header.Value.Type != CompoundFile.EntryStream)
        continue;

      var bytes = container.Read(header.Value);
      if (bytes.Length < _SubimageHeaderSize)
        continue;

      var pixels = (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(_SubimageWidth))
                   * BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(_SubimageHeight));

      if (pixels <= bestPixels)
        continue;

      bestPixels = pixels;
      best = contents;
      bestHeader = header;
    }

    if (bestPixels < 0)
      throw new InvalidDataException("Not a FlashPix picture: no Image Contents in it has a subimage behind it.");

    var dataName = bestHeader.Key[..^"Header".Length] + "Data";
    var tiles = streams.FirstOrDefault(entry => entry.Key == dataName);
    if (tiles.Value.Type != CompoundFile.EntryStream)
      throw new InvalidDataException($"FlashPix subimage {bestHeader.Key} has no data stream beside it.");

    return _ReadSubimage(
      container.Read(bestHeader.Value), container.Read(tiles.Value), _ReadJpegTableSets(container.Read(best.Value)));
  }

  /// <summary>Pulls out the table-only JPEG streams the tiles borrow their tables from.</summary>
  private static Dictionary<int, byte[]> _ReadJpegTableSets(byte[] contents) {

    if (contents.Length < 48)
      throw new InvalidDataException(
        $"FlashPix Image Contents is {contents.Length} bytes, too short to hold a property set.");

    var sectionAt = (int)BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(44));
    if (sectionAt < 0 || sectionAt + 8 > contents.Length)
      throw new InvalidDataException(
        $"FlashPix Image Contents puts its section at {sectionAt} of {contents.Length} bytes.");

    var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(sectionAt + 4));
    if (count < 0 || sectionAt + 8 + (long)count * 8 > contents.Length)
      throw new InvalidDataException(
        $"FlashPix Image Contents states {count} properties, which do not fit in its {contents.Length} bytes.");

    var sets = new Dictionary<int, byte[]>();
    for (var i = 0; i < count; ++i) {
      var identifier = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(sectionAt + 8 + i * 8));
      var at = sectionAt + (int)BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(sectionAt + 12 + i * 8));
      if (at < 0 || at + 8 > contents.Length)
        continue;

      if (identifier >> 24 != _JpegTablesClass
          || BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at)) != _TypeBlob)
        continue;

      var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at + 4));
      if (length < 4 || at + 8 + length > contents.Length)
        throw new InvalidDataException(
          $"FlashPix table set 0x{identifier:X8} states {length} bytes, which reach past the Image Contents stream.");

      var blob = contents.AsSpan(at + 8, length);
      if (blob[0] != 0xFF || blob[1] != 0xD8)
        continue;

      // The set is a JPEG with nothing in it but tables, so what a tile needs is everything between
      // its start marker and its end marker.
      sets[(int)((identifier >> 16) & 0xFF)] = blob[2..^2].ToArray();
    }

    return sets;
  }

  private static FpxFile _ReadSubimage(byte[] header, byte[] tiles, Dictionary<int, byte[]> tables) {

    if (header.Length < _SubimageHeaderSize)
      throw new InvalidDataException(
        $"FlashPix subimage header is {header.Length} bytes where its fixed part takes {_SubimageHeaderSize}.");

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageWidth));
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageHeight));
    var tileCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageTiles));
    var tileSide = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageTileSide));

    if (width is <= 0 or > 1 << 16 || height is <= 0 or > 1 << 16)
      throw new InvalidDataException(
        $"FlashPix subimage is stated as {width} by {height}, which is not a picture size.");

    if (tileSide != _TileSide)
      throw new InvalidDataException(
        $"FlashPix subimage states {tileSide}-pixel tiles; only {_TileSide} has been checked against a file.");

    var across = (width + tileSide - 1) / tileSide;
    var down = (height + tileSide - 1) / tileSide;
    if (tileCount != across * down)
      throw new InvalidDataException(
        $"FlashPix subimage states {tileCount} tiles where {width} by {height} in {tileSide}-pixel tiles "
        + $"needs {across * down}.");

    if (header.Length != _SubimageHeaderSize + tileCount * _TileEntrySize)
      throw new InvalidDataException(
        $"FlashPix subimage header is {header.Length} bytes where {tileCount} tiles need "
        + $"{_SubimageHeaderSize + tileCount * _TileEntrySize}.");

    // The offsets are stated from behind a preamble on the data stream rather than from its start.
    // Rather than assume how long that is, it is taken to be whatever the stream holds over and
    // above the tiles — and every tile is then required to begin where a JPEG begins, which is what
    // says the sum was right.
    long stated = 0;
    for (var i = 0; i < tileCount; ++i)
      stated += BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageHeaderSize + i * _TileEntrySize + 4));

    var preamble = tiles.Length - stated;
    if (preamble < 0)
      throw new InvalidDataException(
        $"FlashPix tiles state {stated} bytes between them where their stream holds {tiles.Length}.");

    var canvas = new byte[width * height * 3];
    for (var tile = 0; tile < tileCount; ++tile) {
      var entryAt = _SubimageHeaderSize + tile * _TileEntrySize;
      var at = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(entryAt)) + preamble;
      var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(entryAt + 4));
      var compression = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(entryAt + 8));
      var subtype = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(entryAt + 12));

      if (at < 0 || length < 0 || at + length > tiles.Length)
        throw new InvalidDataException(
          $"FlashPix tile {tile} runs from {at} for {length} bytes of a {tiles.Length}-byte stream.");

      var left = tile % across * tileSide;
      var top = tile / across * tileSide;
      var tileWidth = Math.Min(tileSide, width - left);
      var tileHeight = Math.Min(tileSide, height - top);

      switch (compression) {
        case _TileJpeg:
          _DrawJpegTile(canvas, width, height, tiles.AsSpan((int)at, length), tables,
            (int)((subtype >> 16) & 0xFF), left, top, tileWidth, tileHeight, tile);
          break;

        case _TileSingleColour:
          // A tile of one colour has no data of its own: the colour is the low three bytes of the
          // entry's own subtype word, which is why such a tile states a length of nothing.
          if (length >= 3)
            _DrawFlatTile(canvas, width, tiles[(int)at], tiles[(int)at + 1], tiles[(int)at + 2],
              left, top, tileWidth, tileHeight);
          else
            _DrawFlatTile(canvas, width, (byte)subtype, (byte)(subtype >> 8), (byte)(subtype >> 16),
              left, top, tileWidth, tileHeight);
          break;

        default:
          throw new InvalidDataException(
            $"FlashPix tile {tile} states compression {compression}; only {_TileJpeg}, a JPEG, and "
            + $"{_TileSingleColour}, a single colour, are read here, and {_TileUncompressed} has not been "
            + "checked against a file.");
      }
    }

    return new() { Width = width, Height = height, PixelData = canvas };
  }

  private static void _DrawJpegTile(
    byte[] canvas, int canvasWidth, int canvasHeight, ReadOnlySpan<byte> tile,
    Dictionary<int, byte[]> tables, int tableSet, int left, int top, int tileWidth, int tileHeight, int index) {

    if (tile.Length < 4 || tile[0] != 0xFF || tile[1] != 0xD8)
      throw new InvalidDataException($"FlashPix tile {index} does not begin where a JPEG begins.");

    // A tile naming a set the picture does not carry is one that carries its own tables, which is
    // what the files that mix the two do. Nothing is spliced in and the decoder is left to say so
    // if the tile turns out to have no tables either.
    if (!tables.TryGetValue(tableSet, out var set))
      set = [];

    // The tile is a JPEG with its tables left out, so they go back in behind the start marker.
    var stream = new byte[tile.Length + set.Length];
    stream[0] = 0xFF;
    stream[1] = 0xD8;
    set.CopyTo(stream.AsSpan(2));
    tile[2..].CopyTo(stream.AsSpan(2 + set.Length));

    JpegManagedDecoder.ComponentPlanes decoded;
    try {
      decoded = JpegManagedDecoder.DecodeToPlanes(stream);
    } catch (Exception exception) when (exception is not InvalidDataException) {
      throw new InvalidDataException(
        $"FlashPix tile {index} does not decode as a JPEG with table set {tableSet}: {exception.Message}",
        exception);
    }

    if (decoded.Planes.Length is not 3 and not 4)
      throw new InvalidDataException(
        $"FlashPix tile {index} holds {decoded.Planes.Length} components where a colour tile has three or four.");

    var luma = decoded.Planes[0];
    var blue = decoded.Planes[1];
    var red = decoded.Planes[2];

    for (var y = 0; y < tileHeight && top + y < canvasHeight; ++y)
    for (var x = 0; x < tileWidth && left + x < canvasWidth; ++x) {
      var from = y * decoded.Width + x;
      if (from >= luma.Length || from >= blue.Length || from >= red.Length)
        continue;

      var into = ((top + y) * canvasWidth + left + x) * 3;
      var cb = blue[from] - 128;
      var cr = red[from] - 128;
      canvas[into] = _Clamp(luma[from] + 1.402 * cr);
      canvas[into + 1] = _Clamp(luma[from] - 0.344136 * cb - 0.714136 * cr);
      canvas[into + 2] = _Clamp(luma[from] + 1.772 * cb);
    }
  }

  private static void _DrawFlatTile(
    byte[] canvas, int canvasWidth, byte luma, byte blue, byte red,
    int left, int top, int tileWidth, int tileHeight) {

    var cb = blue - 128;
    var cr = red - 128;
    var r = _Clamp(luma + 1.402 * cr);
    var g = _Clamp(luma - 0.344136 * cb - 0.714136 * cr);
    var b = _Clamp(luma + 1.772 * cb);

    for (var y = 0; y < tileHeight; ++y)
    for (var x = 0; x < tileWidth; ++x) {
      var into = ((top + y) * canvasWidth + left + x) * 3;
      canvas[into] = r;
      canvas[into + 1] = g;
      canvas[into + 2] = b;
    }
  }

  private static byte _Clamp(double value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value + 0.5);
}
