using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// What a compressed frame says about itself before any of its pictures are read.
/// </summary>
/// <remarks>
/// RDD 36:2022, 5.1.1 for the syntax and 6.1.1 for the semantics. Twenty bytes, then up to two
/// quantisation weight matrices of sixty-four bytes each — which is why the header of every frame
/// ffmpeg writes is 148 bytes long.
/// <para/>
/// <b>The stated size is authoritative, not the syntax.</b> RDD 36:2022, 6.4 says a decoder shall
/// find the next structure from <c>frame_header_size</c> rather than from where its own parsing
/// stopped, so that a bitstream carrying data of a version variant this decoder does not recognise
/// is stepped over instead of being read as the first picture. The same rule governs the picture
/// header and the slice header, and it is followed in all three places here.
/// <para/>
/// <b>The bit depth is not in the frame header.</b> There is no field for it anywhere in a ProRes
/// bitstream: 7.5.1 leaves the sample depth <c>b</c> to the decoder and gives the conversion from
/// the transform's output for any <c>b</c>. What the bitstream does fix is the precision the
/// coefficients were quantised at, which is why the depth a decoder picks is a property of the
/// profile rather than a free choice — see <see cref="ProResVideoDecoder"/>.
/// </remarks>
internal sealed class ProResFrameHeader {

  /// <summary>The four bytes that identify a compressed frame, RDD 36:2022, 5.1.</summary>
  internal const uint FrameIdentifier = 0x69637066; // 'icpf'

  /// <summary>The twenty bytes of the header that precede the quantisation weight matrices.</summary>
  private const int _FIXED_SIZE = 20;

  private const int _MATRIX_SIZE = 64;

  internal required int HeaderSize { get; init; }
  internal required int BitstreamVersion { get; init; }
  internal required int HorizontalSize { get; init; }
  internal required int VerticalSize { get; init; }
  internal required int ChromaFormat { get; init; }
  internal required int InterlaceMode { get; init; }
  internal required int AlphaChannelType { get; init; }
  internal required int MatrixCoefficients { get; init; }

  /// <summary>The luma quantisation weights, in raster order, <c>[v * 8 + u]</c>.</summary>
  internal required byte[] LumaMatrix { get; init; }

  /// <summary>The chroma quantisation weights, in raster order, <c>[v * 8 + u]</c>.</summary>
  internal required byte[] ChromaMatrix { get; init; }

  /// <summary>Whether the frame is two field pictures rather than one frame picture.</summary>
  internal bool IsInterlaced => this.InterlaceMode is 1 or 2;

  /// <summary>Blocks per macroblock for each chroma component: two at 4:2:2, four at 4:4:4.</summary>
  /// <remarks>RDD 36:2022, 5.3, which derives <c>numCBlocks</c> from <c>chroma_format</c>.</remarks>
  internal int ChromaBlocksPerMacroblock => this.ChromaFormat == 3 ? 4 : 2;

  internal static ProResFrameHeader Parse(ReadOnlySpan<byte> frame) {
    if (frame.Length < _FIXED_SIZE)
      throw new InvalidDataException(
        $"A ProRes frame header is at least {_FIXED_SIZE} bytes and this frame holds {frame.Length}.");

    var headerSize = BinaryPrimitives.ReadUInt16BigEndian(frame);
    if (headerSize < _FIXED_SIZE || headerSize > frame.Length)
      throw new InvalidDataException(
        $"A ProRes frame states a header of {headerSize} bytes, which is neither a whole header nor within the {frame.Length} bytes of the frame.");

    var version = frame[3];

    // 6.4: a decoder shall refuse a bitstream whose version it does not support. Versions 0 and 1
    // are the two this specification describes and the two this decoder reads; anything higher
    // changes the decoding process in a way that by definition is not known here, so reading it as
    // though it were version 1 would produce a picture with no reason to be right.
    if (version > 1)
      throw new NotSupportedException(
        $"This ProRes frame states bitstream version {version}. RDD 36 describes versions 0 and 1, which are the ones read here; a later version changes the decoding process and cannot be read as though it were an earlier one.");

    var chromaFormat = frame[12] >> 6;
    var interlaceMode = (frame[12] >> 2) & 3;
    var alphaChannelType = frame[17] & 0x0F;

    // 6.1.1, Table 1: 0 and 1 are reserved, so a frame carrying either is not one this or any other
    // decoder has a sampling format for.
    if (chromaFormat is not (2 or 3))
      throw new NotSupportedException(
        $"This ProRes frame states chroma_format {chromaFormat}. RDD 36 Table 1 defines 2 (4:2:2) and 3 (4:4:4) and reserves the rest.");

    // 6.1.1, Table 2: 3 is reserved. It is refused rather than read as progressive because the
    // choice decides both the block scan and how the two pictures interleave into the frame.
    if (interlaceMode == 3)
      throw new NotSupportedException(
        "This ProRes frame states interlace_mode 3, which RDD 36 Table 2 reserves.");

    // 6.4: version 0 fixes both of these, and a version 0 frame that states otherwise is describing
    // itself with syntax its own version does not have.
    if (version == 0 && (chromaFormat != 2 || alphaChannelType != 0))
      throw new InvalidDataException(
        $"This ProRes frame states bitstream version 0 with chroma_format {chromaFormat} and alpha_channel_type {alphaChannelType}. RDD 36 6.4 fixes those at 2 and 0 for version 0.");

    var flags = frame[19];
    var loadLuma = (flags & 2) != 0;
    var loadChroma = (flags & 1) != 0;

    var at = _FIXED_SIZE;
    var luma = new byte[_MATRIX_SIZE];
    if (loadLuma) {
      _RequireMatrix(frame, at, headerSize, "luma");
      frame.Slice(at, _MATRIX_SIZE).CopyTo(luma);
      at += _MATRIX_SIZE;
    } else {
      // 7.3: the default weight matrix is a flat 4.
      luma.AsSpan().Fill(4);
    }

    // 7.3: when no chroma matrix is stated the luma one serves for both, which is not the same as
    // falling back to the default — a frame that loads a luma matrix and no chroma one quantises its
    // chroma with the loaded matrix.
    var chroma = new byte[_MATRIX_SIZE];
    if (loadChroma) {
      _RequireMatrix(frame, at, headerSize, "chroma");
      frame.Slice(at, _MATRIX_SIZE).CopyTo(chroma);
    } else {
      luma.CopyTo(chroma, 0);
    }

    return new() {
      HeaderSize = headerSize,
      BitstreamVersion = version,
      HorizontalSize = BinaryPrimitives.ReadUInt16BigEndian(frame[8..]),
      VerticalSize = BinaryPrimitives.ReadUInt16BigEndian(frame[10..]),
      ChromaFormat = chromaFormat,
      InterlaceMode = interlaceMode,
      AlphaChannelType = alphaChannelType,
      MatrixCoefficients = frame[16],
      LumaMatrix = luma,
      ChromaMatrix = chroma,
    };
  }

  private static void _RequireMatrix(ReadOnlySpan<byte> frame, int at, int headerSize, string which) {
    if (at + _MATRIX_SIZE > headerSize || at + _MATRIX_SIZE > frame.Length)
      throw new InvalidDataException(
        $"A ProRes frame says it loads a {which} quantisation weight matrix, but its {headerSize}-byte header has no room for one.");
  }
}
