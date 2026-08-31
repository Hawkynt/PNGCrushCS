using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Escape130;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Eidos' Escape 130 — the FMV codec that replaced Escape 122 and Escape 124 for the PC Tomb
/// Raider era and the handful of other Eidos titles the ARMovie/RPL container carries — a picture built
/// from 2x2 blocks in a mixed Y'Pb'Pr' colourspace, each block a flat colour, a four-way brightness
/// pattern around one, or a clone of the block before it.
/// </summary>
/// <remarks>
/// Unlike its two predecessors this codec shares almost nothing with them beyond the container: where
/// Escape 122 and 124 are vector-quantised against a codebook, Escape 130 is a small, entirely
/// self-contained per-block prediction scheme with no codebook at all — see
/// <see cref="FileFormat.Rpl.RplContainer"/> and its own remarks for the container the two still share.
/// The bitstream itself is read and reconstructed in <see cref="Escape130FrameDecoder"/>, whose own
/// remarks record four things a straight reading of the format's own technical description gets wrong
/// and what real files settle them to instead.
/// <para/>
/// <b>Measured.</b> Five real files from <c>samples.ffmpeg.org/game-formats/rpl/</c> — 320x240,
/// 25 to 869 frames apiece, 1,297 pictures measured in all, one of them a Watch advertisement's own
/// intro carrying genuine colour rather than the near-flat greyscale most of the others open on — were
/// decoded here and by ffmpeg and compared against ffmpeg's own decoded <c>yuv420p</c> planes, sample by
/// sample, every frame of every file: Y, Cb and Cr are all bit-exact, with no difference at all, on
/// every one.
/// A direct YCbCr comparison is what settles it rather than an RGB one, for the same reason RoQ's own
/// decoder here is measured that way: this decoder is converted to RGB for its own output, but ffmpeg's
/// <c>swscale</c> RGB is not perfectly faithful to its own decoded planes, where the codec's native
/// colourspace is.
/// <para/>
/// <b>What is not implemented refuses and says so.</b> A picture whose width or height is not a whole
/// number of 2x2 blocks, and an ARMovie/RPL chunk stating more than one frame — the demuxer's own
/// business, and refused there rather than here, since no sample measured against this decoder ever
/// carries one.
/// </remarks>
public sealed class Escape130VideoDecoder : IVideoCodecDecoder<Escape130VideoDecoder> {

  private const int _EscapeCodecId = 130;
  private const int _FrameHeaderLength = 16;
  private const ushort _FrameMagic = 0x0130;

  private readonly Escape130FrameDecoder _frameDecoder;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Eidos Escape 130";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.Value == _EscapeCodecId;
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static Escape130VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"An Escape 130 video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    if (stream.Width % 2 != 0 || stream.Height % 2 != 0)
      throw new NotSupportedException(
        $"An Escape 130 video stream states a picture of {stream.Width}x{stream.Height}, which is not a "
        + "whole number of 2x2 blocks in both directions. Escape 130 codes nothing but whole blocks.");

    return new(stream.Width, stream.Height);
  }

  private Escape130VideoDecoder(int width, int height) => this._frameDecoder = new(width, height);

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _FrameHeaderLength)
      throw new InvalidDataException($"An Escape 130 video packet is {data.Length} bytes, short of its own sixteen-byte frame header.");

    var magic = BinaryPrimitives.ReadUInt16LittleEndian(data);
    if (magic != _FrameMagic)
      throw new InvalidDataException($"An Escape 130 video packet opens with 0x{magic:X4} rather than the codec's own 0x{_FrameMagic:X4} magic.");

    var frameSize = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
    if (frameSize < _FrameHeaderLength || frameSize > data.Length)
      throw new InvalidDataException(
        $"An Escape 130 video packet's own header states a frame size of {frameSize} bytes, which does "
        + $"not fit inside the {data.Length}-byte packet carrying it.");

    var payload = data[_FrameHeaderLength..(int)frameSize];
    frame = this._frameDecoder.DecodeFrame(payload);
    return true;
  }
}
