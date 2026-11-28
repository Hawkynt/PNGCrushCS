using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.MrSid;

/// <summary>Reads the header, metadata and georeferencing of MrSID files from bytes, streams, or file paths.</summary>
/// <remarks>Every multi-byte field in every generation is big-endian. The layout is described in <c>SPEC.md</c>.</remarks>
public static class MrSidReader {

  /// <summary>The bytes every generation's fixed header needs: generation 3/4 boxes start here.</summary>
  private const int _BOX_START = 0x3A;

  /// <summary>Generation 2: <c>msid</c>, version, two bytes, five 32-bit fields, then the level offset table.</summary>
  private const int _MG2_TABLE_START = 27;

  /// <summary>Generation 2 subband offsets: three per wavelet level.</summary>
  private const int _MG2_OFFSETS_PER_LEVEL = 3;

  /// <summary>More levels than any picture could have: 2^32 pixels wide still needs only 32.</summary>
  private const int _MAX_LEVELS = 32;

  /// <summary>A bound on the band count well above anything an encoder writes, so a corrupt field fails here.</summary>
  private const int _MAX_BANDS = 255;

  /// <summary>A bound on generation 2 lowest-level tiles: one per 253 lowest-level samples, so a corrupt count fails here.</summary>
  private const int _MAX_TILES = 1 << 16;

  /// <summary>A bound on the dimensions of one metadata record.</summary>
  private const int _MAX_DIMENSIONS = 8;

  public static MrSidFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MrSID file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MrSidFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static MrSidFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <exception cref="InvalidDataException">Not a MrSID file, or a header, offset or metadata record that does not fit the file.</exception>
  /// <exception cref="NotSupportedException">A generation 4 LiDAR point cloud, or a version byte no file has shown.</exception>
  public static MrSidFile FromSpan(ReadOnlySpan<byte> data) {
    var header = ReadFixedHeader(data);
    return header.Generation == MrSidGeneration.Mg2 ? _ReadMg2(data, header) : _ReadMg3(data, header);
  }

  /// <summary>The fields every generation states at a fixed place.</summary>
  internal readonly record struct FixedHeader(MrSidGeneration Generation, int Width, int Height, int BandCount, int BitsPerSample, int LevelCount);

  internal static FixedHeader ReadFixedHeader(ReadOnlySpan<byte> data) {
    if (data.Length < 5 || !data[..4].SequenceEqual("msid"u8))
      throw new InvalidDataException("Not a MrSID file: the \"msid\" signature is missing.");

    switch (data[4]) {
      case 1: {
        if (data.Length < _MG2_TABLE_START)
          throw new InvalidDataException("MrSID MG2 header is truncated.");

        var bands = _Int(data, 7, "band count");
        var levels = _Int(data, 11, "level count");
        var width = _Int(data, 15, "width");
        var height = _Int(data, 19, "height");
        _Check(width, height, bands, levels, "MG2");
        return new(MrSidGeneration.Mg2, width, height, bands, 8, levels);
      }
      case 3: {
        if (data.Length < _BOX_START)
          throw new InvalidDataException("MrSID MG3/MG4 header is truncated.");

        var generation = data[5] >= 4 ? MrSidGeneration.Mg4 : MrSidGeneration.Mg3;
        var width = _Int(data, 0x1A, "width");
        var height = _Int(data, 0x1E, "height");
        int levels = data[0x22];
        int bands = data[0x24];
        int bits = data[0x28];
        _Check(width, height, bands, levels, MrSidFile.Name(generation));
        if (bits is < 1 or > 64)
          throw new InvalidDataException($"MrSID {MrSidFile.Name(generation)} header states {bits} bits per sample.");

        return new(generation, width, height, bands, bits, levels);
      }
      case 4:
        throw new NotSupportedException("This is a MrSID MG4 LiDAR file: a compressed point cloud, not a raster picture.");
      default:
        throw new NotSupportedException($"MrSID version byte {data[4]} is not one of the known generations (1 for MG2, 3 for MG3/MG4, 4 for MG4 LiDAR).");
    }
  }

  private static void _Check(int width, int height, int bands, int levels, string generation) {
    if (width == 0 || height == 0)
      throw new InvalidDataException($"MrSID {generation} header states a {width}x{height} picture.");
    if (bands is < 1 or > _MAX_BANDS)
      throw new InvalidDataException($"MrSID {generation} header states {bands} bands.");
    if (levels > _MAX_LEVELS)
      throw new InvalidDataException($"MrSID {generation} header states {levels} wavelet levels.");
  }

  private static int _Int(ReadOnlySpan<byte> data, int offset, string what) {
    var value = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
    if (value > int.MaxValue)
      throw new InvalidDataException($"MrSID {what} {value} is out of range.");
    return (int)value;
  }

