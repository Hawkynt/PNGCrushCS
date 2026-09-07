using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;
using FileFormat.Emf;
using FileFormat.EmbeddedDib;
using FileFormat.EmbeddedPicture;
using FileFormat.Fpx;
using FileFormat.Pict;

namespace FileFormat.PowerPoint;

/// <summary>Extracts the pictures from a legacy PowerPoint compound document.</summary>
/// <remarks>
/// A real binary PowerPoint file carries its OfficeArtBStoreDelay in the CFB <c>Pictures</c> stream.
/// That stream may contain raster BLIPs (JPEG, PNG, DIB, TIFF) or metafile BLIPs (EMF, WMF, PICT),
/// and an FBSE may inline one of those same records. All supported records are returned in stream
/// order. The historical raw walk behind the 512-byte compound header is retained only for old
/// compatibility fixtures whose bytes look like the original XnView-oriented reader input but do
/// not form a complete CFB directory/FAT.
/// </remarks>
public static class PowerPointReader {

  private const ushort _OfficeArtFbse = 0xF007;
  private const ushort _EmfBlip = 0xF01A;
  private const ushort _WmfBlip = 0xF01B;
  private const ushort _PictBlip = 0xF01C;
  private const ushort _DibBlip = 0xF01F;
  private const ushort _TiffBlip = 0xF029;
  private const ushort _JpegBlip2 = 0xF02A;
  private const int _FbseFixedBodySize = 36;
  private const int _MetafileHeaderSize = 34;

