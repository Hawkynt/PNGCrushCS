using System;
using System.Collections.Generic;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Bpg;

/// <summary>Assembles BPG (Better Portable Graphics) file bytes from a BpgFile model.</summary>
public static class BpgWriter {

  /// <summary>
  /// Encodes a picture as a BPG file: an HEVC intra picture made of PCM coding units, in the small
  /// header BPG wraps one in.
  /// </summary>
  /// <remarks>
  /// One authoring profile, and it is narrow on purpose: eight bits a sample, 4:4:4, in BPG's RGB
  /// colour space — which stores G, B and R in the three planes and applies no colour transform at
  /// all. It is lossless, and it is coded entirely from HEVC's PCM coding units, which carry their
  /// samples verbatim and so need none of the transform, quantisation or rate-distortion machinery a
  /// compressing encoder is made of. The files are large — a PCM picture is its own samples plus a
  /// few bytes a coding unit — and the point of them is that they are real HEVC any decoder reads,
  /// not that they are small.
  /// <para/>
  /// Everything else the format allows is refused rather than approximated: deeper bit depths, 4:2:0
  /// and 4:2:2 chroma, the YCbCr and YCgCo colour spaces, limited range, an alpha or CMYK plane,
  /// animation, and the extension tags that carry Exif, ICC, XMP and thumbnails.
  /// <para/>
  /// <c>pixel_format</c> 0, a single grey plane, is not among them either, and that one is the
  /// reference decoder's limit rather than this encoder's. <c>libbpg</c>'s <c>hls_pcm_sample</c>
  /// writes a coding unit's chroma blocks unconditionally, so a monochrome picture sends it into the
  /// two planes a monochrome frame has not got. A grey picture is therefore written as three equal
  /// planes, which costs three times the bytes and loses nothing.
  /// </remarks>
  public static BpgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(image), "BPG requires a positive image size.");

    var encoded = H265PcmStillCodec.EncodeBpgStill(image);

    var picture = new List<byte>(encoded.SequenceHeader.Length + encoded.Data.Length + 8);
    BpgUe7.Write(picture, encoded.SequenceHeader.Length);
    picture.AddRange(encoded.SequenceHeader);
    picture.AddRange(encoded.Data);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelFormat = BpgPixelFormat.YCbCr444,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = picture.ToArray(),
    };
  }

  public static byte[] ToBytes(BpgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), "BPG requires a positive image size.");
    if ((uint)file.PixelFormat > (uint)BpgPixelFormat.YCbCr422Mpeg2)
      throw new ArgumentOutOfRangeException(nameof(file),
        $"BPG pixel_format {(int)file.PixelFormat} is reserved; version 0.9.5 defines values 0 through 5.");
    if (file.BitDepth is < 8 or > 14)
      throw new NotSupportedException(
        $"BPG states its bit depth as bit_depth_minus_8 in four bits and caps it at 14; {file.BitDepth} is outside that.");
    if ((uint)file.ColorSpace > (uint)BpgColorSpace.YCbCrBT2020Ncl)
      throw new ArgumentOutOfRangeException(nameof(file),
        $"BPG color_space {(int)file.ColorSpace} is reserved; version 0.9.5 defines values 0 through 4.");
    if (file.PixelFormat == BpgPixelFormat.Grayscale && file.ColorSpace != BpgColorSpace.YCbCrBT601)
      throw new NotSupportedException(
        "A grayscale BPG picture has one plane and therefore no colour matrix: color_space must be zero.");

    var output = new List<byte>();
    output.AddRange(BpgFile.Magic);

    var bitDepthMinus8 = file.BitDepth - 8;
    output.Add((byte)((((int)file.PixelFormat & 0x07) << 5) | ((file.HasAlpha ? 1 : 0) << 4) | (bitDepthMinus8 & 0x0f)));
    output.Add((byte)(
      (((int)file.ColorSpace & 0x0f) << 4) |
      ((file.ExtensionPresent ? 1 : 0) << 3) |
      ((file.HasAlpha2 ? 1 : 0) << 2) |
      ((file.LimitedRange ? 1 : 0) << 1) |
      (file.IsAnimation ? 1 : 0)));

    BpgUe7.Write(output, file.Width);
    BpgUe7.Write(output, file.Height);
    BpgUe7.Write(output, file.PixelData.Length);

    if (file.ExtensionPresent) {
      BpgUe7.Write(output, file.ExtensionData.Length);
      output.AddRange(file.ExtensionData);
    }

    output.AddRange(file.PixelData);
    return [.. output];
  }
}
