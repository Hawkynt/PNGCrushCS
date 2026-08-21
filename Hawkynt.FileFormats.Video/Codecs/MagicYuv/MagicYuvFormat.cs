using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>What a MagicYUV stream's samples mean.</summary>
internal enum MagicYuvColourSpace {

  /// <summary>One plane of luminance and nothing else.</summary>
  Grey,

  /// <summary>Blue, green and red planes, with an alpha plane after them where there is one.</summary>
  Rgb,

  /// <summary>Luminance and two chrominance planes, with an alpha plane where there is one.</summary>
  Yuv,
}

/// <summary>
/// The layout a MagicYUV four-character code stands for, and the header every frame opens with.
/// </summary>
/// <remarks>
/// Unlike every other codec in this package, MagicYUV states almost nothing in the stream
/// description: the sixteen bytes an AVI carries behind the <c>BITMAPINFOHEADER</c> are a copy of
/// the first bytes of the frame, and the frame is where the picture size, the slice height and the
/// tables all are. So the description is not read at all — the frame is, and it is checked against
/// the size the container states.
/// <para/>
/// There is no published description of any of this. Everything below was established by measuring
/// frames against the pictures they were made from.
/// </remarks>
internal sealed class MagicYuvFormat {

  /// <summary>The signature every frame opens with.</summary>
  internal static readonly byte[] Signature = [(byte)'M', (byte)'A', (byte)'G', (byte)'Y'];

  /// <summary>The one header size measured, and the only one whose fields are known.</summary>
  internal const int HEADER_SIZE = 32;

  /// <summary>
  /// The byte at offset eight, which is 7 in every frame measured.
  /// </summary>
  /// <remarks>
  /// Called a version here because that is what its position suggests, not because anything says so:
  /// the codec's author has described its releases publicly without ever referring to a bitstream
  /// version number, so the name is this decoder's guess at what the byte is for. What is certain is
  /// only that every frame reachable here holds 7 in it, and that a frame holding anything else is
  /// one nothing was measured against.
  /// </remarks>
  internal const int VERSION_BYTE = 7;

  private MagicYuvFormat(
    MagicYuvColourSpace colourSpace, int planeCount, int chromaHorizontalShift,
    int chromaVerticalShift, bool hasAlpha) {
    this.ColourSpace = colourSpace;
    this.PlaneCount = planeCount;
    this.ChromaHorizontalShift = chromaHorizontalShift;
    this.ChromaVerticalShift = chromaVerticalShift;
    this.HasAlpha = hasAlpha;
  }

  internal MagicYuvColourSpace ColourSpace { get; }
  internal int PlaneCount { get; }
  internal int ChromaHorizontalShift { get; }
  internal int ChromaVerticalShift { get; }
  internal bool HasAlpha { get; }

  /// <summary>Whether a plane is one of the two that carry chrominance.</summary>
  internal bool IsChroma(int plane)
    => this.ColourSpace == MagicYuvColourSpace.Yuv && plane is 1 or 2;

  /// <summary>The width and height of one plane, rounding a subsampled one up.</summary>
  /// <remarks>
  /// Up rather than down, because a subsampled plane has to cover the odd row and column too: a
  /// 33-wide 4:2:2 picture has 17 chrominance samples a row and not 16, and rounding the other way
  /// loses the last column of every such picture.
  /// </remarks>
  internal (int Width, int Height) PlaneSize(int plane, int width, int height) {
    if (!this.IsChroma(plane))
      return (width, height);

    var horizontal = 1 << this.ChromaHorizontalShift;
    var vertical = 1 << this.ChromaVerticalShift;
    return ((width + horizontal - 1) >> this.ChromaHorizontalShift,
      (height + vertical - 1) >> this.ChromaVerticalShift);
  }

  /// <summary>How many rows of one plane a slice covers.</summary>
  internal int SliceHeight(int plane, int frameSliceHeight) {
    if (!this.IsChroma(plane))
      return frameSliceHeight;

    var vertical = 1 << this.ChromaVerticalShift;
    return (frameSliceHeight + vertical - 1) >> this.ChromaVerticalShift;
  }

  /// <summary>The layout each four-character code stands for, refusing the rest by name.</summary>
  internal static MagicYuvFormat Of(CodecTag codec, int streamIndex) {
    var name = codec.ToString();
    return name switch {
      "M8RG" => new(MagicYuvColourSpace.Rgb, 3, 0, 0, false),
      "M8RA" => new(MagicYuvColourSpace.Rgb, 4, 0, 0, true),
      "M8Y0" => new(MagicYuvColourSpace.Yuv, 3, 1, 1, false),
      "M8Y2" => new(MagicYuvColourSpace.Yuv, 3, 1, 0, false),
      "M8Y4" => new(MagicYuvColourSpace.Yuv, 3, 0, 0, false),
      "M8YA" => new(MagicYuvColourSpace.Yuv, 4, 0, 0, true),
      "M8G0" => new(MagicYuvColourSpace.Grey, 1, 0, 0, false),
      "M8GA" => throw new NotSupportedException(
        $"Video stream {streamIndex} is M8GA — grey with an alpha channel. No encoder reachable here writes one, so there is no file against which a reading of it could be measured, and it is refused rather than decoded on the assumption that it is M8G0 with a second plane behind it."),
      "MAGY" => throw new NotSupportedException(
        $"Video stream {streamIndex} is MAGY, the single code MagicYUV used before it gave each pixel format one of its own. Which format such a file holds is not in its code, and no encoder reachable here writes one, so it is refused rather than guessed at."),
      "M0RG" or "M0RA" or "M0Y0" or "M0Y2" or "M0Y4" or "M0G0"
        or "M2RG" or "M2RA" or "M4RG" or "M4RA" => throw new NotSupportedException(
        $"Video stream {streamIndex} is {name}, one of MagicYUV's codes for samples deeper than eight bits — ten, twelve or fourteen. How those samples are packed is published nowhere and no encoder reachable here writes one, so it is refused by name rather than read as though its samples were bytes."),
      _ => throw new NotSupportedException(
        $"Video stream {streamIndex} is named {name}, which is not a MagicYUV code this reads."),
    };
  }
}
