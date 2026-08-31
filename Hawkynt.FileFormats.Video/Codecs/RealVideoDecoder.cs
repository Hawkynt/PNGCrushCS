using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H263;
using FileFormat.Codecs.RealVideo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes RealVideo 1, the codec a RealMedia file names <c>RV10</c> or <c>RV13</c>.
/// </summary>
/// <remarks>
/// RealVideo 1 is ITU-T H.263 from the macroblock layer down with a different picture header on top.
/// Everything below that header — the macroblock types, the coded block patterns, the motion vector
/// codes and their median predictor, the coefficient tables, the inverse quantisation and the inverse
/// transform — is H.263's, so this codec is the H.263 decoder beside it with its own header reader and
/// its own idea of where a picture begins and ends. Nothing of the block layer is written twice.
/// <para/>
/// What the header replaces is worth stating. H.263 states the picture size as one of five named
/// formats; RealVideo takes it from the container and states none. H.263 codes one picture as one run
/// of macroblocks broken by optional group headers; RealVideo cuts a picture into independently coded
/// runs, each restating the picture's type and quantiser and naming the macroblock it begins at and
/// the number it carries, and sends each run in its own packet so that losing one costs part of a
/// picture rather than all of it. And H.263 keeps its motion vectors inside the picture unless
/// Annex D is signalled, where every RealVideo stream lets them point outside it and reads the edge
/// sample where they do — there is no bit to turn that off with.
/// <para/>
/// <b>What it does not do refuses by name.</b> RealVideo 2, 3 and 4 are named and refused: 2 is a
/// different picture header over an H.263 macroblock layer with features this decoder does not read,
/// and 3 and 4 are a different codec altogether, closer to H.264, sharing nothing below the header.
/// A PB-frame is refused where it is signalled. There is no <c>catch</c> anywhere that hands back a
/// blank, a copied or a repeated picture, because a plausible wrong picture is worse than a refusal:
/// nobody checks a picture that looks like a picture.
/// <para/>
/// <b>Frames come out in coding order, which is display order.</b> RealVideo 1 has no picture coded
/// after one it precedes, so nothing is ever held back.
/// </remarks>
public sealed class RealVideoDecoder : IVideoCodecDecoder<RealVideoDecoder> {

