using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Iff;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes IFF ANIM video: ILBM pictures plus the backwards-referenced delta methods defined by the
/// original ANIM specification and its op-6/op-7/op-8 addenda.
/// </summary>
/// <remarks>
/// ANIM does not have MPEG-style I/P/B pictures or forward references. An ANHD interleave names how
/// many already displayed pictures back a delta modifies; zero is the historical spelling of two.
/// Keeping display history directly models that rule, handles the quad-buffered method 6 naturally,
/// and avoids the common bug of resetting both references whenever a later operation-0 BODY appears.
/// <para/>
/// Implemented delta layouts are methods 2, 3, 5, 6, 7 and 8. Method 5 also recognises DPaint Anim
/// Brush's documented <c>bits == 4</c> XOR extension. Method 4 follows the specification's
/// <c>SetDLTAshort</c> grammar for the RLC form, including short/long data, set/XOR writes,
/// separate/shared info lists, horizontal/vertical traversal and 16/32-bit info fields. The ANHD names
/// a non-RLC method-4 variant but publishes no wire grammar for it, so that combination is refused
/// instead of inferred. Undefined option bits are refused as well. Method 74 remains impossible to
/// implement interoperably: its format was reserved with “details to be released later” and no
/// description was published.
/// <para/>
/// A BODY is stored in scanline-interleaved ILBM order, while every delta format addresses the Amiga
/// bitmap as separate contiguous planes. BODY data is therefore transposed once into plane-major state;
/// all deltas patch that state; conversion to chunky palette indices or HAM RGB happens only for output.
/// </remarks>
public sealed class AnimVideoDecoder : IVideoCodecDecoder<AnimVideoDecoder> {

  private const byte _OP_DIRECT = 0;
  private const byte _OP_XOR_ILBM = 1;
  private const byte _OP_LONG_DELTA = 2;
  private const byte _OP_SHORT_DELTA = 3;
  private const byte _OP_GENERAL_DELTA = 4;
  private const byte _OP_BYTE_VERTICAL_DELTA = 5;
  private const byte _OP_STEREO_BYTE_VERTICAL_DELTA = 6;
  private const byte _OP_VERTICAL_DELTA_7 = 7;
  private const byte _OP_VERTICAL_DELTA_8 = 8;
  private const int _HAM_FLAG = 0x0800;
  private const uint _ANIM_BRUSH_XOR_BITS = 4;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ANIM");

  private readonly List<byte[]> _history = [];
  private int _width, _height, _planes, _bytesPerRow, _planeSize;
  private byte _bodyCompression;
  private byte[] _palette = [];
  private bool _isHam;

