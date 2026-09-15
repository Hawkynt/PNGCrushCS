using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.CineForm;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes simultaneous CineForm stereo/multicam pictures as legacy concatenated CFHD samples.</summary>
/// <remarks>
/// The underlying pictures remain ordinary intra-only CineForm samples. GoPro's legacy tags 92-94
/// state the total encoded view count, zero-based native view number, and optional one-based quality
/// rank. The SDK describes view zero as the left sample and the next sample offset as the right eye for
/// a two-view packet. More than two samples are treated as multicam without inventing spatial roles.
/// </remarks>
public sealed class CineFormMultiViewEncoder {
  private readonly CineFormVideoEncoder _singleViewEncoder;
  private readonly MediaStreamInfo _stream;
  private uint _frameNumber;

  private CineFormMultiViewEncoder(MediaStreamInfo stream, CineFormEncodingFormat encodingFormat) {
    ArgumentNullException.ThrowIfNull(stream);
    this._singleViewEncoder = CineFormVideoEncoder.Create(stream, encodingFormat);
    this._stream = this._singleViewEncoder.DescribeStream();
  }

  public static CineFormMultiViewEncoder Create(
    MediaStreamInfo stream,
    CineFormEncodingFormat encodingFormat = CineFormEncodingFormat.Yuv422)
    => new(stream, encodingFormat);

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>Encodes every simultaneous view into one packet in native view-number order.</summary>
  public bool TryEncode(RawMultiViewImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"This CineForm stream is {this._stream.Width}x{this._stream.Height}; the multi-view picture is {frame.Width}x{frame.Height}.");

    var viewCount = frame.Views.Count;
    if (viewCount > ushort.MaxValue)
      throw new NotSupportedException($"Legacy CineForm stores its encoded-view count in sixteen bits; {viewCount} views do not fit.");

    _ValidateRepresentableRoles(frame);

    var codedFrameNumber = presentationTimestamp ?? this._frameNumber;
    var samples = new byte[viewCount][];
    var totalLength = 0;

    for (var i = 0; i < viewCount; ++i) {
      var view = frame.Views[i];
      if (view.Index != i)
        throw new NotSupportedException(
          $"Legacy CineForm encoded-view numbers are contiguous zero-based ordinals; view {view.Index} appears at ordinal {i}.");

      if (!this._singleViewEncoder.TryEncode(view.Image, codedFrameNumber, out var single))
        throw new InvalidDataException($"CineForm did not emit a coded sample for view {view.Index}.");

      samples[i] = CineFormMultiViewFraming.AddViewTags(single.Data.ToArray(), viewCount, view.Index, view.QualityRank);
      totalLength = checked(totalLength + samples[i].Length);
    }

    var data = new byte[totalLength];
    var offset = 0;
    foreach (var sample in samples) {
      sample.CopyTo(data, offset);
      offset += sample.Length;
    }

    ++this._frameNumber;
    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  private static void _ValidateRepresentableRoles(RawMultiViewImage frame) {
    if (frame.Views.Count == 2) {
      var first = frame.Views[0];
      var second = frame.Views[1];
      if (first.Role is not (RawImageViewRole.Unspecified or RawImageViewRole.Left))
        throw new NotSupportedException(
          $"A two-view CineForm packet encodes native view 0 as the left eye; role {first.Role} cannot be preserved there.");
      if (second.Role is not (RawImageViewRole.Unspecified or RawImageViewRole.Right))
        throw new NotSupportedException(
          $"A two-view CineForm packet encodes native view 1 as the right eye; role {second.Role} cannot be preserved there.");
      return;
    }

    foreach (var view in frame.Views)
      if (view.Role is not (RawImageViewRole.Unspecified or RawImageViewRole.Auxiliary))
        throw new NotSupportedException(
          $"Legacy CineForm multicam tags preserve view numbers and quality ranks, not the {view.Role} spatial role on view {view.Index}.");
  }
}

/// <summary>Decodes a legacy CineForm stereo/multicam packet without flattening its simultaneous views.</summary>
public sealed class CineFormMultiViewDecoder {
  private readonly CineFormVideoDecoder _singleViewDecoder;

  private CineFormMultiViewDecoder(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    this._singleViewDecoder = CineFormVideoDecoder.Create(stream);
  }

  public static CineFormMultiViewDecoder Create(MediaStreamInfo stream) => new(stream);

  public bool TryDecode(CodedPacket packet, out RawMultiViewImage frame) {
    var samples = CineFormMultiViewFraming.Split(packet.Data);
    var views = new RawImageView[samples.Count];

    foreach (var (sample, info) in samples) {
      var subpacket = packet with { Data = sample };
      if (!this._singleViewDecoder.TryDecode(subpacket, out var image))
        throw new InvalidDataException($"CineForm did not produce a decoded raster for view {info.ViewNumber}.");

      var role = samples.Count == 2
        ? info.ViewNumber switch {
          0 => RawImageViewRole.Left,
          1 => RawImageViewRole.Right,
          _ => RawImageViewRole.Unspecified,
        }
        : RawImageViewRole.Unspecified;

      views[info.ViewNumber] = new(image, info.ViewNumber, role, info.QualityRank);
    }

    frame = new(views);
    return true;
  }

  public IEnumerable<RawMultiViewImage> Flush() => [];
}
