using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Idcin;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes id Cinematic video (<c>IDCV</c>) — the FMV codec behind Quake II's cutscenes — an order-1
/// static Huffman code over an already-paletted 8-bit picture, 256 trees selected by the value of the
/// pixel just decoded.
/// </summary>
/// <remarks>
/// Nothing here is inter-frame. Every picture is its own complete Huffman-coded index buffer with no
/// motion compensation and no reference to any other picture — the palette is the only thing that can
/// carry over, and only because a command that states no palette means exactly "the previous one still
/// applies", not "there is none".
/// <para/>
/// <b>The tree, not the table, is what a decoder builds.</b> The file's 64KiB table is 256 histograms
/// of 256 byte counts, one histogram per value the previous pixel might have held; what actually reads
/// bits is 256 canonical Huffman trees built from those counts, one built once per stream rather than
/// once per picture — see <see cref="IdcinHuffmanTree"/> for the construction, including the one
/// degenerate case (a context with at most one nonzero count) it reproduces on purpose rather than
/// special-cases away.
/// <para/>
/// <b>Bits read low end first — settled by measurement, since the format's own documentation names a
/// Huffman coder without stating which bit of a byte comes first.</b> Each coded byte is walked from
/// its least significant bit, a zero stepping to a node's first child and a one to its second, until
/// the node reached is itself a symbol value rather than a further choice; that symbol is both the next
/// output pixel and the context — the tree — the pixel after it is read against. The very first pixel
/// of every picture uses context zero, with nothing decoded yet to supply one. Reading most significant
/// bit first was tried against both real files and fails immediately: neither file's first picture
/// finishes decoding before its own coded bytes run out, while least significant bit first reaches
/// every picture of both, forty-eight and eighty-two.
/// <para/>
/// <b>The palette is six-bit VGA precision unless it plainly is not.</b> Some of the tools that built
/// these files wrote full eight-bit RGB triplets instead of the vast majority's six-bit DAC values;
/// nothing in a palette command says which, so every one of its 768 bytes is checked and only widened
/// — by repeating the top two bits into the bottom, the same construction as RoQ's and MVE's palettes —
/// when none of them exceeds 63. A single byte over that line means the palette is left exactly as
/// written, since a component that large cannot be a six-bit value in the first place.
/// <para/>
/// <b>Measured.</b> Two files from <c>samples.ffmpeg.org/game-formats/idcin/</c> — 320x200 and 320x240,
/// 48 and 82 pictures, 130 in all — were decoded here and by ffmpeg and compared sample for sample
/// against ffmpeg's own <c>rgb24</c> output, index looked up through the installed palette both ways:
/// every picture is identical, maximum delta nought. <c>quake.cin</c> — the smaller of the two, and
/// whose bytes match the checksum ffmpeg's own sample server publishes for it — runs out of file
/// mid-picture with no end-of-file command anywhere in it; ffmpeg's own decode stops at the same
/// forty-eighth picture this reader does, which is what "measured against ffmpeg" means for a file
/// that is not, itself, complete.
/// </remarks>
public sealed class IdcinVideoDecoder : IVideoCodecDecoder<IdcinVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("IDCV");

  private const int _HEADER_LENGTH = 20;
  private const int _PALETTE_LENGTH = 768;
  private const uint _COMMAND_PALETTE = 1;
  private const int _TOKENS = 256;

  private readonly IdcinHuffmanTree[] _trees;
  private readonly int _width;
  private readonly int _height;
  private readonly byte[] _palette = new byte[768];

  public static string CodecName => "id Cinematic Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static IdcinVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var table = stream.CodecPrivateData.Span;
    if (table.Length < _TOKENS * _TOKENS)
      throw new InvalidDataException(
        $"An id Cinematic video stream carries {table.Length} bytes of Huffman table, short of the "
        + $"{_TOKENS * _TOKENS} a 256x256 histogram needs.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"An id Cinematic video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    var trees = new IdcinHuffmanTree[_TOKENS];
    for (var context = 0; context < _TOKENS; ++context)
      trees[context] = IdcinHuffmanTree.Build(table.Slice(context * _TOKENS, _TOKENS));

    return new(trees, stream.Width, stream.Height);
  }

  private IdcinVideoDecoder(IdcinHuffmanTree[] trees, int width, int height) {
    this._trees = trees;
    this._width = width;
    this._height = height;
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 4)
      throw new InvalidDataException($"An id Cinematic video packet is {data.Length} bytes, short of the four-byte command word it opens with.");

    var command = BinaryPrimitives.ReadUInt32LittleEndian(data);
    var cursor = 4;

    if (command == _COMMAND_PALETTE) {
      if (data.Length < cursor + _PALETTE_LENGTH)
        throw new InvalidDataException("An id Cinematic video packet states a palette but is too short to carry the 768 bytes one needs.");

      this._ReadPalette(data.Slice(cursor, _PALETTE_LENGTH));
      cursor += _PALETTE_LENGTH;
    }

    if (data.Length < cursor + 8)
      throw new InvalidDataException("An id Cinematic video packet is too short to carry the chunk size and decoded pixel count after its command and palette.");

    var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data[cursor..]);
    // The decoded pixel count right after is never read back: on every picture of both real files
    // measured it equals width times height exactly, which this decoder already knows from the stream.
    cursor += 8;

    var videoDataLength = (int)chunkSize - 4;
    if (videoDataLength < 0 || data.Length < cursor + videoDataLength)
      throw new InvalidDataException(
        $"An id Cinematic video packet states a chunk size of {chunkSize}, which does not fit the "
        + $"{data.Length - cursor} bytes left in the packet after its own header.");

    frame = this._DecodePicture(data.Slice(cursor, videoDataLength));
    return true;
  }

  /// <summary>
  /// Installs the palette this packet carries. Six-bit VGA precision is widened to eight bits by
  /// repeating its top two bits into the bottom — the same construction RoQ's and MVE's six-bit
  /// palettes use — unless any of the 768 bytes exceeds 63, in which case the palette is already
  /// eight-bit precision and is kept exactly as written.
  /// </summary>
  private void _ReadPalette(ReadOnlySpan<byte> colours) {
    var isSixBit = true;
    foreach (var component in colours)
      if (component > 63) {
        isSixBit = false;
        break;
      }

    for (var i = 0; i < _PALETTE_LENGTH; ++i)
      this._palette[i] = isSixBit ? ChannelScaling.Expand6(colours[i]) : colours[i];
  }

  private RawImage _DecodePicture(ReadOnlySpan<byte> videoData) {
    var pixelCount = this._width * this._height;
    var indices = new byte[pixelCount];

    var context = 0;
    var bytePosition = 0;
    var bitsLeftInByte = 0;
    var currentByte = 0;

    for (var i = 0; i < pixelCount; ++i) {
      var tree = this._trees[context];
      var node = tree.Root;

      while (node >= _TOKENS) {
        if (bitsLeftInByte == 0) {
          if (bytePosition >= videoData.Length)
            throw new InvalidDataException(
              $"An id Cinematic picture's Huffman data ran out after {bytePosition} bytes, at pixel "
              + $"{i} of {pixelCount}.");

          currentByte = videoData[bytePosition++];
          bitsLeftInByte = 8;
        }

        var bit = currentByte & 1;
        currentByte >>= 1;
        --bitsLeftInByte;
        node = bit == 0 ? tree.Left(node) : tree.Right(node);
      }

      indices[i] = (byte)node;
      context = node;
    }

    var palette = new byte[768];
    Array.Copy(this._palette, palette, 768);

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = palette,
      PaletteCount = 256,
    };
  }
}
