using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PictureGearPocket;

/// <summary>Reads Sony PictureGear Pocket pictures from bytes, streams, or file paths.</summary>
public static class PictureGearPocketReader {

  /// <summary>The Palm database header, before the list of resources.</summary>
  private const int _DatabaseHeaderSize = 78;

  /// <summary>Each entry in the resource list: a type, a sixteen-bit id and a four-byte offset.</summary>
  private const int _ResourceEntrySize = 10;

  /// <summary>An info resource is nine big-endian words.</summary>
  private const int _InfoSize = 18;

  /// <summary>The id the thumbnail's info resource has; the picture's is any other.</summary>
  internal const ushort ThumbnailId = 1000;

  private static ReadOnlySpan<byte> _Signature => "IMVSIMVS"u8;

  /// <summary>Whether the bytes begin a Palm database whose type and creator are both <c>IMVS</c>.</summary>
  public static bool HasSignature(ReadOnlySpan<byte> header) => header.Length >= 68 && header.Slice(60, 8).SequenceEqual(_Signature);

  public static PictureGearPocketFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PictureGear Pocket file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PictureGearPocketFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static PictureGearPocketFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PictureGearPocketFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _DatabaseHeaderSize)
      throw new InvalidDataException("Data too small for a Palm database.");
    if (!HasSignature(data))
      throw new InvalidDataException(
        $"Not a PictureGear Pocket database: type '{Encoding.ASCII.GetString(data.Slice(60, 4))}' "
        + $"creator '{Encoding.ASCII.GetString(data.Slice(64, 4))}'.");

    var resources = _ReadResources(data);

    // Sony's reader takes the info resource numbered 1000 for the thumbnail and any other for the
    // picture, the last one it meets winning.
    (ushort Id, int Offset, int Length)? info = null;
    foreach (var resource in resources)
      if (resource.Type == "iINF" && resource.Id != ThumbnailId)
        info = (resource.Id, resource.Offset, resource.Length);

    if (info is not { } main)
      throw new InvalidDataException("The database holds a thumbnail but no picture: there is no info resource other than 1000.");

    var image = _ReadImage(data, resources, main.Offset, main.Length);
    if (image.BitsPerPixel is not (8 or 16))
      throw new InvalidDataException(
        $"The picture is {image.BitsPerPixel} bits deep; Sony's software reads eight (palette indices) and sixteen (RGB 5:6:5) and nothing else.");

    // Sony's decoder never looks at the thumbnail, so a damaged one costs the thumbnail and not the
    // picture; the writer makes a new one.
    byte[]? thumbnail = null;
    foreach (var resource in resources)
      if (resource.Type == "iINF" && resource.Id == ThumbnailId)
        try {
          var small = _ReadImage(data, resources, resource.Offset, resource.Length, isThumbnail: true);
          thumbnail = small is { BitsPerPixel: 8, Width: PictureGearPocketFile.ThumbnailSize, Height: PictureGearPocketFile.ThumbnailSize }
            ? small.Pixels
            : null;
        } catch (InvalidDataException) {
          thumbnail = null;
        }

    var nameEnd = 0;
    while (nameEnd < 32 && data[nameEnd] != 0)
      ++nameEnd;

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = image.BitsPerPixel,
      TileSize = image.TileSize,
      IsCompressed = image.IsCompressed,
      Name = Encoding.Latin1.GetString(data[..nameEnd]),
      Created = PictureGearPocketCodec.FromPalmSeconds(BinaryPrimitives.ReadUInt32BigEndian(data[36..])),
      Modified = PictureGearPocketCodec.FromPalmSeconds(BinaryPrimitives.ReadUInt32BigEndian(data[40..])),
      LastBackup = PictureGearPocketCodec.FromPalmSeconds(BinaryPrimitives.ReadUInt32BigEndian(data[44..])),
      ModificationNumber = BinaryPrimitives.ReadUInt32BigEndian(data[48..]),
      UniqueIdSeed = BinaryPrimitives.ReadUInt32BigEndian(data[68..]),
      Palette = image.Palette,
      PixelData = image.Pixels,
      Thumbnail = thumbnail,
    };
  }

  private readonly record struct Resource(string Type, ushort Id, int Offset, int Length);

  private readonly record struct Image(int Width, int Height, int BitsPerPixel, int TileSize, bool IsCompressed, byte[]? Palette, byte[] Pixels);

  /// <summary>Lists the resources, each running to the next one's start or the end of the file.</summary>
  private static List<Resource> _ReadResources(ReadOnlySpan<byte> data) {
    var count = BinaryPrimitives.ReadUInt16BigEndian(data[76..]);
    if (_DatabaseHeaderSize + count * _ResourceEntrySize > data.Length)
      throw new InvalidDataException($"The database lists {count} resources but ends inside the list.");

    var offsets = new List<int>(count);
    var entries = new List<(string Type, ushort Id, int Offset)>(count);
    for (var i = 0; i < count; ++i) {
      var entry = data.Slice(_DatabaseHeaderSize + i * _ResourceEntrySize, _ResourceEntrySize);
      var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[6..]);
      if (offset > (uint)data.Length)
        throw new InvalidDataException($"Resource {i} begins at {offset}, past the end of the {data.Length}-byte file.");

      entries.Add((Encoding.ASCII.GetString(entry[..4]), BinaryPrimitives.ReadUInt16BigEndian(entry[4..]), (int)offset));
      offsets.Add((int)offset);
    }

    offsets.Add(data.Length);
    offsets.Sort();

    var resources = new List<Resource>(count);
    foreach (var (type, id, offset) in entries) {
      var end = offset;
      foreach (var next in offsets)
        if (next > offset) {
          end = next;
          break;
        }

      resources.Add(new(type, id, offset, end - offset));
    }

    return resources;
  }

  private static Resource? _Find(List<Resource> resources, string type, ushort id) {
    foreach (var resource in resources)
      if (resource.Type == type && resource.Id == id)
        return resource;

    return null;
  }

  /// <summary>Reads one info resource and everything it names: the frame list, the palette and the tiles.</summary>
  private static Image _ReadImage(ReadOnlySpan<byte> data, List<Resource> resources, int offset, int length, bool isThumbnail = false) {
    if (length < _InfoSize)
      throw new InvalidDataException("An info resource is shorter than its nine fields.");

    var info = data.Slice(offset, _InfoSize);
    var width = BinaryPrimitives.ReadUInt16BigEndian(info);
    var height = BinaryPrimitives.ReadUInt16BigEndian(info[2..]);
    var depth = BinaryPrimitives.ReadUInt16BigEndian(info[6..]);
    var tileSize = BinaryPrimitives.ReadUInt16BigEndian(info[10..]);
    var compression = BinaryPrimitives.ReadUInt16BigEndian(info[12..]);
    var frameId = BinaryPrimitives.ReadUInt16BigEndian(info[14..]);
    var paletteId = BinaryPrimitives.ReadUInt16BigEndian(info[16..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"An info resource gives the size {width}x{height}.");
    if (width > PictureGearPocketFile.MaximumExtent || height > PictureGearPocketFile.MaximumExtent)
      throw new InvalidDataException($"A {width}x{height} picture is beyond what this reader takes.");
    if (depth is not (8 or 16))
      return new(width, height, depth, tileSize, false, null, []);
    if (tileSize is 0 or > 128)
      throw new InvalidDataException($"Tiles of {tileSize} pixels are not something the format holds.");

    var frames = _Find(resources, "iFRI", frameId)
      ?? throw new InvalidDataException($"The info resource names frame list {frameId}, which is not in the database.");
    if (frames.Length < 8)
      throw new InvalidDataException("The frame list is too short to name a first tile.");

    var frameList = data.Slice(frames.Offset, frames.Length);
    if (BinaryPrimitives.ReadUInt16BigEndian(frameList) < 1)
      throw new InvalidDataException("The frame list names no frames.");

    // Only the first frame is a picture anybody wrote; its tiles are numbered on from the id given.
    var firstTile = BinaryPrimitives.ReadUInt16BigEndian(frameList[6..]);

    byte[]? palette = null;
    if (depth == 8 && paletteId != 0 && _Find(resources, "iPLT", paletteId) is { } plt) {
      var table = data.Slice(plt.Offset, plt.Length);
      if (table.Length < 2)
        throw new InvalidDataException("The palette resource is too short to hold its count.");

      var count = BinaryPrimitives.ReadUInt16BigEndian(table);
      if (count > 256 || 2 + count * 4 > table.Length)
        throw new InvalidDataException($"The palette states {count} entries in {table.Length} bytes.");

      palette = new byte[count * 3];
      for (var i = 0; i < count; ++i)
        table.Slice(2 + i * 4 + 1, 3).CopyTo(palette.AsSpan(i * 3));
    }

    var bytesPerPixel = depth / 8;
    var across = (width + tileSize - 1) / tileSize;
    var down = (height + tileSize - 1) / tileSize;
    if (firstTile + across * down - 1 > ushort.MaxValue)
      throw new InvalidDataException($"A {width}x{height} picture in {tileSize}-pixel tiles needs tile ids past 65535.");

    var pixels = new byte[width * height * bytesPerPixel];
    var tile = new byte[tileSize * tileSize * bytesPerPixel];
    var isCompressed = compression == PictureGearPocketCodec.RunLengthCoded;
    for (var t = 0; t < across * down; ++t) {
      var id = (ushort)(firstTile + t);
      var resource = _Find(resources, "iTIL", id)
        ?? throw new InvalidDataException($"Tile {id} of the {(isThumbnail ? "thumbnail" : "picture")} is not in the database.");
      if (resource.Length < 2)
        throw new InvalidDataException($"Tile {id} is too short to hold its length.");

      var stated = BinaryPrimitives.ReadUInt16BigEndian(data[resource.Offset..]);
      var body = data.Slice(resource.Offset + 2, Math.Min(stated, resource.Length - 2));

      tile.AsSpan().Clear();
      if (isCompressed)
        PictureGearPocketCodec.Decode(body, tile, bytesPerPixel);
      else
        body[..Math.Min(body.Length, tile.Length)].CopyTo(tile);

      var left = t % across * tileSize;
      var top = t / across * tileSize;
      var span = Math.Min(tileSize, width - left) * bytesPerPixel;
      for (var row = 0; row < tileSize && top + row < height; ++row)
        tile.AsSpan(row * tileSize * bytesPerPixel, span).CopyTo(pixels.AsSpan(((top + row) * width + left) * bytesPerPixel));
    }

    return new(width, height, depth, tileSize, isCompressed, palette, pixels);
  }
}
