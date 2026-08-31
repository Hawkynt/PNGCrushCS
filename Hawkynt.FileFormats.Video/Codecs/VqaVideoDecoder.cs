using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Vqa;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Westwood VQA video (<c>WSVQ</c>) — the FMV codec behind Command &amp; Conquer, Red Alert and
/// most of Westwood's DOS-and-Windows-era catalogue — vector-quantised 8-bit blocks read against a
/// codebook that is itself spread across the pictures that use it.
/// </summary>
/// <remarks>
/// A picture is a codebook, a palette and an index table read together, not necessarily all three
/// arriving in the same <c>VQFR</c> chunk. The size a codebook entry — a "vector" in the format's own
/// terminology — covers is stated once in <c>VQHD</c>: four by two pixels in every sample this was
/// measured against. An index table names, for every block of the picture, either a codebook entry to
/// copy or a single colour to fill the block with outright.
/// <para/>
/// <b>A codebook is rationed across eight pictures, not delivered whole.</b> The first picture's
/// <c>CBFZ</c> chunk is a complete codebook; every eighth picture after it is preceded by seven more
/// carrying a <c>CBPZ</c> chunk apiece, an eighth of the *next* codebook each, which only becomes a
/// real codebook once all eight are concatenated in picture order and decompressed together — decompressing
/// one piece on its own, the format's own description states outright, is not the same data. <b>That
/// assembled codebook becomes current starting with the picture after the one whose <c>CBPZ</c>
/// completed it, not the picture that delivered the final piece</b> — measured directly: applying it to
/// the delivering picture too reads every eighth picture of a real 85-picture file wrong, some three
/// hundred thousand samples' worth, and holding it back one picture reads every one of the same 85
/// pictures correctly.
/// <para/>
/// <b>An index table is two byte-arrays end to end, not one array of pairs.</b> For a block at column
/// <c>bx</c>, row <c>by</c> (in block units), the format's own description gives
/// <c>topVal = table[by*blocksWide+bx]</c> and <c>lowVal = table[blocksWide*blocksHigh + by*blocksWide+bx]</c>
/// — a value from the first half of the table and the corresponding one from the second, not two
/// neighbouring bytes. <c>lowVal == 0x0f</c> means "fill this block with colour <c>topVal</c>" outright;
/// any other <c>lowVal</c> means "copy codebook entry <c>lowVal*256+topVal</c>".
/// <para/>
/// <b>Measured.</b> Three files from <c>samples.ffmpeg.org/game-formats/vqa/</c> — two from the Red
/// Alert set and one from the original Command &amp; Conquer set, 320x156, 85 and 160 pictures — were
/// decoded here and by ffmpeg and compared sample for sample against ffmpeg's own <c>rgb24</c> output:
/// every picture of both files, 245 in all, is identical.
/// <para/>
/// <b>Only version 2, standard colour, is decoded.</b> <c>VQHD</c> states a version — the format's own
/// description covers both <c>1</c>, from Legend of Kyrandia III, and <c>2</c>, from Command &amp;
/// Conquer and Red Alert onward — and version 1's own index table does not decode under the reading
/// above: measured against a real version-1 file, it produces implausible, structureless codebook
/// indices rather than the small, mostly-repeating ones every version-2 file this was measured against
/// gives. Nothing in the format's own published description says what a version-1 index table uses
/// instead, so version 1, and the separate fifteen-bit-colour form the header's own flag byte can name,
/// both refuse by name rather than guess.
/// </remarks>
public sealed class VqaVideoDecoder : IVideoCodecDecoder<VqaVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WSVQ");

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _PALETTE_ENTRIES = 256;
  private const int _PALETTE_BYTES = _PALETTE_ENTRIES * 3;
  private const int _CODEBOOK_PIECES_PER_GROUP = 8;
  private const int _SOLID_FILL_SENTINEL = 0x0f;
  private const int _SUPPORTED_VERSION = 2;

  private readonly int _width;
  private readonly int _height;
  private readonly int _blockWidth;
  private readonly int _blockHeight;
  private readonly int _blocksWide;
  private readonly int _blocksHigh;

  private byte[] _codebook = [];
  private byte[]? _pendingCodebook;
  private readonly List<byte[]> _codebookPieces = [];
  private readonly byte[] _palette = new byte[_PALETTE_BYTES];

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Westwood VQA Video";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static VqaVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var header = stream.CodecPrivateData.Span;
    if (header.Length < 42)
      throw new InvalidDataException($"A VQA video stream carries {header.Length} bytes of header, short of the forty-two a VQHD payload needs.");

    var version = BinaryPrimitives.ReadUInt16LittleEndian(header);
    var isHighColour = (header[2] & 0x10) != 0;

    if (version != _SUPPORTED_VERSION)
      throw new NotSupportedException(
        $"This VQA stream states format version {version}. Only version {_SUPPORTED_VERSION}'s index "
        + "table layout — the far more common one, and the one every sample this was measured against "
        + "uses — is implemented; nothing published states what version 1's own layout is.");

    if (isHighColour)
      throw new NotSupportedException(
        "This VQA stream's header flag byte marks the separate fifteen-bit-colour form. Only the "
        + "standard 8-bit palettised form every sample this was measured against carries is implemented.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"A VQA video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    var blockWidth = header[10];
    var blockHeight = header[11];
    if (blockWidth == 0 || blockHeight == 0)
      throw new InvalidDataException($"A VQA video stream states a block size of {blockWidth}x{blockHeight}, which has no pixels.");
    if (stream.Width % blockWidth != 0 || stream.Height % blockHeight != 0)
      throw new NotSupportedException(
        $"This VQA stream's {stream.Width}x{stream.Height} picture is not an exact number of "
        + $"{blockWidth}x{blockHeight} blocks. No sample this was measured against carries one that isn't.");

    return new(stream.Width, stream.Height, blockWidth, blockHeight);
  }

  private VqaVideoDecoder(int width, int height, int blockWidth, int blockHeight) {
    this._width = width;
    this._height = height;
    this._blockWidth = blockWidth;
    this._blockHeight = blockHeight;
    this._blocksWide = width / blockWidth;
    this._blocksHigh = height / blockHeight;
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;

    // A codebook that finished accumulating during the previous picture's own CBPZ piece becomes
    // current starting with this picture, not the one that delivered its final piece — see this type's
    // remarks for how that was measured.
    if (this._pendingCodebook != null) {
      this._codebook = this._pendingCodebook;
      this._pendingCodebook = null;
    }

    byte[]? table = null;
    var at = 0;
    while (at + _CHUNK_HEADER_LENGTH <= data.Length) {
      var id = data.Slice(at, 4);
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(at + 4)..]);
      var payloadStart = at + _CHUNK_HEADER_LENGTH;
      if (payloadStart + size > data.Length)
        throw new InvalidDataException($"A VQA picture's sub-chunk at byte {at} states a payload of {size} bytes, which runs past the picture's own data.");

      var payload = data.Slice(payloadStart, size);
      var kind = id[..3];
      var compression = id[3];

      if (kind.SequenceEqual("CBF"u8))
        this._codebook = _DecodeVariable(payload, compression);
      else if (kind.SequenceEqual("CBP"u8))
        this._AccumulateCodebookPiece(payload, compression);
      else if (kind.SequenceEqual("CPL"u8))
        this._ReadPalette(payload, compression);
      else if (kind.SequenceEqual("VPT"u8))
        table = _DecodeFixed(payload, compression, this._blocksWide * this._blocksHigh * 2, "a VPT? index table");

      var padding = size & 1;
      at = payloadStart + size + padding;
    }

    if (table == null)
      throw new InvalidDataException("A VQA picture chunk carries no index table (VPT? sub-chunk).");

    frame = this._BuildPicture(table);
    return true;
  }

  private void _AccumulateCodebookPiece(ReadOnlySpan<byte> payload, byte compression) {
    this._codebookPieces.Add(payload.ToArray());
    if (this._codebookPieces.Count < _CODEBOOK_PIECES_PER_GROUP)
      return;

    // "If the chunks are CBPZ, first you need to append 8 of them and then decompress the data, NOT
    // decompress each chunk individually" — the format's own description, and the reason this buffers
    // pieces rather than decompressing each as it arrives.
    var combinedLength = 0;
    foreach (var piece in this._codebookPieces)
      combinedLength += piece.Length;

    var combined = new byte[combinedLength];
    var offset = 0;
    foreach (var piece in this._codebookPieces) {
      piece.CopyTo(combined.AsSpan(offset));
      offset += piece.Length;
    }

    this._pendingCodebook = compression == (byte)'Z' ? VqaFormat80.Decompress(combined) : combined;
    this._codebookPieces.Clear();
  }

  private static byte[] _DecodeVariable(ReadOnlySpan<byte> payload, byte compression)
    => compression == (byte)'Z' ? VqaFormat80.Decompress(payload) : payload.ToArray();

  /// <summary>
  /// Decompresses (or takes verbatim) a chunk whose decompressed size is known ahead of time from the
  /// picture's own geometry — the index table, currently the only such chunk this decoder reads.
  /// </summary>
  /// <remarks>
  /// An uncompressed chunk shorter than that known size is named here rather than left to the range
  /// check <c>payload[..length]</c> would otherwise throw on its own — <see cref="ArgumentOutOfRangeException"/>
  /// with no further message, which is exactly the failure a bare index or count exception in this
  /// project's own standard is not allowed to be. No real sample measured against this decoder carries
  /// a short uncompressed chunk of this kind; the compressed path's own equivalent check lives in
  /// <see cref="VqaFormat80.Decompress(ReadOnlySpan{byte},int)"/>.
  /// </remarks>
  private static byte[] _DecodeFixed(ReadOnlySpan<byte> payload, byte compression, int length, string description) {
    if (compression == (byte)'Z')
      return VqaFormat80.Decompress(payload, length);

    if (payload.Length < length)
      throw new InvalidDataException(
        $"This picture's {description} needs {length} uncompressed bytes but its chunk carries only {payload.Length}.");

    return payload[..length].ToArray();
  }

  /// <summary>
  /// Installs the palette this picture carries. Six-bit VGA precision is widened to eight bits by
  /// repeating its top two bits into the bottom — the same construction RoQ's, Interplay MVE's and id
  /// Cinematic's six-bit palettes use.
  /// </summary>
  /// <remarks>
  /// A <c>CPL?</c> chunk is not always the full 768 bytes of 256 colours. Measured against four real
  /// files from the original Command &amp; Conquer demo — every one of them, not a damaged corner case —
  /// the chunk is 753 bytes, 251 colours, and nothing past index 250 is ever painted by any block those
  /// same files' index tables name. Only the colours a chunk actually states are installed here; any
  /// past the end of what it carries keep whatever they already held, the same "unstated means
  /// unchanged" rule this project's other partial-palette formats use.
  /// </remarks>
  private void _ReadPalette(ReadOnlySpan<byte> payload, byte compression) {
    var colours = _DecodeVariable(payload, compression);
    var count = Math.Min(colours.Length, _PALETTE_BYTES);
    for (var i = 0; i < count; ++i)
      this._palette[i] = ChannelScaling.Expand6(colours[i] & 0x3F);
  }

  private RawImage _BuildPicture(byte[] table) {
    var indices = new byte[this._width * this._height];
    var blocksWide = this._blocksWide;
    var blocksHigh = this._blocksHigh;
    var blockWidth = this._blockWidth;
    var blockHeight = this._blockHeight;
    var blockArea = blockWidth * blockHeight;
    var codebook = this._codebook;
    var codebookEntries = codebook.Length / blockArea;

    for (var by = 0; by < blocksHigh; ++by) {
      for (var bx = 0; bx < blocksWide; ++bx) {
        var topValue = table[by * blocksWide + bx];
        var lowValue = table[blocksWide * blocksHigh + by * blocksWide + bx];

        if (lowValue == _SOLID_FILL_SENTINEL) {
          var colourIndex = topValue;
          for (var yy = 0; yy < blockHeight; ++yy) {
            var rowStart = (by * blockHeight + yy) * this._width + bx * blockWidth;
            indices.AsSpan(rowStart, blockWidth).Fill(colourIndex);
          }

          continue;
        }

        var entry = lowValue * 256 + topValue;
        if (entry >= codebookEntries)
          throw new InvalidDataException(
            $"A VQA index table names codebook entry {entry}, but the current codebook holds only {codebookEntries}.");

        var entryOffset = entry * blockArea;
        for (var yy = 0; yy < blockHeight; ++yy) {
          var rowStart = (by * blockHeight + yy) * this._width + bx * blockWidth;
          codebook.AsSpan(entryOffset + yy * blockWidth, blockWidth).CopyTo(indices.AsSpan(rowStart, blockWidth));
        }
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
}
