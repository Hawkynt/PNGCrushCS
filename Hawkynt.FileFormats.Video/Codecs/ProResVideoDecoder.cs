using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.ProRes;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Apple ProRes: an intra-only transform codec whose every frame is a whole picture.
/// </summary>
/// <remarks>
/// Written from SMPTE RDD 36:2022, <i>Apple ProRes Bitstream Syntax and Decoding Process</i>, which
/// is the published description of the format; the clause numbers cited throughout these files are
/// that document's. Nothing here is derived from another decoder's source.
/// <para/>
/// <b>Intra only, so there is no reference handling at all.</b> Every frame stands alone — that is
/// what makes ProRes an editing codec — so a decoder holds no picture between packets, a seek needs
/// nothing decoded before it, and a difference in one frame cannot become a difference in the next.
/// The only state this class keeps is the stream's dimensions.
/// <para/>
/// <b>The bit depth is a decoder's choice, not the bitstream's.</b> There is no depth field anywhere
/// in a ProRes frame. RDD 36:2022, 7.5.1 gives the conversion from the transform's output to samples
/// of any depth <c>b</c> and leaves <c>b</c> open. What is fixed is the precision the encoder
/// quantised at, so the depth worth choosing is the one the profile is coded for: ten bits for the
/// 4:2:2 profiles — Proxy, LT, Standard (<c>apcn</c>) and HQ (<c>apch</c>) — and twelve for the
/// 4:4:4 ones (<c>ap4h</c>, <c>ap4x</c>). Those are also the depths ffmpeg reconstructs at, which is
/// what makes a sample-by-sample comparison against it meaningful rather than a comparison of two
/// different normalisations.
/// <para/>
/// <b>Measured against ffmpeg, on the planes, at the coded depth.</b> Every frame of every profile
/// both of ffmpeg's ProRes encoders will write was decoded here and by ffmpeg and compared plane by
/// plane, sample by sample, against <c>-pix_fmt yuv422p10le</c> and <c>yuv444p12le</c> — before any
/// reduction to eight bits, so that what is measured is the decode and not the reduction, and on the
/// planes rather than on packed colour, because this library interpolates chroma where ffmpeg
/// replicates and a comparison of the two would measure that instead.
/// <para/>
/// Progressive and interlaced in both field orders, sizes that are and are not a whole number of
/// macroblocks, from 40x24 to 1280x718: <b>every sample of every plane is within one level, and one
/// is the only difference that ever occurs.</b> That residue is the inverse transform and nothing
/// else — see <see cref="ProResInverseDct"/>. <b>Alpha is exact</b>, at both of its depths, with no
/// sample differing anywhere; it should be, since RDD 36 codes alpha losslessly with no transform in
/// the path.
/// <para/>
/// <b>What refuses.</b> A bitstream version later than 1, whose decoding process this specification
/// does not describe; a reserved <c>chroma_format</c>, <c>interlace_mode</c> or
/// <c>alpha_channel_type</c>; a <c>quantization_index</c> outside the permitted 1 to 224; a version 0
/// frame stating syntax its own version does not have; a packet that is not a compressed frame; and
/// any structure whose stated size does not fit inside the one containing it. There is no
/// <c>catch</c> here returning a blank, a copied or a repeated frame.
/// </remarks>
public sealed class ProResVideoDecoder : IVideoCodecDecoder<ProResVideoDecoder> {