  private static MrSidFile _ReadMg2(ReadOnlySpan<byte> data, FixedHeader header) {
    var headerEnd = _Int(data, 23, "header length");
    var tableEnd = _MG2_TABLE_START + 4 * _MG2_OFFSETS_PER_LEVEL * header.LevelCount;
    if (headerEnd < tableEnd || headerEnd > data.Length - 8)
      throw new InvalidDataException($"MrSID MG2 header length {headerEnd} does not fit a {header.LevelCount}-level offset table and a {data.Length}-byte file.");

    for (var offset = _MG2_TABLE_START; offset < tableEnd; offset += 4)
      if (_Int(data, offset, "subband offset") >= data.Length)
        throw new InvalidDataException("MrSID MG2 file is truncated: a subband offset points past its end.");

    var metadata = new List<MrSidMetadataRecord>();
    var unread = false;
    var position = tableEnd;
    for (; position + 6 <= headerEnd; position += 6) {
      if (data[position] != 0xFF)
        throw new InvalidDataException($"MrSID MG2 header segment at {position} does not start with a marker.");
      if (data[position + 1] != 0xD0)
        continue;

      var at = _Int(data, position + 2, "metadata offset");
      if (at > data.Length - 6 || data[at] != 0xFF || data[at + 1] != 0xD1)
        throw new InvalidDataException("MrSID MG2 metadata pointer does not lead to a metadata segment.");
      unread |= _ReadRecords(data, at + 6, data.Length, metadata, isMg2: true);
    }

    if (position != headerEnd)
      throw new InvalidDataException("MrSID MG2 header segments do not end where the header length says.");

    if (data[headerEnd] != 0xFF || data[headerEnd + 1] != 0xAC)
      throw new InvalidDataException("MrSID MG2 tile directory is missing after the header.");

    var tiles = _Int(data, headerEnd + 4, "tile count");
    var fields = header.BandCount + 4;
    if (tiles is < 1 or > _MAX_TILES || headerEnd + 8 + (long)tiles * fields * 4 > data.Length)
      throw new InvalidDataException($"MrSID MG2 tile directory of {tiles} tiles does not fit the file.");

    for (var tile = 0; tile < tiles; ++tile)
      for (var band = 0; band < header.BandCount; ++band) {
        var at = _Int(data, headerEnd + 8 + (tile * fields + band) * 4, "band offset");
        if (at > data.Length - 2 || data[at] != 0xFF || data[at + 1] != 0xAA)
          throw new InvalidDataException("MrSID MG2 tile band offset does not lead to a coding table segment.");
      }

    var bits = header.BitsPerSample;
    foreach (var record in metadata)
      if (record is { Key: "IMAGE::BITS_PER_SAMPLE", Numbers.Count: 1 })
        bits = (int)record.Numbers[0];

    return _Build(header, metadata, unread, null) with { BitsPerSample = bits, TileCount = tiles };
  }

  private static MrSidFile _ReadMg3(ReadOnlySpan<byte> data, FixedHeader header) {
    var metadata = new List<MrSidMetadataRecord>();
    var unread = false;
    MrSidGeoreference? georeference = null;
    var codestream = false;
    var position = (long)_BOX_START;
    while (position < data.Length) {
      var at = (int)position;
      int headerSize;
      switch (data[at]) {
        case 0x02 when at + 1 < data.Length && data[at + 1] == 0x43: headerSize = 11; break;
        case 0x04: headerSize = 10; break;
        case 0x44: headerSize = 12; break;
        default: position = data.Length; continue;
      }

      if (at + headerSize > data.Length)
        throw new InvalidDataException($"MrSID {MrSidFile.Name(header.Generation)} box header at {at} is truncated.");

      var length = BinaryPrimitives.ReadUInt64BigEndian(data[(at + headerSize - 8)..]);
      var payload = at + headerSize;
      if (length > (ulong)(data.Length - payload))
        throw new InvalidDataException($"MrSID {MrSidFile.Name(header.Generation)} box at {at} runs {length} bytes past a {data.Length}-byte file.");

      var end = payload + (int)length;
      codestream |= headerSize != 11;
      if (headerSize == 11)
        switch (data[at + 2]) {
          case 1:
            if (length < 8)
              throw new InvalidDataException("MrSID metadata box is too short.");
            unread |= _ReadRecords(data, payload + 8, end, metadata, isMg2: false);
            break;
          case 3 when length >= 53:
            var box = data[(payload + 5)..];
            georeference = new(_Double(box, 0), _Double(box, 8), _Double(box, 32), _Double(box, 40));
            break;
        }

      position = end;
    }

    if (!codestream)
      throw new InvalidDataException($"MrSID {MrSidFile.Name(header.Generation)} file holds no codestream box.");

    var generation = header.Generation;
    foreach (var record in metadata)
      if (record is { Key: "IMAGE::FORMAT", Strings.Count: 1 })
        generation = record.Strings[0] switch {
          "MrSID/MG4" => MrSidGeneration.Mg4,
          "MrSID/MG3" => MrSidGeneration.Mg3,
          _ => generation,
        };

    return _Build(header with { Generation = generation }, metadata, unread, georeference);
  }

