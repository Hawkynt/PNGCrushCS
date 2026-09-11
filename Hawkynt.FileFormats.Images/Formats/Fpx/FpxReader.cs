using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Jpeg;

namespace FileFormat.Fpx;

/// <summary>Reads FlashPix pictures, and the Picture It! and PhotoDraw documents that are made of them.</summary>
/// <remarks>
/// A FlashPix picture is a compound file holding one or more tiled resolutions. FlashPix streams
/// carry a 28-byte class header which is explicitly excluded from every offset stored inside the
/// stream; treating that prefix as an inferred amount of padding breaks sparse streams and legal
/// single-colour tiles. The reader therefore validates the class header and applies the fixed
/// 28-byte offset directly.
/// </remarks>
public static class FpxReader {

  private const int _TileSide = 64;
  private const int _FlashPixStreamHeaderSize = 28;
  private const int _SubimageHeaderDataSize = 36;
  private const int _SubimageHeaderSize = _FlashPixStreamHeaderSize + _SubimageHeaderDataSize;
  private const int _TileEntrySize = 16;

  private const int _SubimageHeaderLength = 28;
  private const int _SubimageWidth = 32;
  private const int _SubimageHeight = 36;
  private const int _SubimageTiles = 40;
  private const int _SubimageTileWidth = 44;
  private const int _SubimageTileHeight = 48;
  private const int _SubimageChannels = 52;
  private const int _SubimageTileTableOffset = 56;
  private const int _SubimageTileEntrySize = 60;

  private const uint _TileUncompressed = 0;
  private const uint _TileSingleColour = 1;
  private const uint _TileJpeg = 2;

  private const uint _TypeUi4 = 19;
  private const uint _TypeBlob = 65;
  private const uint _TypeVector = 0x1000;
  private const uint _VtUi1 = 17;
  private const int _JpegTablesClass = 0x03;

  private const string _ContentsName = "Image Contents";
  private const string _HeaderName = "Subimage 0000 Header";

  private static readonly Guid _SubimageHeaderClass = new("00010000-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _SubimageDataClass = new("00010100-C154-11CE-8553-00AA00A1F95B");

  private enum ColorEncoding {
    Unknown,
    PhotoYcc,
    NifRgb,
  }

  private readonly record struct ImageContentsInfo(
    Dictionary<int, byte[]> JpegTables,
    ColorEncoding Color,
    bool IsEightBitUnsigned);

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

    var stores = streams
      .Where(entry => _LastPart(entry.Key) == _ContentsName && entry.Value.Type == CompoundFile.EntryStream)
      .ToArray();

    if (stores.Length == 0)
      throw new InvalidDataException("Not a FlashPix picture: the compound file holds no Image Contents stream.");

    KeyValuePair<string, CompoundFile.Entry> best = default;
    KeyValuePair<string, CompoundFile.Entry> bestHeader = default;
    var bestPixels = -1L;

    foreach (var contents in stores) {
      var store = contents.Key[..contents.Key.LastIndexOf('/')];
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

    var contentsBytes = container.Read(best.Value);
    var imageContents = _ReadImageContents(contentsBytes, _ResolutionNumber(bestHeader.Key));
    return _ReadSubimage(container.Read(bestHeader.Value), container.Read(tiles.Value), imageContents);
  }

  private static int _ResolutionNumber(string path) {
    const string marker = "/Resolution ";
    var at = path.LastIndexOf(marker, StringComparison.Ordinal);
    if (at < 0)
      return -1;

    at += marker.Length;
    var end = path.IndexOf('/', at);
    return end > at && int.TryParse(path.AsSpan(at, end - at), out var result) ? result : -1;
  }

  private static ImageContentsInfo _ReadImageContents(byte[] contents, int resolution) {
    var tables = _ReadJpegTableSets(contents);
    if (resolution is < 0 or > 255)
      return new(tables, ColorEncoding.Unknown, false);

    var resolutionBase = 0x02000000u | ((uint)resolution << 16);
    return new(
      tables,
      _ReadColorEncoding(contents, resolutionBase | 2),
      _ReadNumericalFormat(contents, resolutionBase | 3));
  }

  private static ColorEncoding _ReadColorEncoding(byte[] contents, uint propertyId) {
    if (!_TryGetProperty(contents, propertyId, out var at)
        || at + 8 > contents.Length
        || BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at)) != _TypeBlob)
      return ColorEncoding.Unknown;