  public static string CodecName => "IFF ANIM Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static AnimVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new();
  }

  private AnimVideoDecoder() { }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var span = packet.Data.Span;
    if (span.Length < 12 || !span[..4].SequenceEqual("FORM"u8) || !span.Slice(8, 4).SequenceEqual("ILBM"u8))
      throw new InvalidDataException("An IFF ANIM video packet does not open with 'FORM', a size, and 'ILBM'.");

    var iff = IffReader.FromSpan(span);
    byte[]? bmhd = null, anhd = null, cmap = null, camg = null, body = null, dlta = null;
    foreach (var chunk in iff.Chunks)
      switch (chunk.ChunkId.ToString()) {
        case "BMHD": bmhd = chunk.Data; break;
        case "ANHD": anhd = chunk.Data; break;
        case "CMAP": cmap = chunk.Data; break;
        case "CAMG": camg = chunk.Data; break;
        case "BODY": body = chunk.Data; break;
        case "DLTA": dlta = chunk.Data; break;
      }

    if (bmhd is not null)
      this._ReadBitmapHeader(bmhd);
    if (camg is { Length: >= 4 })
      this._isHam = (BinaryPrimitives.ReadUInt32BigEndian(camg) & _HAM_FLAG) != 0;
    if (cmap is not null)
      this._palette = cmap;

    var operation = anhd is { Length: >= 1 } ? anhd[0] : _OP_DIRECT;
    if (body is not null) {
      if (operation == _OP_DIRECT)
        this._DecodeDirectBody(body);
      else if (operation == _OP_XOR_ILBM)
        this._DecodeXorBody(body, anhd!);
      else
        throw new InvalidDataException($"IFF ANIM operation {operation} carries a BODY where this operation requires DLTA data.");
    } else if (dlta is not null)
      this._DecodeDelta(dlta, anhd);
    else if (operation == _OP_DIRECT && this._history.Count > 0)
      this._AppendFrame((byte[])this._history[^1].Clone());
    else
      throw new InvalidDataException("An IFF ANIM video packet carries neither a picture nor a delta.");

    if (this._history.Count == 0)
      throw new InvalidDataException("An IFF ANIM packet produced no picture and no earlier frame established one.");

    frame = this._BuildFrame(this._history[^1]);
    return true;
  }

  private void _ReadBitmapHeader(ReadOnlySpan<byte> bmhd) {
    if (bmhd.Length < 11)
      throw new InvalidDataException("An IFF ANIM BMHD chunk is shorter than the eleven bytes this decoder needs.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(bmhd);
    var height = BinaryPrimitives.ReadUInt16BigEndian(bmhd[2..]);
    var planes = bmhd[8];
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"An IFF ANIM BMHD chunk states a picture of {width}x{height}, which has no pixels.");
    if (planes is <= 0 or > 8)
      throw new InvalidDataException($"An IFF ANIM BMHD chunk states {planes} bitplanes; this decoder supports one through eight.");

    if (this._history.Count > 0 && (width != this._width || height != this._height || planes != this._planes))
      throw new InvalidDataException(
        $"IFF ANIM changed bitmap geometry/depth from {this._width}x{this._height}/{this._planes} to {width}x{height}/{planes}; "
        + "delta references cannot cross such a change safely.");

    this._width = width;
    this._height = height;
    this._planes = planes;
    this._bytesPerRow = checked((width + 15) / 16 * 2);
    this._planeSize = checked(this._bytesPerRow * height);
    this._bodyCompression = bmhd[10];
  }

  private void _DecodeDirectBody(byte[] body) {
    this._RequireGeometry("BODY");
    var interleaved = _UnpackBody(body, this._bodyCompression, checked(this._planes * this._planeSize));
    this._AppendFrame(_InterleavedToPlaneMajor(interleaved, this._planes, this._bytesPerRow, this._height));
  }

  private void _DecodeXorBody(byte[] body, byte[] anhd) {
    this._RequireGeometry("XOR BODY");
    if (anhd.Length < 20)
      throw new InvalidDataException("An IFF ANIM XOR frame has a truncated ANHD chunk.");

    var mask = anhd[1];
    var width = BinaryPrimitives.ReadUInt16BigEndian(anhd.AsSpan(2, 2));
    var height = BinaryPrimitives.ReadUInt16BigEndian(anhd.AsSpan(4, 2));
    var x = BinaryPrimitives.ReadInt16BigEndian(anhd.AsSpan(6, 2));
    var y = BinaryPrimitives.ReadInt16BigEndian(anhd.AsSpan(8, 2));
    var fullMask = (1 << this._planes) - 1;

    if ((mask != 0 && mask != fullMask) || (width != 0 && width != this._width) || (height != 0 && height != this._height) || x != 0 || y != 0)
      throw new NotSupportedException(
        "IFF ANIM operation 1 is decoded for the full-frame/all-plane form. Cropped or selectively masked XOR BODY frames need bit-level rectangle placement.");

    var reference = this._ReferenceFor(anhd[18]);
    var interleaved = _UnpackBody(body, this._bodyCompression, checked(this._planes * this._planeSize));
    var xor = _InterleavedToPlaneMajor(interleaved, this._planes, this._bytesPerRow, this._height);
    var target = (byte[])reference.Clone();
    for (var i = 0; i < target.Length; ++i)
      target[i] ^= xor[i];
    this._AppendFrame(target);
  }

  private void _DecodeDelta(byte[] dlta, byte[]? anhd) {
    this._RequireGeometry("DLTA");
    if (anhd is not { Length: >= 24 })
      throw new InvalidDataException("An IFF ANIM delta frame has no complete ANHD chunk.");

    var operation = anhd[0];
    var interleave = anhd[18];
    var bits = BinaryPrimitives.ReadUInt32BigEndian(anhd.AsSpan(20, 4));
    var mayStartFromBlack = operation is _OP_VERTICAL_DELTA_7 or _OP_VERTICAL_DELTA_8;
    var target = this._history.Count == 0 && mayStartFromBlack
      ? new byte[checked(this._planes * this._planeSize)]
      : (byte[])this._ReferenceFor(interleave)._Clone();

    switch (operation) {
      case _OP_LONG_DELTA:
      case _OP_SHORT_DELTA:
        if (bits != 0)
          throw new InvalidDataException($"IFF ANIM operation {operation} does not define ANHD option bits; received 0x{bits:x8}.");
        _ApplyLongOrShortDelta(target, dlta, this._planes, this._planeSize, operation == _OP_LONG_DELTA ? 4 : 2);
        break;

      case _OP_GENERAL_DELTA:
        _ApplyGeneralDelta(target, dlta, this._planes, this._bytesPerRow, this._planeSize, bits);
        break;

      case _OP_BYTE_VERTICAL_DELTA:
        if (bits is not (0 or _ANIM_BRUSH_XOR_BITS))
          throw new InvalidDataException(
            $"IFF ANIM method 5 defines zero option bits, apart from DPaint Anim Brush's documented XOR value 0x00000004; received 0x{bits:x8}.");
        _ApplyByteVerticalDelta(target, dlta, this._planes, this._bytesPerRow, this._planeSize, bits == _ANIM_BRUSH_XOR_BITS);
        break;

      case _OP_STEREO_BYTE_VERTICAL_DELTA:
        if (interleave != 4 || bits != 0)
          throw new InvalidDataException("IFF ANIM method 6 is method 5 with interleave exactly four and no option bits.");
        _ApplyByteVerticalDelta(target, dlta, this._planes, this._bytesPerRow, this._planeSize, xor: false);
        break;

      case _OP_VERTICAL_DELTA_7:
        if ((bits & ~1u) != 0)
          throw new InvalidDataException($"IFF ANIM method 7 uses only ANHD option bit zero; received 0x{bits:x8}.");
        _ApplyVerticalDelta7(target, dlta, this._planes, this._bytesPerRow, this._planeSize, (bits & 1) != 0 ? 4 : 2);
        break;

      case _OP_VERTICAL_DELTA_8:
        if ((bits & ~1u) != 0)
          throw new InvalidDataException($"IFF ANIM method 8 uses only ANHD option bit zero; received 0x{bits:x8}.");
        _ApplyVerticalDelta8(target, dlta, this._planes, this._bytesPerRow, this._planeSize, (bits & 1) != 0 ? 4 : 2);
        break;

      case 74:
        throw new NotSupportedException("IFF ANIM operation 74 ('J') has no published wire-format description.");
      default:
        throw new NotSupportedException($"IFF ANIM operation {operation} is not defined by the supported ANIM specifications.");
    }

    this._AppendFrame(target);
  }

  private byte[] _ReferenceFor(byte interleave) {
    if (this._history.Count == 0)
      throw new InvalidDataException("An IFF ANIM delta arrived before any reference picture was established.");

    var framesBack = interleave == 0 ? 2 : interleave;
    if (framesBack <= 0)
      framesBack = 2;
    return this._history.Count < framesBack ? this._history[0] : this._history[^framesBack];
  }

  private void _AppendFrame(byte[] frame) {
    this._history.Add(frame);
    if (this._history.Count > byte.MaxValue)
      this._history.RemoveAt(0);
  }

  private void _RequireGeometry(string chunk) {
    if (this._width <= 0 || this._height <= 0 || this._planes <= 0)
      throw new InvalidDataException($"An IFF ANIM {chunk} arrived before BMHD established picture geometry.");
  }

  private static byte[] _UnpackBody(byte[] body, int compression, int expectedSize) {
    if (compression == 0) {
      if (body.Length < expectedSize)
        throw new InvalidDataException($"An uncompressed IFF ANIM BODY is {body.Length} bytes; {expectedSize} are required.");
      return body.AsSpan(0, expectedSize).ToArray();
    }
    if (compression != 1)
      throw new InvalidDataException($"IFF ANIM BMHD BODY compression {compression} is not uncompressed or ByteRun1.");

    var result = new byte[expectedSize];
    var at = 0;
    var outAt = 0;
    while (outAt < expectedSize) {
      if (at >= body.Length)
        throw new InvalidDataException("An IFF ANIM ByteRun1 BODY ended before the picture was complete.");
      var control = unchecked((sbyte)body[at++]);
      if (control >= 0) {
        var count = control + 1;
        if (at + count > body.Length || outAt + count > expectedSize)
          throw new InvalidDataException("An IFF ANIM ByteRun1 literal run exceeds its input or output buffer.");
        body.AsSpan(at, count).CopyTo(result.AsSpan(outAt));
        at += count;
        outAt += count;
      } else if (control != -128) {
        var count = 1 - control;
        if (at >= body.Length || outAt + count > expectedSize)
          throw new InvalidDataException("An IFF ANIM ByteRun1 repeat run exceeds its input or output buffer.");
        result.AsSpan(outAt, count).Fill(body[at++]);
        outAt += count;
      }
    }
    return result;
  }

  private static byte[] _InterleavedToPlaneMajor(byte[] interleaved, int planes, int bytesPerRow, int height) {
    var planeSize = checked(bytesPerRow * height);
    var result = new byte[checked(planeSize * planes)];
    var scanlineBytes = checked(bytesPerRow * planes);
    for (var y = 0; y < height; ++y)
      for (var p = 0; p < planes; ++p)
        Array.Copy(interleaved, y * scanlineBytes + p * bytesPerRow, result, p * planeSize + y * bytesPerRow, bytesPerRow);
    return result;
  }

  private static void _ApplyLongOrShortDelta(byte[] buffer, byte[] dlta, int planes, int planeSize, int itemSize) {
    if (dlta.Length < 32)
      throw new InvalidDataException("IFF ANIM methods 2/3 require an eight-pointer (32-byte) DLTA table.");

    for (var plane = 0; plane < planes; ++plane) {
      var pos = _Pointer(dlta, plane, 32);
      if (pos == 0)
        continue;
      var cursor = 0;
      var terminated = false;
      while (pos + 2 <= dlta.Length) {
        var rawOffset = BinaryPrimitives.ReadUInt16BigEndian(dlta.AsSpan(pos, 2));
        pos += 2;
        if (rawOffset == ushort.MaxValue) {
          terminated = true;
          break;
        }

        var offset = unchecked((short)rawOffset);
        if (offset >= 0) {
          cursor = checked(cursor + offset);
          _CopyDeltaItem(buffer, plane * planeSize, planeSize, cursor, itemSize, dlta, ref pos, xor: false);
        } else {
          cursor = checked(cursor + (-offset - 2));
          if (pos + 2 > dlta.Length)
            throw new InvalidDataException("IFF ANIM methods 2/3 run group is missing its count.");
          var count = BinaryPrimitives.ReadUInt16BigEndian(dlta.AsSpan(pos, 2));
          pos += 2;
          if (count == 0)
            throw new InvalidDataException("IFF ANIM methods 2/3 run group has a zero item count.");
          for (var i = 0; i < count; ++i)
            _CopyDeltaItem(buffer, plane * planeSize, planeSize, cursor + i, itemSize, dlta, ref pos, xor: false);
          cursor = checked(cursor + count - 1);
        }
      }
      if (!terminated)
        throw new InvalidDataException($"IFF ANIM method {(itemSize == 4 ? 2 : 3)} plane {plane} has no 0xffff terminator.");
    }
  }

  private static void _ApplyGeneralDelta(byte[] buffer, byte[] dlta, int planes, int bytesPerRow, int planeSize, uint bits) {
    if (dlta.Length < 64)
      throw new InvalidDataException("IFF ANIM method 4 requires a sixteen-pointer (64-byte) DLTA table.");
    if ((bits & ~0x3Fu) != 0)
      throw new InvalidDataException($"IFF ANIM method 4 has undefined option bits set: 0x{bits:x8}.");

    var itemSize = (bits & 1) != 0 ? 4 : 2;
    var xor = (bits & 2) != 0;
    // Bit 2 selects one shared info list rather than one per plane. The wire representation needs no
    // special branch: the specification writes the shared pointer into every applicable pointer slot,
    // and even explicitly permits arbitrary subsets of planes to share one list.
    var runLengthCoded = (bits & 8) != 0;
    var vertical = (bits & 16) != 0;
    var infoSize = (bits & 32) != 0 ? 4 : 2;

    if (!runLengthCoded)
      throw new NotSupportedException(
        $"IFF ANIM method 4 states the non-RLC variant (bits 0x{bits:x8}), but the published format defines no wire grammar for it.");
    if (vertical && bytesPerRow % itemSize != 0)
      throw new NotSupportedException(
        $"IFF ANIM method 4 vertical long-data traversal requires the {bytesPerRow}-byte bitplane row width to be divisible by {itemSize}.");

    var destinationStep = vertical ? bytesPerRow : itemSize;
    var terminator = infoSize == 4 ? uint.MaxValue : ushort.MaxValue;

    for (var plane = 0; plane < planes; ++plane) {
      // SetDLTAshort receives a WORD* and adds these LONG table values to that pointer. The table
      // values therefore remain 16-bit-WORD offsets even when data words or info fields themselves
      // are LONGs.
      var dataWords = BinaryPrimitives.ReadUInt32BigEndian(dlta.AsSpan(plane * 4, 4));
      var infoWords = BinaryPrimitives.ReadUInt32BigEndian(dlta.AsSpan((plane + 8) * 4, 4));
      if (dataWords == 0 && infoWords == 0)
        continue;
      if (dataWords == 0 || infoWords == 0)
        throw new InvalidDataException("IFF ANIM method 4 has only one of its data/info pointers for a changed plane.");

      var dataPos64 = (ulong)dataWords * 2;
      var infoPos64 = (ulong)infoWords * 2;
      if (dataPos64 > (ulong)dlta.Length || infoPos64 >= (ulong)dlta.Length)
        throw new InvalidDataException("IFF ANIM method 4 points outside its DLTA chunk.");
      var dataPos = checked((int)dataPos64);
      var infoPos = checked((int)infoPos64);

      while (true) {
        var offset = _ReadUnsigned(dlta, ref infoPos, infoSize, "method 4 info offset");
        if (offset == terminator)
          break;

        var rawSize = _ReadUnsigned(dlta, ref infoPos, infoSize, "method 4 info run size");
        long size = infoSize == 2
          ? unchecked((short)rawSize)
          : unchecked((int)rawSize);
        if (size == 0)
          continue;

        var count64 = size < 0 ? -size : size;
        if (count64 > int.MaxValue)
          throw new InvalidDataException("IFF ANIM method 4 run is too large to represent safely.");
        var count = (int)count64;

        // In the long-data variant the analogous playback routine uses LONG* planeptr/dest, so the
        // absolute per-op offset counts data items: WORDs in short-data mode, LONGs in long-data mode.
        var destination64 = (ulong)offset * (uint)itemSize;
        if (destination64 > int.MaxValue)
          throw new InvalidDataException("IFF ANIM method 4 destination offset is too large to represent safely.");
        var destination = (int)destination64;

        if (size < 0) {
          var valuePos = dataPos;
          _RequireBytes(dlta, valuePos, itemSize, "method 4 repeated value");
          for (var i = 0; i < count; ++i)
            _CopyRawItem(
              buffer,
              plane * planeSize,
              planeSize,
              checked(destination + i * destinationStep),
              itemSize,
              dlta,
              valuePos,
              xor);
          dataPos += itemSize;
        } else {
          for (var i = 0; i < count; ++i) {
            _CopyRawItem(
              buffer,
              plane * planeSize,
              planeSize,
              checked(destination + i * destinationStep),
              itemSize,
              dlta,
              dataPos,
              xor);
            dataPos += itemSize;
          }
        }
      }
    }
  }

  private static void _ApplyByteVerticalDelta(byte[] buffer, byte[] dlta, int planes, int bytesPerRow, int planeSize, bool xor) {
    if (dlta.Length < 32)
      throw new InvalidDataException("IFF ANIM method 5/6 DLTA is shorter than its plane pointer table.");

    var height = planeSize / bytesPerRow;
    for (var plane = 0; plane < planes; ++plane) {
      var pos = _Pointer(dlta, plane, 32);
      if (pos == 0)
        continue;
      var planeOffset = plane * planeSize;

      for (var column = 0; column < bytesPerRow; ++column) {
        _RequireBytes(dlta, pos, 1, "method 5 column op-count");
        var opCount = dlta[pos++];
        var row = 0;
        for (var op = 0; op < opCount; ++op) {
          _RequireBytes(dlta, pos, 1, "method 5 opcode");
          var code = dlta[pos++];
          if (code == 0) {
            _RequireBytes(dlta, pos, 2, "method 5 same-run");
            var count = dlta[pos++];
            var value = dlta[pos++];
            _RequireRows(row, count, height, "method 5 same-run");
            for (var i = 0; i < count; ++i)
              _StoreByte(buffer, planeOffset + (row + i) * bytesPerRow + column, value, xor);
            row += count;
          } else if ((code & 0x80) != 0) {
            var count = code & 0x7F;
            _RequireRows(row, count, height, "method 5 unique-run");
            _RequireBytes(dlta, pos, count, "method 5 unique-run data");
            for (var i = 0; i < count; ++i)
              _StoreByte(buffer, planeOffset + (row + i) * bytesPerRow + column, dlta[pos++], xor);
            row += count;
          } else {
            _RequireRows(row, code, height, "method 5 skip");
            row += code;
          }
        }
      }
    }
  }

  private static void _ApplyVerticalDelta7(byte[] buffer, byte[] dlta, int planes, int bytesPerRow, int planeSize, int itemSize) {
    if (dlta.Length < 64)
      throw new InvalidDataException("IFF ANIM method 7 requires sixteen DLTA pointers.");
    if (bytesPerRow % itemSize != 0)
      throw new NotSupportedException("IFF ANIM method 7 long data requires the bitplane row width to be divisible by four bytes.");

    var height = planeSize / bytesPerRow;
    var columns = bytesPerRow / itemSize;
    for (var plane = 0; plane < planes; ++plane) {
      var opPos = _Pointer(dlta, plane, 64);
      var dataPos = _Pointer(dlta, plane + 8, 64);
      if (opPos == 0 && dataPos == 0)
        continue;
      if (opPos == 0 || dataPos == 0)
        throw new InvalidDataException("IFF ANIM method 7 has only one of its opcode/data pointers for a plane.");

      var planeOffset = plane * planeSize;
      for (var column = 0; column < columns; ++column) {
        _RequireBytes(dlta, opPos, 1, "method 7 column op-count");
        var opCount = dlta[opPos++];
        var row = 0;
        for (var op = 0; op < opCount; ++op) {
          _RequireBytes(dlta, opPos, 1, "method 7 opcode");
          var code = dlta[opPos++];
          if (code == 0) {
            _RequireBytes(dlta, opPos, 1, "method 7 same-run count");
            var count = dlta[opPos++];
            _RequireRows(row, count, height, "method 7 same-run");
            _RequireBytes(dlta, dataPos, itemSize, "method 7 same-run data");
            for (var i = 0; i < count; ++i)
              _CopyRawItem(buffer, planeOffset, planeSize, (row + i) * bytesPerRow + column * itemSize, itemSize, dlta, dataPos, xor: false);
            dataPos += itemSize;
            row += count;
          } else if ((code & 0x80) != 0) {
            var count = code & 0x7F;
            _RequireRows(row, count, height, "method 7 unique-run");
            for (var i = 0; i < count; ++i) {
              _CopyRawItem(buffer, planeOffset, planeSize, (row + i) * bytesPerRow + column * itemSize, itemSize, dlta, dataPos, xor: false);
              dataPos += itemSize;
            }
            row += count;
          } else {
            _RequireRows(row, code, height, "method 7 skip");
            row += code;
          }
        }
      }
    }
  }

  private static void _ApplyVerticalDelta8(byte[] buffer, byte[] dlta, int planes, int bytesPerRow, int planeSize, int requestedItemSize) {
    if (dlta.Length < 64)
      throw new InvalidDataException("IFF ANIM method 8 requires sixteen DLTA pointers.");

    var height = planeSize / bytesPerRow;
    for (var plane = 0; plane < planes; ++plane) {
      var pos = _Pointer(dlta, plane, 64);
      if (pos == 0)
        continue;
      var planeOffset = plane * planeSize;
      var fullLongColumns = requestedItemSize == 4 ? bytesPerRow / 4 : 0;
      var columns = requestedItemSize == 4 ? fullLongColumns + (bytesPerRow % 4 == 0 ? 0 : 1) : bytesPerRow / 2;

      for (var column = 0; column < columns; ++column) {
        var itemSize = requestedItemSize == 4 && column == fullLongColumns && bytesPerRow % 4 != 0 ? 2 : requestedItemSize;
        var columnOffset = requestedItemSize == 4 ? Math.Min(column * 4, bytesPerRow - itemSize) : column * 2;
        var opCount = _ReadUnsigned(dlta, ref pos, itemSize, "method 8 column op-count");
        if (opCount > int.MaxValue)
          throw new InvalidDataException("IFF ANIM method 8 column has more operations than can be represented safely.");
        var row = 0;
        var signBit = itemSize == 2 ? 0x8000u : 0x80000000u;

        for (var op = 0; op < (int)opCount; ++op) {
          var code = _ReadUnsigned(dlta, ref pos, itemSize, "method 8 opcode");
          if (code == 0) {
            var count = _ReadUnsigned(dlta, ref pos, itemSize, "method 8 same-run count");
            if (count > int.MaxValue)
              throw new InvalidDataException("IFF ANIM method 8 same-run count is too large.");
            _RequireRows(row, (int)count, height, "method 8 same-run");
            var valuePos = pos;
            _RequireBytes(dlta, valuePos, itemSize, "method 8 same-run data");
            for (var i = 0; i < (int)count; ++i)
              _CopyRawItem(buffer, planeOffset, planeSize, (row + i) * bytesPerRow + columnOffset, itemSize, dlta, valuePos, xor: false);
            pos += itemSize;
            row += (int)count;
          } else if ((code & signBit) != 0) {
            var count = code & (signBit - 1);
            if (count > int.MaxValue)
              throw new InvalidDataException("IFF ANIM method 8 unique-run count is too large.");
            _RequireRows(row, (int)count, height, "method 8 unique-run");
            for (var i = 0; i < (int)count; ++i) {
              _CopyRawItem(buffer, planeOffset, planeSize, (row + i) * bytesPerRow + columnOffset, itemSize, dlta, pos, xor: false);
              pos += itemSize;
            }
            row += (int)count;
          } else {
            if (code > int.MaxValue)
              throw new InvalidDataException("IFF ANIM method 8 skip count is too large.");
            _RequireRows(row, (int)code, height, "method 8 skip");
            row += (int)code;
          }
        }
      }
    }
  }

  private static int _Pointer(byte[] dlta, int slot, int minimumTableSize) {
    if (dlta.Length < minimumTableSize || slot * 4 + 4 > dlta.Length)
      throw new InvalidDataException("IFF ANIM DLTA pointer table is truncated.");
    var pointer = BinaryPrimitives.ReadUInt32BigEndian(dlta.AsSpan(slot * 4, 4));
    if (pointer == 0)
      return 0;
    if (pointer >= dlta.Length)
      throw new InvalidDataException($"IFF ANIM DLTA pointer {slot} names offset {pointer}, outside the {dlta.Length}-byte chunk.");
    return checked((int)pointer);
  }

  private static void _CopyDeltaItem(
    byte[] buffer,
    int planeOffset,
    int planeSize,
    int itemIndex,
    int itemSize,
    byte[] dlta,
    ref int source,
    bool xor) {
    var destination = checked(itemIndex * itemSize);
    _CopyRawItem(buffer, planeOffset, planeSize, destination, itemSize, dlta, source, xor);
    source += itemSize;
  }

  private static void _CopyRawItem(
    byte[] buffer,
    int planeOffset,
    int planeSize,
    int destination,
    int itemSize,
    byte[] source,
    int sourceOffset,
    bool xor) {
    if (destination < 0 || destination > planeSize - itemSize)
      throw new InvalidDataException("IFF ANIM delta writes outside its destination bitplane.");
    _RequireBytes(source, sourceOffset, itemSize, "delta item data");
    for (var i = 0; i < itemSize; ++i) {
      var target = planeOffset + destination + i;
      if (xor)
        buffer[target] ^= source[sourceOffset + i];
      else
        buffer[target] = source[sourceOffset + i];
    }
  }

  private static uint _ReadUnsigned(byte[] data, ref int pos, int size, string what) {
    _RequireBytes(data, pos, size, what);
    uint result = size == 2
      ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2))
      : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
    pos += size;
    return result;
  }

  private static void _StoreByte(byte[] buffer, int destination, byte value, bool xor) {
    if (xor)
      buffer[destination] ^= value;
    else
      buffer[destination] = value;
  }

  private static void _RequireRows(int row, int count, int height, string what) {
    if (count < 0 || row < 0 || row > height || count > height - row)
      throw new InvalidDataException($"IFF ANIM {what} runs past the bottom of the picture.");
  }

  private static void _RequireBytes(byte[] data, int offset, int count, string what) {
    if (offset < 0 || count < 0 || offset > data.Length - count)
      throw new InvalidDataException($"IFF ANIM {what} runs outside the coded chunk.");
  }

  private RawImage _BuildFrame(byte[] planeMajor) {
    if (this._isHam)
      return new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = _DecodeHam(planeMajor, this._palette, this._width, this._height, this._planes, this._bytesPerRow),
      };

    var indices = _PlaneMajorToChunky(planeMajor, this._width, this._height, this._planes, this._bytesPerRow);
    var paletteCount = this._palette.Length / 3;
    foreach (var index in indices)
      if (index >= paletteCount)
        throw new InvalidDataException(
          $"An IFF ANIM pixel names palette index {index}, which the {paletteCount}-entry palette does not have.");

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = this._palette,
      PaletteCount = paletteCount,
    };
  }

  private static byte[] _PlaneMajorToChunky(byte[] planeMajor, int width, int height, int planes, int bytesPerRow) {
    var result = new byte[checked(width * height)];
    var planeSize = checked(bytesPerRow * height);
    for (var p = 0; p < planes; ++p) {
      var planeOffset = p * planeSize;
      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerRow;
        var outRowOffset = y * width;
        for (var x = 0; x < width; ++x)
          if (((planeMajor[rowOffset + (x >> 3)] >> (7 - (x & 7))) & 1) != 0)
            result[outRowOffset + x] |= (byte)(1 << p);
      }
    }
    return result;
  }

  private static byte[] _DecodeHam(byte[] planeMajor, byte[] palette, int width, int height, int planes, int bytesPerRow) {
    var result = new byte[checked(width * height * 3)];
    var controlBits = planes - 2;
    if (controlBits is not (4 or 6))
      throw new InvalidDataException($"IFF ANIM HAM requires six or eight bitplanes; received {planes}.");
    var controlMask = (1 << controlBits) - 1;
    var shift = 8 - controlBits;
    var paletteCount = palette.Length / 3;
    var indices = _PlaneMajorToChunky(planeMajor, width, height, planes, bytesPerRow);
    byte bgR = paletteCount > 0 ? palette[0] : (byte)0;
    byte bgG = paletteCount > 0 ? palette[1] : (byte)0;
    byte bgB = paletteCount > 0 ? palette[2] : (byte)0;

    for (var y = 0; y < height; ++y) {
      byte r = bgR, g = bgG, b = bgB;
      var rowOffset = y * width;
      for (var x = 0; x < width; ++x) {
        var value = indices[rowOffset + x];
        var control = value >> controlBits;
        var low = value & controlMask;
        var widened = (byte)((low << shift | low >> (controlBits - shift)) & 0xFF);
        switch (control) {
          case 0:
            if (low >= paletteCount)
              throw new InvalidDataException($"IFF ANIM HAM palette index {low} exceeds its {paletteCount}-entry palette.");
            var o = low * 3;
            r = palette[o];
            g = palette[o + 1];
            b = palette[o + 2];
            break;
          case 1: b = widened; break;
          case 2: r = widened; break;
          case 3: g = widened; break;
        }
        var output = (rowOffset + x) * 3;
        result[output] = r;
        result[output + 1] = g;
        result[output + 2] = b;
      }
    }
    return result;
  }
}

internal static class AnimByteArrayExtensions {
  internal static object _Clone(this byte[] value) => value.Clone();
}