  private static MrSidFile _Build(FixedHeader header, List<MrSidMetadataRecord> metadata, bool unread, MrSidGeoreference? georeference) {
    if (georeference is null) {
      double[]? origin = null;
      double? xResolution = null, yResolution = null;
      foreach (var record in metadata)
        switch (record.Key) {
          case "IMAGE::XY_ORIGIN" when record.Numbers.Count == 2: origin = [record.Numbers[0], record.Numbers[1]]; break;
          case "IMAGE::X_RESOLUTION" when record.Numbers.Count == 1: xResolution = record.Numbers[0]; break;
          case "IMAGE::Y_RESOLUTION" when record.Numbers.Count == 1: yResolution = record.Numbers[0]; break;
        }

      // The metadata states both resolutions as distances; rows run southward, as GDAL's tests for these files expect.
      if (origin is not null && xResolution is { } x && yResolution is { } y)
        georeference = new(origin[0], origin[1], x, -y);
    }

    return new() {
      Generation = header.Generation,
      Width = header.Width,
      Height = header.Height,
      BandCount = header.BandCount,
      BitsPerSample = header.BitsPerSample,
      LevelCount = header.LevelCount,
      Metadata = metadata,
      HasUnreadMetadata = unread,
      Georeference = georeference,
    };
  }

  private static double _Double(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadDoubleBigEndian(data[offset..]);

  /// <summary>Reads records until the terminator; returns whether it stopped early at a value type it cannot size.</summary>
  /// <remarks>
  /// A record is a 32-bit length (the key's length plus 16), the key, a NUL, then the type, the element count, the
  /// number of dimensions and each dimension as 32-bit fields, then the elements. Generation 2 ends the list with an
  /// <c>FF D2</c> marker, generation 3 and 4 with <c>FF FF FF FF</c>.
  /// </remarks>
  private static bool _ReadRecords(ReadOnlySpan<byte> data, int position, int end, List<MrSidMetadataRecord> records, bool isMg2) {
    while (true) {
      if (position + 4 > end)
        throw new InvalidDataException("MrSID metadata ends without its terminator.");
      if (isMg2 ? data[position] == 0xFF && data[position + 1] == 0xD2 : BinaryPrimitives.ReadUInt32BigEndian(data[position..]) == uint.MaxValue)
        return false;

      var length = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
      if (length < 16 || length - 16 > (uint)(end - position - 4))
        throw new InvalidDataException($"MrSID metadata record at {position} states a length of {length}.");

      var keyLength = (int)length - 16;
      var key = Encoding.ASCII.GetString(data.Slice(position + 4, keyLength));
      var at = position + 4 + keyLength;
      if (at + 13 > end || data[at] != 0)
        throw new InvalidDataException($"MrSID metadata record \"{key}\" is not NUL-terminated.");

      ++at;
      var typeCode = BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
      var count = BinaryPrimitives.ReadUInt32BigEndian(data[(at + 4)..]);
      var dimensionCount = BinaryPrimitives.ReadUInt32BigEndian(data[(at + 8)..]);
      at += 12;
      if (dimensionCount > _MAX_DIMENSIONS || at + 4 * (int)dimensionCount > end)
        throw new InvalidDataException($"MrSID metadata record \"{key}\" states {dimensionCount} dimensions.");

      var dimensions = new int[dimensionCount];
      for (var i = 0; i < dimensions.Length; ++i, at += 4)
        dimensions[i] = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data[at..]), int.MaxValue);

      var type = (MrSidMetadataType)typeCode;
      var size = type switch {
        MrSidMetadataType.UInt8 => 1,
        MrSidMetadataType.UInt16 => 2,
        MrSidMetadataType.UInt32 or MrSidMetadataType.Float32 or MrSidMetadataType.Int32 => 4,
        MrSidMetadataType.Float64 => 8,
        MrSidMetadataType.Ascii => 0,
        _ => -1,
      };
      if (size < 0)
        return true;

      if (count > (uint)(end - at))
        throw new InvalidDataException($"MrSID metadata record \"{key}\" states {count} values.");

      var numbers = new List<double>();
      var strings = new List<string>();
      if (type == MrSidMetadataType.Ascii)
        for (var i = 0u; i < count; ++i) {
          var terminator = data[at..end].IndexOf((byte)0);
          if (terminator < 0)
            throw new InvalidDataException($"MrSID metadata string \"{key}\" runs past its block.");
          strings.Add(Encoding.Latin1.GetString(data.Slice(at, terminator)));
          at += terminator + 1;
        }
      else {
        if ((long)count * size > end - at)
          throw new InvalidDataException($"MrSID metadata record \"{key}\" runs past its block.");
        for (var i = 0u; i < count; ++i, at += size)
          numbers.Add(type switch {
            MrSidMetadataType.UInt8 => data[at],
            MrSidMetadataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(data[at..]),
            MrSidMetadataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(data[at..]),
            MrSidMetadataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(data[at..]),
            MrSidMetadataType.Float32 => BinaryPrimitives.ReadSingleBigEndian(data[at..]),
            _ => BinaryPrimitives.ReadDoubleBigEndian(data[at..]),
          });
      }

      records.Add(new(key, type, dimensions, numbers, strings));
      position = at;
    }
  }
}