  public static PowerPointFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PowerPoint presentation not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PowerPointFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromSpan(memory.ToArray());
  }

  public static PowerPointFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PowerPointFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PowerPointFile.ScanStart)
      throw new InvalidDataException(
        $"Data too small for a PowerPoint presentation (minimum {PowerPointFile.ScanStart} bytes, got {data.Length}).");

    if (!data[..PowerPointFile.Signature.Length].SequenceEqual(PowerPointFile.Signature))
      throw new InvalidDataException("Not a PowerPoint presentation: it is not a Microsoft compound document.");

    IReadOnlyList<RawImage> images;
    try {
      images = _ReadPicturesStream(new CompoundFile(data));
    } catch (InvalidDataException) {
      images = _ReadRecords(data[PowerPointFile.ScanStart..]);
    }

    if (images.Count == 0)
      throw new InvalidDataException("A PowerPoint presentation carries no decodable OfficeArt pictures.");

    var first = images[0].EnsureFormat(PixelFormat.Rgb24);
    return new() {
      Width = first.Width,
      Height = first.Height,
      PixelData = first.PixelData[..],
      Images = images,
      Kind = PowerPointKind.Legacy,
    };
  }

  private static IReadOnlyList<RawImage> _ReadPicturesStream(CompoundFile compound) {
    var pictures = compound.Streams()
      .FirstOrDefault(pair => pair.Value.Type == CompoundFile.EntryStream
        && pair.Key.Equals("/Pictures", StringComparison.OrdinalIgnoreCase));

    return string.IsNullOrEmpty(pictures.Key)
      ? []
      : _ReadRecords(compound.Read(pictures.Value));
  }

  private static IReadOnlyList<RawImage> _ReadRecords(ReadOnlySpan<byte> data) {
    var images = new List<RawImage>();
    for (var at = 0; at + PowerPointFile.RecordHeaderSize <= data.Length;) {
      var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
      if (length > int.MaxValue)
        break;

      var total = (long)PowerPointFile.RecordHeaderSize + length;
      if (total <= PowerPointFile.RecordHeaderSize || total > data.Length - at)
        break;

      var record = data.Slice(at, checked((int)total));
      try {
        _DecodeRecord(record, images);
      } catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException) {
        // A broken/unsupported BLIP does not prevent later pictures from being extracted.
      }

      at += checked((int)total);
    }

    return images;
  }

  private static void _DecodeRecord(ReadOnlySpan<byte> record, List<RawImage> images) {
    if (record.Length < PowerPointFile.RecordHeaderSize)
      return;

    var versionAndInstance = BinaryPrimitives.ReadUInt16LittleEndian(record);
    var type = BinaryPrimitives.ReadUInt16LittleEndian(record[2..]);
    var bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
    if (bodyLength > int.MaxValue || PowerPointFile.RecordHeaderSize + (long)bodyLength > record.Length)
      return;

    var body = record.Slice(PowerPointFile.RecordHeaderSize, checked((int)bodyLength));
    if (type == _OfficeArtFbse) {
      _DecodeFbse(body, images);
      return;
    }

    var instance = versionAndInstance >> 4;
    RawImage? decoded = type switch {
      PowerPointFile.JpegBlipType or _JpegBlip2 => _DecodeRaster(body, _RasterPrefix(instance, type), RasterKind.Encoded),
      PowerPointFile.PngBlipType => _DecodeRaster(body, _RasterPrefix(instance, type), RasterKind.Encoded),
      _TiffBlip => _DecodeRaster(body, _RasterPrefix(instance, type), RasterKind.Encoded),
      _DibBlip => _DecodeRaster(body, _RasterPrefix(instance, type), RasterKind.Dib),
      _EmfBlip => _DecodeMetafile(body, instance, MetafileKind.Emf),
      _WmfBlip => _DecodeMetafile(body, instance, MetafileKind.Wmf),
      _PictBlip => _DecodeMetafile(body, instance, MetafileKind.Pict),
      _ => null,
    };

    if (decoded is not null)
      images.Add(decoded);
  }

  private static void _DecodeFbse(ReadOnlySpan<byte> body, List<RawImage> images) {
    if (body.Length < _FbseFixedBodySize)
      return;

    var nameLength = body[33];
    var embeddedAt = _FbseFixedBodySize + nameLength;
    if (embeddedAt + PowerPointFile.RecordHeaderSize > body.Length)
      return;

    var embeddedLength = BinaryPrimitives.ReadUInt32LittleEndian(body[(embeddedAt + 4)..]);
    var total = (long)PowerPointFile.RecordHeaderSize + embeddedLength;
    if (embeddedLength <= int.MaxValue && total <= body.Length - embeddedAt)
      _DecodeRecord(body.Slice(embeddedAt, checked((int)total)), images);
  }

  private static int _RasterPrefix(int instance, ushort type) => type switch {
    PowerPointFile.PngBlipType when instance == 0x6E0 => 17,
    PowerPointFile.PngBlipType when instance == 0x6E1 => 33,
    _DibBlip when instance == 0x7A8 => 17,
    _DibBlip when instance == 0x7A9 => 33,
    _TiffBlip when instance == 0x6E4 => 17,
    _TiffBlip when instance == 0x6E5 => 33,
    PowerPointFile.JpegBlipType or _JpegBlip2 when instance is 0x46A or 0x6E2 => 17,
    PowerPointFile.JpegBlipType or _JpegBlip2 when instance is 0x46B or 0x6E3 => 33,
    _ => throw new InvalidDataException($"Unsupported OfficeArt raster BLIP instance 0x{instance:X} for type 0x{type:X4}."),
  };

  private static RawImage _DecodeRaster(ReadOnlySpan<byte> body, int prefix, RasterKind kind) {
    if (prefix >= body.Length)
      throw new InvalidDataException("The OfficeArt raster BLIP ends before its picture begins.");

    return kind == RasterKind.Dib
      ? EmbeddedDibReader.DecodeHeaderless(body[prefix..])
      : EmbeddedPictureReader.Decode(body[prefix..]);
  }

  private static RawImage _DecodeMetafile(ReadOnlySpan<byte> body, int instance, MetafileKind kind) {
    var uidBytes = kind switch {
      MetafileKind.Emf when instance == 0x3D4 => 16,
      MetafileKind.Emf when instance == 0x3D5 => 32,
      MetafileKind.Wmf when instance == 0x216 => 16,
      MetafileKind.Wmf when instance == 0x217 => 32,
      MetafileKind.Pict when instance == 0x542 => 16,
      MetafileKind.Pict when instance == 0x543 => 32,
      _ => throw new InvalidDataException($"Unsupported OfficeArt metafile BLIP instance 0x{instance:X}."),
    };

    if (body.Length < uidBytes + _MetafileHeaderSize)
      throw new InvalidDataException("The OfficeArt metafile BLIP is truncated before its metafile header ends.");

    var header = body[uidBytes..];
    var decompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header);
    var storedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
    var compression = header[32];
    var payloadAt = uidBytes + _MetafileHeaderSize;
    if (storedSize > int.MaxValue || storedSize > body.Length - payloadAt)
      throw new InvalidDataException("The OfficeArt metafile BLIP states a payload longer than its record.");

    var stored = body.Slice(payloadAt, checked((int)storedSize));
    var metafile = compression switch {
      0xFE => stored.ToArray(),
      0x00 => _Inflate(stored, decompressedSize),
      _ => throw new InvalidDataException($"Unsupported OfficeArt metafile compression 0x{compression:X2}."),
    };

    return kind switch {
      MetafileKind.Emf => EmfFile.ToRawImage(EmfReader.FromSpan(metafile)),
      MetafileKind.Wmf => EmbeddedPictureReader.Decode(metafile),
      MetafileKind.Pict => _DecodePict(metafile),
      _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
  }

  private static byte[] _Inflate(ReadOnlySpan<byte> stored, uint expectedSize) {
    using var source = new MemoryStream(stored.ToArray(), false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress, false);
    using var target = expectedSize is > 0 and <= int.MaxValue
      ? new MemoryStream(checked((int)expectedSize))
      : new MemoryStream();
    zlib.CopyTo(target);
    var result = target.ToArray();
    if (expectedSize != 0 && result.LongLength != expectedSize)
      throw new InvalidDataException($"OfficeArt metafile expands to {result.Length} bytes, expected {expectedSize}.");
    return result;
  }

  private static RawImage _DecodePict(ReadOnlySpan<byte> data) {
    try {
      return PictFile.ToRawImage(PictReader.FromSpan(data));
    } catch (InvalidDataException) {
      // OfficeArt stores the QuickDraw picture itself, whereas PICT files commonly have a 512-byte
      // Macintosh file header. Add that non-picture wrapper when the direct form is what we got.
      var wrapped = new byte[PowerPointFile.ScanStart + data.Length];
      data.CopyTo(wrapped.AsSpan(PowerPointFile.ScanStart));
      return PictFile.ToRawImage(PictReader.FromSpan(wrapped));
    }
  }

  private enum RasterKind { Encoded, Dib }
  private enum MetafileKind { Emf, Wmf, Pict }
}
