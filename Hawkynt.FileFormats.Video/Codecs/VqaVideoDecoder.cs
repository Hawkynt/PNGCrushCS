using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Vqa;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Westwood VQA video (<c>WSVQ</c>): the version-1 and version-2 palettised vector formats and
/// the version-2/version-3 15-bit HiColor delta format.
/// </summary>
/// <remarks>
/// The old format is intra-picture vector quantisation with persistent palette/codebook state. Version
/// 1 stores one little-endian pointer word per block; version 2 splits all low bytes from all high bytes
/// before format80 compression. Partial codebooks are accumulated for the number of frames stated by
/// <c>VQHD.CBParts</c> and become current only after the picture carrying the final part has been drawn.
/// <para/>
/// HiColor VQA is genuinely inter-frame. A VPTR/VPRZ stream walks every block row and can leave blocks
/// untouched, so those blocks reference the previous decoded picture. Other commands copy one or more
/// 15-bit codebook vectors, and Blade Runner's overlay commands additionally leave individual pixels
/// untouched when the vector pixel's high alpha bit is set. There are no future-picture references or
/// B-frames in the published format.
/// <para/>
/// This implementation follows Gordan Ugarkovic's VQA_INFO/HC_VQA descriptions mirrored by
/// MultimediaWiki and is cross-checked against FFmpeg's LGPL VQA decoder. No implementation code is
/// copied; the syntax and state transitions are implemented directly from the published bitstream
/// description.
/// </remarks>
public sealed class VqaVideoDecoder : IVideoCodecDecoder<VqaVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WSVQ");

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _PALETTE_ENTRIES = 256;
  private const int _PALETTE_BYTES = _PALETTE_ENTRIES * 3;
  private const int _VERSION_1 = 1;
  private const int _VERSION_2 = 2;
  private const int _VERSION_3 = 3;
  private const int _HICOLOR_MAX_VECTOR_INDEX = 0x1fff;

  private readonly int _version;
  private readonly bool _isHighColour;
  private readonly int _width;
  private readonly int _height;
  private readonly int _blockWidth;
  private readonly int _blockHeight;
  private readonly int _blocksWide;
  private readonly int _blocksHigh;
  private readonly int _codebookParts;

  private byte[] _codebook = [];
  private byte[]? _pendingCodebook;
  private readonly List<byte[]> _codebookPieces = [];
  private bool? _codebookPiecesCompressed;
  private readonly byte[] _palette = new byte[_PALETTE_BYTES];
  private readonly ushort[]? _highColourPixels;

  public static string CodecName => "Westwood VQA Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static VqaVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var header = stream.CodecPrivateData.Span;
    if (header.Length < 42)
      throw new InvalidDataException($"A VQA video stream carries {header.Length} bytes of header, short of the forty-two a VQHD payload needs.");

    var version = BinaryPrimitives.ReadUInt16LittleEndian(header);
    if (version is < _VERSION_1 or > _VERSION_3)
      throw new NotSupportedException($"This VQA stream states format version {version}; versions 1 through 3 are defined.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"A VQA video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    var blockWidth = header[10];
    var blockHeight = header[11];
    if (blockWidth != 4 || blockHeight is not (2 or 4))
      throw new NotSupportedException(
        $"This VQA stream uses {blockWidth}x{blockHeight} vectors. The published format and reference decoder define 4x2 and 4x4 vectors.");
    if (stream.Width % blockWidth != 0 || stream.Height % blockHeight != 0)
      throw new NotSupportedException(
        $"This VQA stream's {stream.Width}x{stream.Height} picture is not an exact number of {blockWidth}x{blockHeight} blocks.");

    var colours = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
    var isHighColour = colours == 0;
    if (version == _VERSION_3 && !isHighColour)
      throw new InvalidDataException("A version-3 VQA stream declares a palette even though VQA3 is the 15-bit HiColor form.");
    if (version == _VERSION_1 && isHighColour)
      throw new NotSupportedException("No published version-1 HiColor VQA syntax exists; version 1 is the original palettised format.");

    var codebookParts = header[13];
    return new(version, isHighColour, stream.Width, stream.Height, blockWidth, blockHeight, codebookParts);
  }

  private VqaVideoDecoder(int version, bool isHighColour, int width, int height, int blockWidth, int blockHeight, int codebookParts) {
    this._version = version;
    this._isHighColour = isHighColour;
    this._width = width;
    this._height = height;
    this._blockWidth = blockWidth;
    this._blockHeight = blockHeight;
    this._blocksWide = width / blockWidth;
    this._blocksHigh = height / blockHeight;
    this._codebookParts = codebookParts;
    if (isHighColour)
      this._highColourPixels = new ushort[checked(width * height)];
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = this._isHighColour ? this._DecodeHighColour(packet.Data.Span) : this._DecodePaletted(packet.Data.Span);
    return true;
  }

  // ============================================================================================
  // Version 1 / 2 palettised VQA
  // ============================================================================================

  private RawImage _DecodePaletted(ReadOnlySpan<byte> data) {
    if (this._pendingCodebook != null) {
      this._codebook = this._pendingCodebook;
      this._pendingCodebook = null;
    }

    byte[]? table = null;
    foreach (var chunk in _Chunks(data)) {
      var id = chunk.Id;
      var kind = id[..3];
      var compression = id[3];

      if (kind.SequenceEqual("CBF"u8)) {
        this._codebook = _DecodeVariable(chunk.Payload, compression);
        this._codebookPieces.Clear();
        this._codebookPiecesCompressed = null;
      } else if (kind.SequenceEqual("CBP"u8)) {
        this._AccumulateCodebookPiece(chunk.Payload, compression);
      } else if (kind.SequenceEqual("CPL"u8)) {
        this._ReadPalette(chunk.Payload, compression);
      } else if (kind.SequenceEqual("VPT"u8)) {
        table = _DecodeFixed(
          chunk.Payload,
          compression,
          checked(this._blocksWide * this._blocksHigh * 2),
          "a VPT? index table");
      }
    }

    if (table == null)
      throw new InvalidDataException("A palettised VQA picture carries no VPT? index table.");

    return this._BuildPalettedPicture(table);
  }

  private void _AccumulateCodebookPiece(ReadOnlySpan<byte> payload, byte compression) {
    if (this._codebookParts == 0)
      throw new NotSupportedException(
        "This palettised VQA stream uses CBP? chunks while VQHD.CBParts is zero. The format then requires FINF/CIND scheduling metadata that is not part of the coded picture packet.");

    var compressed = compression == (byte)'Z';
    if (compression is not ((byte)'0') and not ((byte)'Z'))
      throw new InvalidDataException($"A CBP? chunk uses unknown compression marker 0x{compression:X2}.");
    if (this._codebookPiecesCompressed is { } previous && previous != compressed)
      throw new InvalidDataException("One partial VQA codebook mixes compressed and uncompressed CBP chunks.");

    this._codebookPiecesCompressed = compressed;
    this._codebookPieces.Add(payload.ToArray());
    if (this._codebookPieces.Count < this._codebookParts)
      return;
    if (this._codebookPieces.Count > this._codebookParts)
      throw new InvalidDataException($"A partial VQA codebook received more than its declared {this._codebookParts} pieces.");

    var combinedLength = 0;
    foreach (var piece in this._codebookPieces)
      combinedLength = checked(combinedLength + piece.Length);

    var combined = new byte[combinedLength];
    var offset = 0;
    foreach (var piece in this._codebookPieces) {
      piece.CopyTo(combined.AsSpan(offset));
      offset += piece.Length;
    }

    this._pendingCodebook = compressed ? VqaFormat80.Decompress(combined) : combined;
    this._codebookPieces.Clear();
    this._codebookPiecesCompressed = null;
  }

  private static byte[] _DecodeVariable(ReadOnlySpan<byte> payload, byte compression)
    => compression switch {
      (byte)'Z' => VqaFormat80.Decompress(payload),
      (byte)'0' => payload.ToArray(),
      _ => throw new InvalidDataException($"A VQA sub-chunk uses unknown compression marker 0x{compression:X2}."),
    };

  private static byte[] _DecodeFixed(ReadOnlySpan<byte> payload, byte compression, int length, string description) {
    if (compression == (byte)'Z')
      return VqaFormat80.Decompress(payload, length);
    if (compression != (byte)'0')
      throw new InvalidDataException($"A VQA sub-chunk uses unknown compression marker 0x{compression:X2}.");
    if (payload.Length < length)
      throw new InvalidDataException(
        $"This picture's {description} needs {length} uncompressed bytes but its chunk carries only {payload.Length}.");

    return payload[..length].ToArray();
  }

  private void _ReadPalette(ReadOnlySpan<byte> payload, byte compression) {
    var colours = _DecodeVariable(payload, compression);
    var count = Math.Min(colours.Length, _PALETTE_BYTES);
    for (var i = 0; i < count; ++i)
      this._palette[i] = ChannelScaling.Expand6(colours[i] & 0x3F);
  }

  private RawImage _BuildPalettedPicture(byte[] table) {
    var indices = new byte[checked(this._width * this._height)];
    var blockArea = this._blockWidth * this._blockHeight;
    var codebookEntries = this._codebook.Length / blockArea;
    var blockCount = this._blocksWide * this._blocksHigh;
    var solidSentinel = this._blockHeight == 4 ? 0xff : 0x0f;

    for (var block = 0; block < blockCount; ++block) {
      int entry;
      byte colour;
      bool solid;

      if (this._version == _VERSION_1) {
        var lo = table[block * 2];
        var hi = table[block * 2 + 1];
        solid = hi == 0xff;
        colour = (byte)(255 - lo);
        entry = ((hi << 8) | lo) >> 3;
      } else {
        var lo = table[block];
        var hi = table[blockCount + block];
        solid = hi == solidSentinel;
        colour = lo;
        entry = (hi << 8) | lo;
      }

      var bx = block % this._blocksWide;
      var by = block / this._blocksWide;
      if (solid) {
        this._FillPalettedBlock(indices, bx, by, colour);
        continue;
      }

      if ((uint)entry >= (uint)codebookEntries)
        throw new InvalidDataException(
          $"A VQA index table names codebook entry {entry}, but the current codebook holds only {codebookEntries}.");

      var entryOffset = entry * blockArea;
      for (var yy = 0; yy < this._blockHeight; ++yy) {
        var rowStart = (by * this._blockHeight + yy) * this._width + bx * this._blockWidth;
        this._codebook.AsSpan(entryOffset + yy * this._blockWidth, this._blockWidth)
          .CopyTo(indices.AsSpan(rowStart, this._blockWidth));
      }
    }

    var palette = new byte[_PALETTE_BYTES];
    Array.Copy(this._palette, palette, _PALETTE_BYTES);
    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = palette,
      PaletteCount = _PALETTE_ENTRIES,
    };
  }

  private void _FillPalettedBlock(Span<byte> indices, int bx, int by, byte colour) {
    for (var yy = 0; yy < this._blockHeight; ++yy) {
      var rowStart = (by * this._blockHeight + yy) * this._width + bx * this._blockWidth;
      indices.Slice(rowStart, this._blockWidth).Fill(colour);
    }
  }

  // ============================================================================================
  // Version 2 / 3 HiColor VQA
  // ============================================================================================

  private RawImage _DecodeHighColour(ReadOnlySpan<byte> data) {
    byte[]? pointers = null;

    foreach (var chunk in _Chunks(data)) {
      if (chunk.Id.SequenceEqual("CBF0"u8))
        this._codebook = chunk.Payload.ToArray();
      else if (chunk.Id.SequenceEqual("CBFZ"u8))
        this._codebook = _DecodeHighColourCodebook(chunk.Payload);
      else if (chunk.Id.SequenceEqual("VPTR"u8))
        pointers = chunk.Payload.ToArray();
      else if (chunk.Id.SequenceEqual("VPRZ"u8))
        pointers = VqaFormat80.Decompress(chunk.Payload);
    }

    if (pointers == null)
      throw new InvalidDataException("A HiColor VQA picture carries neither VPTR nor VPRZ block data.");

    this._ApplyHighColourPointers(pointers);
    return this._BuildHighColourPicture();
  }

  private static byte[] _DecodeHighColourCodebook(ReadOnlySpan<byte> payload) {
    if (payload.IsEmpty)
      return [];

    // HC_VQA: a leading zero selects the modified format80 whose long references are relative. Any
    // other first byte is the first command byte of an ordinary format80 stream.
    return payload[0] == 0
      ? VqaFormat80.DecompressRelative(payload[1..])
      : VqaFormat80.Decompress(payload);
  }

  private void _ApplyHighColourPointers(ReadOnlySpan<byte> pointers) {
    var at = 0;
    for (var by = 0; by < this._blocksHigh; ++by) {
      var bx = 0;
      while (bx < this._blocksWide) {
        if (at + 2 > pointers.Length)
          throw new InvalidDataException($"A HiColor VPTR stream ends in block row {by} before the row is complete.");

        var raw = BinaryPrimitives.ReadUInt16LittleEndian(pointers[at..]);
        at += 2;
        var type = raw >> 13;
        var value = raw & _HICOLOR_MAX_VECTOR_INDEX;

        switch (type) {
          case 0: {
            var count = value;
            if (count == 0)
              throw new InvalidDataException("A HiColor VPTR skip command has a zero block count and would make no progress.");
            _CheckBlockCount(count, this._blocksWide - bx, by, type);
            bx += count;
            break;
          }

          case 1: {
            var count = (((value >> 8) & 0x1f) + 1) * 2;
            _CheckBlockCount(count, this._blocksWide - bx, by, type);
            var entry = value & 0xff;
            for (var i = 0; i < count; ++i)
              this._DrawHighColourVector(entry, bx++, by, skipAlpha: false);
            break;
          }

          case 2: {
            var following = (((value >> 8) & 0x1f) + 1) * 2;
            var count = following + 1;
            _CheckBlockCount(count, this._blocksWide - bx, by, type);
            this._DrawHighColourVector(value & 0xff, bx++, by, skipAlpha: false);
            for (var i = 0; i < following; ++i) {
              if (at >= pointers.Length)
                throw new InvalidDataException("A HiColor VPTR type-2 command ends before all following one-byte vector indices are present.");
              this._DrawHighColourVector(pointers[at++], bx++, by, skipAlpha: false);
            }
            break;
          }

          case 3:
          case 4:
            _CheckBlockCount(1, this._blocksWide - bx, by, type);
            this._DrawHighColourVector(value, bx++, by, skipAlpha: type == 4);
            break;

          case 5:
          case 6: {
            if (at >= pointers.Length)
              throw new InvalidDataException($"A HiColor VPTR type-{type} command is missing its block-count byte.");
            var count = pointers[at++];
            _CheckBlockCount(count, this._blocksWide - bx, by, type);
            for (var i = 0; i < count; ++i)
              this._DrawHighColourVector(value, bx++, by, skipAlpha: type == 6);
            break;
          }

          default:
            throw new InvalidDataException("A HiColor VPTR stream contains reserved command type 7.");
        }
      }
    }
  }

  private static void _CheckBlockCount(int count, int remaining, int row, int type) {
    if (count > remaining)
      throw new InvalidDataException(
        $"A HiColor VPTR type-{type} command in block row {row} writes/skips {count} blocks with only {remaining} left in that row.");
  }

  private void _DrawHighColourVector(int entry, int bx, int by, bool skipAlpha) {
    var blockArea = this._blockWidth * this._blockHeight;
    var vectorBytes = blockArea * 2;
    var offset = checked(entry * vectorBytes);
    if (entry > _HICOLOR_MAX_VECTOR_INDEX || offset + vectorBytes > this._codebook.Length)
      throw new InvalidDataException(
        $"A HiColor VPTR command names codebook entry {entry}, but the current codebook is only {this._codebook.Length} bytes.");

    var source = this._codebook.AsSpan(offset, vectorBytes);
    var pixels = this._highColourPixels!;
    for (var yy = 0; yy < this._blockHeight; ++yy) {
      for (var xx = 0; xx < this._blockWidth; ++xx) {
        var sourcePixel = BinaryPrimitives.ReadUInt16LittleEndian(source[((yy * this._blockWidth + xx) * 2)..]);
        if (skipAlpha && (sourcePixel & 0x8000) != 0)
          continue;

        pixels[(by * this._blockHeight + yy) * this._width + bx * this._blockWidth + xx] = (ushort)(sourcePixel & 0x7fff);
      }
    }
  }

  private RawImage _BuildHighColourPicture() {
    var pixels = this._highColourPixels!;
    var rgb = new byte[checked(pixels.Length * 3)];
    for (var i = 0; i < pixels.Length; ++i) {
      var value = pixels[i];
      rgb[i * 3] = _Expand5((value >> 10) & 0x1f);
      rgb[i * 3 + 1] = _Expand5((value >> 5) & 0x1f);
      rgb[i * 3 + 2] = _Expand5(value & 0x1f);
    }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte _Expand5(int value) => (byte)((value << 3) | (value >> 2));

  // ============================================================================================
  // Sub-chunk framing
  // ============================================================================================

  private readonly ref struct SubChunk(ReadOnlySpan<byte> id, ReadOnlySpan<byte> payload) {
    public ReadOnlySpan<byte> Id { get; } = id;
    public ReadOnlySpan<byte> Payload { get; } = payload;
  }

  private static IEnumerable<(byte[] Id, byte[] Payload)> _ChunksEnumerable(ReadOnlyMemory<byte> data) {
    // Iterator methods cannot retain spans. Kept separate from _Chunks so the decoder itself still
    // works span-first without allocating a packet-wide copy.
    var at = 0;
    while (at + _CHUNK_HEADER_LENGTH <= data.Length) {
      var span = data.Span;
      var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(span[(at + 4)..]));
      var payloadStart = at + _CHUNK_HEADER_LENGTH;
      if (payloadStart + size > data.Length)
        throw new InvalidDataException($"A VQA picture's sub-chunk at byte {at} states {size} payload bytes past the picture's end.");
      yield return (span.Slice(at, 4).ToArray(), span.Slice(payloadStart, size).ToArray());
      at = payloadStart + size + (size & 1);
    }
  }

  private static ChunkWalker _Chunks(ReadOnlySpan<byte> data) => new(data);

  private ref struct ChunkWalker(ReadOnlySpan<byte> data) {
    private readonly ReadOnlySpan<byte> _data = data;
    private int _at = 0;
    public SubChunk Current { get; private set; }

    public readonly ChunkWalker GetEnumerator() => this;

    public bool MoveNext() {
      if (this._at >= this._data.Length)
        return false;
      if (this._at + _CHUNK_HEADER_LENGTH > this._data.Length)
        throw new InvalidDataException($"A VQA picture has {this._data.Length - this._at} trailing bytes, short of an eight-byte sub-chunk header.");

      var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(this._data[(this._at + 4)..]));
      var payloadStart = this._at + _CHUNK_HEADER_LENGTH;
      if (payloadStart + size > this._data.Length)
        throw new InvalidDataException($"A VQA picture's sub-chunk at byte {this._at} states {size} payload bytes past the picture's end.");

      this.Current = new(this._data.Slice(this._at, 4), this._data.Slice(payloadStart, size));
      this._at = payloadStart + size + (size & 1);
      return true;
    }
  }
}