  /// <summary>
  /// The codes the profiles of this codec are named by.
  /// </summary>
  /// <remarks>
  /// One bitstream, six names. The profiles differ in the quantisation an encoder applies and in the
  /// sampling it writes, both of which a decoder reads out of the frame itself, so the tag chooses
  /// nothing here except the sample depth — and even that follows from <c>chroma_format</c> rather
  /// than from the tag. They are all listed because a container names the stream with one of them
  /// and a decoder that took only some would refuse files it can read.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("apco"), // 422 Proxy
    CodecTag.FromCharacters("apcs"), // 422 LT
    CodecTag.FromCharacters("apcn"), // 422 Standard
    CodecTag.FromCharacters("apch"), // 422 HQ
    CodecTag.FromCharacters("ap4h"), // 4444
    CodecTag.FromCharacters("ap4x"), // 4444 XQ
  ];

  /// <summary>The four bytes a compressed frame begins with, after its size. RDD 36:2022, 5.1.</summary>
  private static readonly byte[] _FrameIdentifier = "icpf"u8.ToArray();

  /// <summary>The bytes of <c>frame_size</c> and <c>frame_identifier</c> together.</summary>
  private const int _FRAME_PREFIX_SIZE = 8;

  private readonly int _width;
  private readonly int _height;

  private ProResVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "Apple ProRes";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder from the stream description, which for this codec states only the picture size.
  /// </summary>
  /// <remarks>
  /// There is nothing else in it to read. A ProRes sample description carries no codec configuration
  /// the way an AVC one does: every frame restates its own dimensions, sampling, interlacing and
  /// quantisation weights, which is what lets a single frame be cut out of a stream and still decode.
  /// The container's dimensions are kept only to check the frames against and to size the picture
  /// that comes out.
  /// </remarks>
  public static ProResVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    return new(stream.Width, stream.Height);
  }

  /// <summary>Decodes one frame, which for this codec is always exactly one whole picture.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var planes = this.DecodePlanes(packet.Data, out var header);

    frame = new() {
      Width = header.HorizontalSize,
      Height = header.VerticalSize,
      Format = planes.Alpha == null ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
      PixelData = ProResColorConversion.ToPackedColour(planes, header.HorizontalSize, header.VerticalSize, header.MatrixCoefficients),
    };

    return true;
  }

  /// <summary>
  /// Decodes one frame as far as its component planes, before any narrowing or colour conversion.
  /// </summary>
  /// <remarks>
  /// This is where a comparison against another decoder has to be made. The planes are the output of
  /// the decoding process RDD 36:2022, 7 describes; everything after them — narrowing to eight bits,
  /// choosing a colour matrix, resampling chroma up to every luma column — is a display convention
  /// that two correct decoders are free to disagree about. Comparing packed colour therefore
  /// measures the conventions and not the decode, and does so loudly enough to hide a real defect.
  /// </remarks>
  internal ProResPlanes DecodePlanes(ReadOnlyMemory<byte> frame, out ProResFrameHeader header) {
    var span = frame.Span;
    if (span.Length < _FRAME_PREFIX_SIZE)
      throw new InvalidDataException(
        $"A ProRes frame begins with {_FRAME_PREFIX_SIZE} bytes of size and identifier, and this packet holds {span.Length}.");

    var frameSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span);
    if (frameSize < _FRAME_PREFIX_SIZE || frameSize > span.Length)
      throw new InvalidDataException(
        $"A ProRes frame states a size of {frameSize} bytes and its packet holds {span.Length}.");

    if (!span.Slice(4, 4).SequenceEqual(_FrameIdentifier))
      throw new InvalidDataException(
        "A ProRes packet does not begin with the four bytes 'icpf' that identify a compressed frame.");

    header = ProResFrameHeader.Parse(span[_FRAME_PREFIX_SIZE..frameSize]);

    // Table 7 defines 0, 1 and 2 and reserves 3 to 15. A reserved type cannot be read, and it cannot
    // be skipped either — the alpha data are the tail of every slice, so not knowing their code
    // means not knowing where they end, and a frame carrying one is refused rather than decoded
    // without its transparency.
    if (header.AlphaChannelType > 2)
      throw new NotSupportedException(
        $"This ProRes frame states alpha_channel_type {header.AlphaChannelType}, which RDD 36 Table 7 reserves. Its alpha data cannot be read and cannot be stepped over.");

    this._RefuseUnexpectedSize(header);

    var planes = ProResPlanes.Allocate(
      width: (header.HorizontalSize + 15) / 16 * 16,
      height: header.VerticalSize,
      chromaShift: header.ChromaFormat == 3 ? 0 : 1,
      bitDepth: header.ChromaFormat == 3 ? 12 : 10,
      alphaChannelType: header.AlphaChannelType);

    var at = _FRAME_PREFIX_SIZE + header.HeaderSize;

    // 5.1: one picture for a progressive frame, two for an interlaced one, in the temporal order the
    // fields are displayed in. 6.2 gives each picture its own height, which for an odd number of
    // rows differs between the two fields by one.
    if (!header.IsInterlaced) {
      ProResPictureDecoder.Decode(frame[at..frameSize], header, planes, header.VerticalSize, 0, 1);
      return planes;
    }

    var topHeight = (header.VerticalSize + 1) / 2;
    var bottomHeight = header.VerticalSize / 2;

    // Table 2: interlace_mode 1 makes the first picture the top field, 2 makes the second one the
    // top field. So the first picture's rows land on the even rows of the frame in the first case
    // and on the odd rows in the second.
    var firstIsTop = header.InterlaceMode == 1;

    var firstSize = ProResPictureDecoder.Decode(
      frame[at..frameSize], header, planes, firstIsTop ? topHeight : bottomHeight, firstIsTop ? 0 : 1, 2);

    ProResPictureDecoder.Decode(
      frame[(at + firstSize)..frameSize], header, planes, firstIsTop ? bottomHeight : topHeight,
      firstIsTop ? 1 : 0, 2);

    return planes;
  }

  /// <summary>
  /// Refuses a frame whose own dimensions are not the ones the container described.
  /// </summary>
  /// <remarks>
  /// The frame is believed over the container — every ProRes frame restates its size and that is
  /// what its slices are laid out for — but a disagreement is still worth refusing rather than
  /// silently resolving. A container that says one size and frames that say another is a file that
  /// has been cut or repackaged wrongly, and a caller that asked for a stream of one size should
  /// hear about it instead of receiving pictures of another.
  /// </remarks>
  private void _RefuseUnexpectedSize(ProResFrameHeader header) {
    if (header.HorizontalSize == this._width && header.VerticalSize == this._height)
      return;

    throw new InvalidDataException(
      $"A ProRes frame states a picture of {header.HorizontalSize}x{header.VerticalSize} in a stream the container describes as {this._width}x{this._height}.");
  }
}