    var length = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at + 4));
    if (length < 20 || (long)at + 8 + length > contents.Length)
      return ColorEncoding.Unknown;

    var blob = contents.AsSpan(at + 8, (int)length);
    var channels = BinaryPrimitives.ReadUInt32LittleEndian(blob[4..]);
    if (channels is not 3 and not 4 || 8L + channels * 4 > blob.Length)
      return ColorEncoding.Unknown;

    var first = BinaryPrimitives.ReadUInt32LittleEndian(blob[8..]) & 0x7FFFFFFF;
    var second = BinaryPrimitives.ReadUInt32LittleEndian(blob[12..]) & 0x7FFFFFFF;
    var third = BinaryPrimitives.ReadUInt32LittleEndian(blob[16..]) & 0x7FFFFFFF;

    return (first, second, third) switch {
      (0x00030000, 0x00030001, 0x00030002) => ColorEncoding.NifRgb,
      (0x00020000, 0x00020001, 0x00020002) => ColorEncoding.PhotoYcc,
      _ => ColorEncoding.Unknown,
    };
  }

  private static bool _ReadNumericalFormat(byte[] contents, uint propertyId) {
    if (!_TryGetProperty(contents, propertyId, out var at) || at + 8 > contents.Length)
      return false;
    if (BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at)) != (_TypeUi4 | _TypeVector))
      return false;

    var count = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at + 4));
    if (count == 0 || count > 16 || (long)at + 8 + count * 4 > contents.Length)
      return false;

    for (var i = 0u; i < count; ++i)
      if (BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at + 8 + checked((int)i * 4))) != _VtUi1)
        return false;

    return true;
  }

  private static bool _TryGetProperty(byte[] contents, uint identifier, out int propertyAt) {
    propertyAt = 0;
    if (contents.Length < 48)
      return false;

    var sectionAtValue = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(44));
    if (sectionAtValue > int.MaxValue || sectionAtValue + 8L > contents.Length)
      return false;

    var sectionAt = (int)sectionAtValue;
    var count = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(sectionAt + 4));
    if (sectionAt + 8L + count * 8 > contents.Length)
      return false;

    for (var i = 0u; i < count; ++i) {
      var pair = sectionAt + 8 + checked((int)i * 8);
      if (BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(pair)) != identifier)
        continue;

      var relative = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(pair + 4));
      if (relative > int.MaxValue || sectionAt + (long)relative > contents.Length - 4)
        return false;

      propertyAt = sectionAt + (int)relative;
      return true;
    }

    return false;
  }

  private static Dictionary<int, byte[]> _ReadJpegTableSets(byte[] contents) {
    if (contents.Length < 48)
      throw new InvalidDataException($"FlashPix Image Contents is {contents.Length} bytes, too short to hold a property set.");

    var sectionAtValue = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(44));
    if (sectionAtValue > int.MaxValue || sectionAtValue + 8L > contents.Length)
      throw new InvalidDataException($"FlashPix Image Contents puts its section at {sectionAtValue} of {contents.Length} bytes.");

    var sectionAt = (int)sectionAtValue;
    var count = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(sectionAt + 4));
    if (sectionAt + 8L + count * 8 > contents.Length)
      throw new InvalidDataException($"FlashPix Image Contents states {count} properties, which do not fit in its {contents.Length} bytes.");

    var sets = new Dictionary<int, byte[]>();
    for (var i = 0u; i < count; ++i) {
      var pair = sectionAt + 8 + checked((int)i * 8);
      var identifier = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(pair));
      var relative = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(pair + 4));
      if (relative > int.MaxValue || sectionAt + (long)relative + 8 > contents.Length)
        continue;

      var at = sectionAt + (int)relative;
      if (identifier >> 24 != _JpegTablesClass
          || BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at)) != _TypeBlob)
        continue;

      var length = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at + 4));
      if (length < 4 || at + 8L + length > contents.Length)
        throw new InvalidDataException(
          $"FlashPix table set 0x{identifier:X8} states {length} bytes, which reach past the Image Contents stream.");

      var blob = contents.AsSpan(at + 8, (int)length);
      if (blob[0] != 0xFF || blob[1] != 0xD8 || blob[^2] != 0xFF || blob[^1] != 0xD9)
        continue;

      sets[(int)((identifier >> 16) & 0xFF)] = blob[2..^2].ToArray();
    }

    return sets;
  }

  private static FpxFile _ReadSubimage(byte[] header, byte[] tiles, ImageContentsInfo imageContents) {
    _ValidateFlashPixStream(header, _SubimageHeaderClass, "subimage header");
    _ValidateFlashPixStream(tiles, _SubimageDataClass, "subimage data");

    if (header.Length < _SubimageHeaderSize)
      throw new InvalidDataException(
        $"FlashPix subimage header is {header.Length} bytes where its fixed part takes {_SubimageHeaderSize}.");

    var headerLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageHeaderLength));
    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageWidth));
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageHeight));
    var tileCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageTiles));
    var tileWidth = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageTileWidth));
    var tileHeight = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageTileHeight));
    var channels = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageChannels));
    var tileTableOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageTileTableOffset));
    var tileEntrySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(_SubimageTileEntrySize));

    if (headerLength != _SubimageHeaderDataSize)
      throw new InvalidDataException($"FlashPix subimage header states {headerLength} bytes where the core header is {_SubimageHeaderDataSize}.");
    if (width is <= 0 or > 1 << 16 || height is <= 0 or > 1 << 16)
      throw new InvalidDataException($"FlashPix subimage is stated as {width} by {height}, which is not a picture size.");
    if (tileWidth != _TileSide || tileHeight != _TileSide)
      throw new InvalidDataException($"FlashPix subimage states {tileWidth} by {tileHeight} pixel tiles; core FlashPix uses {_TileSide} by {_TileSide}.");
    if (channels is not 3 and not 4)
      throw new InvalidDataException($"FlashPix subimage states {channels} channels where this colour reader supports three or four.");
    if (tileTableOffset != _SubimageHeaderDataSize || tileEntrySize != _TileEntrySize)
      throw new InvalidDataException(
        $"FlashPix subimage puts its {tileEntrySize}-byte tile table at {tileTableOffset}; core FlashPix requires {_TileEntrySize} bytes at {_SubimageHeaderDataSize}.");

    var across = (width + _TileSide - 1) / _TileSide;
    var down = (height + _TileSide - 1) / _TileSide;
    if (tileCount != across * down)
      throw new InvalidDataException(
        $"FlashPix subimage states {tileCount} tiles where {width} by {height} in {_TileSide}-pixel tiles needs {across * down}.");

    var expectedHeaderLength = checked(_SubimageHeaderSize + tileCount * _TileEntrySize);
    if (header.Length != expectedHeaderLength)
      throw new InvalidDataException(
        $"FlashPix subimage header is {header.Length} bytes where {tileCount} tiles need {expectedHeaderLength}.");

    var canvas = new byte[checked(width * height * 3)];
    for (var tile = 0; tile < tileCount; ++tile) {
      var entryAt = _SubimageHeaderSize + tile * _TileEntrySize;
      var statedOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(entryAt));
      var statedLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(entryAt + 4));
      var compression = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(entryAt + 8));
      var subtype = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(entryAt + 12));
      var at = _FlashPixStreamHeaderSize + (long)statedOffset;

      if (at < _FlashPixStreamHeaderSize || at + statedLength > tiles.Length)
        throw new InvalidDataException(
          $"FlashPix tile {tile} runs from data offset {statedOffset} for {statedLength} bytes of a {tiles.Length - _FlashPixStreamHeaderSize}-byte data portion.");

      var left = tile % across * _TileSide;
      var top = tile / across * _TileSide;
      var visibleWidth = Math.Min(_TileSide, width - left);
      var visibleHeight = Math.Min(_TileSide, height - top);

      switch (compression) {
        case _TileUncompressed:
          if (!imageContents.IsEightBitUnsigned)
            throw new InvalidDataException($"FlashPix tile {tile} is uncompressed but its Image Contents does not declare 8-bit unsigned samples.");
          _DrawUncompressedTile(
            canvas, width, tiles.AsSpan((int)at, checked((int)statedLength)), imageContents.Color, channels,
            left, top, visibleWidth, visibleHeight, tile);
          break;

        case _TileSingleColour:
          if (statedOffset != 0 || statedLength != 0)
            throw new InvalidDataException($"FlashPix single-colour tile {tile} must have zero offset and zero data length.");
          _DrawFlatTile(
            canvas, width, imageContents.Color,
            (byte)subtype, (byte)(subtype >> 8), (byte)(subtype >> 16),
            left, top, visibleWidth, visibleHeight);
          break;

        case _TileJpeg:
          _DrawJpegTile(
            canvas, width, height, tiles.AsSpan((int)at, checked((int)statedLength)), imageContents,
            (int)(subtype >> 24), ((subtype >> 16) & 0xFF) != 0,
            left, top, visibleWidth, visibleHeight, tile);
          break;

        default:
          throw new InvalidDataException(
            $"FlashPix tile {tile} states compression {compression}; core FlashPix defines 0 (raw), 1 (single colour), and 2 (JPEG).");
      }
    }

    return new() { Width = width, Height = height, PixelData = canvas };
  }

  private static void _ValidateFlashPixStream(byte[] stream, Guid expectedClass, string description) {
    if (stream.Length < _FlashPixStreamHeaderSize)
      throw new InvalidDataException($"FlashPix {description} is too short to contain its 28-byte stream header.");
    if (BinaryPrimitives.ReadUInt16LittleEndian(stream) != 0xFFFE
        || BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(2)) != 0)
      throw new InvalidDataException($"FlashPix {description} does not carry a little-endian version-0 stream header.");

    var actualClass = new Guid(stream.AsSpan(8, 16));
    if (actualClass != expectedClass)
      throw new InvalidDataException($"FlashPix {description} has class ID {actualClass:B} instead of {expectedClass:B}.");
  }

  private static void _DrawUncompressedTile(
    byte[] canvas, int canvasWidth, ReadOnlySpan<byte> tile, ColorEncoding color, int channels,
    int left, int top, int visibleWidth, int visibleHeight, int index) {

    var expected = checked(_TileSide * _TileSide * channels);
    if (tile.Length != expected)
      throw new InvalidDataException($"FlashPix raw tile {index} is {tile.Length} bytes where {_TileSide}x{_TileSide}x{channels} needs {expected}.");
    if (color == ColorEncoding.Unknown)
      throw new InvalidDataException($"FlashPix raw tile {index} uses a colour encoding this reader does not recognize.");

    for (var y = 0; y < visibleHeight; ++y)
    for (var x = 0; x < visibleWidth; ++x) {
      var from = (y * _TileSide + x) * channels;
      var into = ((top + y) * canvasWidth + left + x) * 3;
      _WriteColor(canvas, into, color, tile[from], tile[from + 1], tile[from + 2]);
    }
  }

  private static void _DrawJpegTile(
    byte[] canvas, int canvasWidth, int canvasHeight, ReadOnlySpan<byte> tile,
    ImageContentsInfo imageContents, int tableSet, bool internalColorConversion,
    int left, int top, int tileWidth, int tileHeight, int index) {

    if (tile.Length < 4 || tile[0] != 0xFF || tile[1] != 0xD8)
      throw new InvalidDataException($"FlashPix tile {index} does not begin where a JPEG begins.");

    if (!imageContents.JpegTables.TryGetValue(tableSet, out var set))
      set = [];

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

    var first = decoded.Planes[0];
    var second = decoded.Planes[1];
    var third = decoded.Planes[2];
    var directRgb = !internalColorConversion && imageContents.Color == ColorEncoding.NifRgb;

    for (var y = 0; y < tileHeight && top + y < canvasHeight; ++y)
    for (var x = 0; x < tileWidth && left + x < canvasWidth; ++x) {
      var from = y * decoded.Width + x;
      if (from >= first.Length || from >= second.Length || from >= third.Length)
        continue;

      var into = ((top + y) * canvasWidth + left + x) * 3;
      if (directRgb) {
        canvas[into] = first[from];
        canvas[into + 1] = second[from];
        canvas[into + 2] = third[from];
      } else {
        _WriteYcc(canvas, into, first[from], second[from], third[from]);
      }
    }
  }

  private static void _DrawFlatTile(
    byte[] canvas, int canvasWidth, ColorEncoding color, byte first, byte second, byte third,
    int left, int top, int tileWidth, int tileHeight) {

    for (var y = 0; y < tileHeight; ++y)
    for (var x = 0; x < tileWidth; ++x) {
      var into = ((top + y) * canvasWidth + left + x) * 3;
      _WriteColor(canvas, into, color, first, second, third);
    }
  }

  private static void _WriteColor(byte[] canvas, int into, ColorEncoding color, byte first, byte second, byte third) {
    if (color == ColorEncoding.NifRgb) {
      canvas[into] = first;
      canvas[into + 1] = second;
      canvas[into + 2] = third;
      return;
    }

    _WriteYcc(canvas, into, first, second, third);
  }

  private static void _WriteYcc(byte[] canvas, int into, byte luma, byte blue, byte red) {
    var cb = blue - 128;
    var cr = red - 128;
    canvas[into] = _Clamp(luma + 1.402 * cr);
    canvas[into + 1] = _Clamp(luma - 0.344136 * cb - 0.714136 * cr);
    canvas[into + 2] = _Clamp(luma + 1.772 * cb);
  }

  private static byte _Clamp(double value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value + 0.5);
}