  /// <summary>The codes a container names RealVideo 1 with.</summary>
  /// <remarks>
  /// <c>RV13</c> is the same bitstream under a second name — the code was written by one encoder for
  /// streams the others called <c>RV10</c>, and the version word in the stream's private data is the
  /// same. The later generations are deliberately absent from this list rather than accepted and then
  /// refused, so that a caller asking whether anything reads an <c>RV40</c> stream is told no once,
  /// instead of being handed a decoder that fails on its first packet.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("RV10"),
    CodecTag.FromCharacters("RV13"),
  ];

  /// <summary>The codes of the generations this does not decode, named so a refusal can say which.</summary>
  private static readonly (string Code, string Reason)[] _Refused = [
    ("RV20", "RealVideo 2, whose picture header carries a temporal reference, a variable-width macroblock "
             + "position sized by the picture and a picture-size table this decoder does not read"),
    ("RV30", "RealVideo 3, which is not an H.263 derivative at all — it has its own transform, its own intra "
             + "prediction and a loop filter, and shares nothing below the header with what is implemented here"),
    ("RV40", "RealVideo 4, which like RealVideo 3 is closer to H.264 than to H.263 and shares nothing below the "
             + "header with what is implemented here"),
  ];

  private readonly RealVideoBitstreamVersion _version;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _width;
  private readonly int _height;
  private H263Frame? _reference;

  private RealVideoDecoder(RealVideoBitstreamVersion version, int width, int height) {
    this._version = version;
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "RealVideo 1 (RV10/RV13)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
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
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// The picture size comes from the container and not from the bitstream, because RealVideo states it
  /// nowhere else: its picture header has no size field at all, so a stream whose container lost the
  /// size is a stream nothing can decode. That is the one thing here that a container's copy is not a
  /// copy of.
  /// </remarks>
  public static RealVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    foreach (var (code, reason) in _Refused)
      if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters(code)))
        throw new NotSupportedException($"This stream is coded with {code} — {reason}. It is not implemented.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"This RealVideo stream states a picture size of {stream.Width}x{stream.Height}. RealVideo carries no size in "
        + "its bitstream, so the container's is the only one there is and a stream without it cannot be decoded.");

    var version = RealVideoBitstreamVersion.Read(stream.CodecPrivateData.Span, RealVideoGeneration.RealVideo10);
    if (version.Generation != RealVideoGeneration.RealVideo10)
      throw new NotSupportedException(
        $"This stream is named {stream.Codec} but its private data states bitstream version 0x{version.Version:X8}, "
        + "whose leading byte names a different generation of RealVideo. A file whose two statements about itself "
        + "disagree is not decoded as either.");

    if (version.Minor != RealVideoBitstreamVersion.IMPLEMENTED_MINOR)
      throw new NotSupportedException(
        $"This RealVideo 1 stream states bitstream version 0x{version.Version:X8}, which is revision {version.Minor} of "
        + $"the format. Only revision {RealVideoBitstreamVersion.IMPLEMENTED_MINOR} is implemented — its macroblock "
        + "layer is ITU-T H.263's exactly, and the later revisions' is not: no offset into one of their pictures "
        + "decodes even three macroblocks with the H.263 tables, so the difference is in the macroblock layer and not "
        + "in the length of the picture header. Decoding one of them as though it were revision "
        + $"{RealVideoBitstreamVersion.IMPLEMENTED_MINOR} would produce noise rather than a picture.");

    // Sixty-four macroblocks each way is what the six-bit position fields of a picture header can name,
    // and a picture larger than that would have runs this decoder could not place.
    var macroblockWidth = (stream.Width + 15) / 16;
    var macroblockHeight = (stream.Height + 15) / 16;
    if (macroblockWidth > 63 || macroblockHeight > 63)
      throw new NotSupportedException(
        $"This RealVideo stream is {stream.Width}x{stream.Height}, which is {macroblockWidth}x{macroblockHeight} "
        + "macroblocks. A picture header names the macroblock a run begins at in six bits each way, so a picture more "
        + "than sixty-three macroblocks across or down cannot state where its runs begin.");

    return new(version, stream.Width, stream.Height);
  }

  /// <summary>Decodes one packet and hands back the picture it holds.</summary>
  /// <returns><c>false</c> when the packet held no picture at all.</returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.IsEmpty) {
      frame = null!;
      return false;
    }

    var picture = this._DecodePicture(packet.Data.Span, packet.Fragments);
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = H263ColorConversion.ToRgb24(picture, this._width, this._height),
    };

    return true;
  }

  /// <summary>
  /// Decodes one packet and hands back the reconstructed sample planes rather than a picture.
  /// </summary>
  /// <remarks>
  /// For measuring against a reference decoder, which is the one thing the converted picture cannot be
  /// used for. Turning 4:2:0 planes into interleaved samples means inventing the chrominance that was
  /// never coded, and this library interpolates where ffmpeg repeats — so two decoders that agree on
  /// every coded sample disagree on tens of thousands of converted ones, and a comparison after the
  /// conversion measures the conversion. The planes are what the bitstream actually says.
  /// </remarks>
  internal H263Frame DecodePlanes(CodedPacket packet) => this._DecodePicture(packet.Data.Span, packet.Fragments);

  /// <summary>Nothing is ever held back, so there is nothing left when the packets run out.</summary>
  public IEnumerable<RawImage> Flush() => [];

  // ============================================================================================
  // One picture, as the runs it was cut into
  // ============================================================================================

  /// <summary>
  /// Decodes one whole picture out of the runs a container reassembled into one packet.
  /// </summary>
  /// <remarks>
  /// The runs sit one after another with no marker between them and no start code to find them by, so
  /// where each begins is taken from the container: RealMedia carries one run to a piece, and the
  /// piece offsets come across on the packet. That is the whole reason
  /// <see cref="CodedPacket.FragmentOffsets"/> exists — the boundaries are a fact about how the file
  /// was written, they are unrecoverable once the pieces are joined, and a decoder cannot invent them.
  /// <para/>
  /// A packet that states no boundaries — one picture in one piece, or a caller who assembled the
  /// bytes some other way — falls back to searching the few bytes after the run that just ended. The
  /// search is bounded and is safe because a run's header states which macroblock it begins at, so a
  /// candidate that does not name the macroblock actually due is not a run header. It exists so that a
  /// stream reaching this decoder from somewhere other than a RealMedia file still decodes; every
  /// picture of every file measured here is placed by the container's offsets and never reaches it.
  /// </remarks>
  private H263Frame _DecodePicture(ReadOnlySpan<byte> data, IReadOnlyList<int> fragments) {
    var macroblockCount = this._macroblockWidth * this._macroblockHeight;
    var reader = new H263BitReader(data);

    var first = RealVideoSliceHeader.Read(ref reader, this._version, this._macroblockWidth, macroblockCount, false);

    var header = new H263PictureHeader {
      Width = this._width,
      Height = this._height,
      MacroblockRowsPerGroup = 1,
      IsIntra = first.IsIntra,
      IsReference = true,
      Quantiser = first.Quantiser,
      HasWideEscapeLevel = false,

      // There is no group of blocks layer: a picture's runs are delimited by their macroblock counts
      // and not by a start code, and looking for one would find the sixteen zero bits that ordinary
      // RealVideo macroblock data is free to produce.
      HasGroupLayer = false,

      // Every RealVideo stream lets a motion vector point outside the picture and reads the edge
      // sample where it does. There is no bit to turn it off with, which is why this is not read from
      // the header the way H.263 reads Annex D.
      AllowsVectorsOutsidePicture = true,
      TemporalReference = 0,
    };

    var target = new H263Frame(this._macroblockWidth, this._macroblockHeight);
    var picture = H263PictureDecoder.BeginPicture(header, target, this._reference);

    var run = first;
    var fragment = 0;
    for (; ; ) {
      if (run.IsIntra != first.IsIntra)
        throw new InvalidDataException(
          $"A run of this RealVideo picture states that it is {(run.IsIntra ? "intra" : "predicted")} coded where the "
          + $"picture's first run states {(first.IsIntra ? "intra" : "predicted")}. Every run of one picture codes the "
          + "same kind of picture.");

      picture.DecodeRun(ref reader, run.FirstMacroblock, run.MacroblockCount, run.Quantiser);

      var decoded = run.FirstMacroblock + run.MacroblockCount;
      if (decoded >= macroblockCount)
        break;

      ++fragment;
      if (fragment < fragments.Count) {
        var at = fragments[fragment];
        if (at < 0 || at >= data.Length)
          throw new InvalidDataException(
            $"The container states that a piece of this RealVideo picture begins at byte {at} of a picture {data.Length} "
            + "bytes long.");

        reader.SeekToBit(at << 3);
        run = RealVideoSliceHeader.Read(ref reader, this._version, this._macroblockWidth, macroblockCount, true);
      } else if (!this._TryFindNextRun(ref reader, data, macroblockCount, decoded, out run)) {
        throw new InvalidDataException(
          $"This RealVideo picture stops after {decoded} of its {macroblockCount} macroblocks: the container stated "
          + $"{fragments.Count} piece(s) and no further run header naming macroblock {decoded} was found after the last "
          + "of them. A picture missing its last runs cannot be shown.");
      }

      if (run.FirstMacroblock != decoded)
        throw new InvalidDataException(
          $"A run of this RealVideo picture states that it begins at macroblock {run.FirstMacroblock} where "
          + $"{decoded} was due. The picture's runs do not cover it without a gap.");
    }

    if (header.IsReference)
      this._reference = target;

    return target;
  }

  /// <summary>
  /// How far past the byte a run ended in to look for the next run's header.
  /// </summary>
  /// <remarks>
  /// Every stream measured here begins the next run in the byte the reader stopped in or the one after
  /// it, which is a run padded to a byte and nothing more. The allowance is wider than that because a
  /// padded run costs nothing to step over and a missed run costs the rest of the picture, and it is
  /// bounded because an unbounded search over a corrupt picture would find a header-shaped run of bits
  /// somewhere and decode noise from it.
  /// </remarks>
  private const int _RUN_SEARCH_BYTES = 8;

  private bool _TryFindNextRun(
    ref H263BitReader reader, ReadOnlySpan<byte> data, int macroblockCount, int due, out RealVideoSliceHeader run) {
    run = default;

    var start = (reader.BitPosition + 7) >> 3;
    for (var at = start; at <= start + _RUN_SEARCH_BYTES && at < data.Length; ++at) {
      var candidate = new H263BitReader(data);
      candidate.SeekToBit(at << 3);

      RealVideoSliceHeader header;
      try {
        header = RealVideoSliceHeader.Read(ref candidate, this._version, this._macroblockWidth, macroblockCount, true);
      } catch (InvalidDataException) {
        // Not a run header, which is what most of the bytes searched over are. This is not a decode
        // that failed and was swallowed: nothing has been reconstructed from these bits, and a picture
        // whose runs are never found is refused below rather than handed back part-decoded.
        continue;
      }

      if (header.FirstMacroblock != due)
        continue;

      reader.SeekToBit(candidate.BitPosition);
      run = header;
      return true;
    }

    return false;
  }
}
