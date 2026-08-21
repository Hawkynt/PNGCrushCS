using System;
using System.IO;

namespace FileFormat.Codecs.Theora;

/// <summary>How the chroma planes are sampled relative to the luma plane.</summary>
/// <remarks>Theora specification Table 6.4. Value 1 is reserved and refused by name.</remarks>
internal enum TheoraPixelFormat {

  Yuv420 = 0,
  Reserved = 1,
  Yuv422 = 2,
  Yuv444 = 3,
}

/// <summary>
/// The first of Theora's three setup headers: what the stream is and what shape its pictures are.
/// </summary>
/// <remarks>
/// Theora specification section 6.2, Figure 6.2. Everything in it but the granule shift and the
/// nominal bit rate is needed to decode a frame, and the two that are not are needed by whoever
/// wraps the stream in a container.
/// <para/>
/// The frame and the picture are two different rectangles and both matter. The frame is what is
/// coded — a whole number of macro blocks in each direction, always — and the picture is the part of
/// it that is meant to be seen, offset from the frame's lower-left corner by up to 255 pixels. The
/// portions outside the picture hold real coded samples that later frames predict from, so they are
/// decoded like any other and cropped away only at the end.
/// </remarks>
internal sealed class TheoraIdentificationHeader {

  internal required int VersionMajor { get; init; }
  internal required int VersionMinor { get; init; }
  internal required int VersionRevision { get; init; }

  /// <summary>The width of the coded frame in macro blocks; the frame is sixteen times this.</summary>
  internal required int FrameMacroBlocksWide { get; init; }

  internal required int FrameMacroBlocksHigh { get; init; }

  /// <summary>The width of the displayable picture region in pixels.</summary>
  internal required int PictureWidth { get; init; }

  internal required int PictureHeight { get; init; }

  /// <summary>Where the picture's lower-left corner sits inside the frame.</summary>
  internal required int PictureX { get; init; }

  internal required int PictureY { get; init; }

  internal required uint FrameRateNumerator { get; init; }
  internal required uint FrameRateDenominator { get; init; }
  internal required uint AspectNumerator { get; init; }
  internal required uint AspectDenominator { get; init; }
  internal required int ColorSpace { get; init; }
  internal required uint NominalBitrate { get; init; }
  internal required int Quality { get; init; }

  /// <summary>How many bits of an Ogg granule position hold the frames since the last keyframe.</summary>
  internal required int KeyFrameGranuleShift { get; init; }

  internal required TheoraPixelFormat PixelFormat { get; init; }

  /// <summary>The coded frame's width in pixels, which is sixteen macro blocks' worth per unit.</summary>
  internal int FrameWidth => this.FrameMacroBlocksWide * 16;

  internal int FrameHeight => this.FrameMacroBlocksHigh * 16;

  /// <summary>The width of the chroma planes in pixels — Table 7.89.</summary>
  internal int ChromaWidth => this.PixelFormat == TheoraPixelFormat.Yuv444 ? this.FrameWidth : this.FrameWidth / 2;

  /// <summary>The height of the chroma planes in pixels — Table 7.89.</summary>
  internal int ChromaHeight => this.PixelFormat == TheoraPixelFormat.Yuv420 ? this.FrameHeight / 2 : this.FrameHeight;

  /// <summary>
  /// Reads the identification header out of a packet whose type byte has already been taken.
  /// </summary>
  internal static TheoraIdentificationHeader Read(TheoraBitReader reader) {
    var major = (int)reader.ReadBits(8);
    var minor = (int)reader.ReadBits(8);
    var revision = (int)reader.ReadBits(8);

    // Refused by name rather than read hopefully. Version 3.2 is the format this specification
    // describes; anything else is a bitstream laid out to rules this decoder does not have, and
    // reading it to these rules would give a picture nobody could tell was wrong.
    if (major != 3 || minor != 2)
      throw new NotSupportedException(
        $"This Theora stream states bitstream version {major}.{minor}.{revision}, where the Theora I specification defines 3.2 and this decoder reads no other.");

    var macroBlocksWide = (int)reader.ReadBits(16);
    var macroBlocksHigh = (int)reader.ReadBits(16);
    if (macroBlocksWide <= 0 || macroBlocksHigh <= 0)
      throw new InvalidDataException(
        $"The identification header states a coded frame of {macroBlocksWide} by {macroBlocksHigh} macro blocks, and both MUST be greater than zero.");

    // Twenty bits would do; twenty-four are read, to keep the header octet-aligned.
    var pictureWidth = (int)reader.ReadBits(24);
    var pictureHeight = (int)reader.ReadBits(24);
    var pictureX = (int)reader.ReadBits(8);
    var pictureY = (int)reader.ReadBits(8);

    var frameWidth = macroBlocksWide * 16;
    var frameHeight = macroBlocksHigh * 16;
    if (pictureWidth > frameWidth || pictureHeight > frameHeight)
      throw new InvalidDataException(
        $"The identification header states a picture of {pictureWidth}x{pictureHeight} inside a coded frame of {frameWidth}x{frameHeight}, which does not contain it.");

    if (pictureX + pictureWidth > frameWidth || pictureY + pictureHeight > frameHeight)
      throw new InvalidDataException(
        $"The identification header offsets a {pictureWidth}x{pictureHeight} picture to ({pictureX}, {pictureY}) in a {frameWidth}x{frameHeight} frame, which puts part of it outside.");

    var rateNumerator = reader.ReadBits(32);
    var rateDenominator = reader.ReadBits(32);
    if (rateNumerator == 0 || rateDenominator == 0)
      throw new InvalidDataException(
        $"The identification header states a frame rate of {rateNumerator}/{rateDenominator}, and both parts MUST be greater than zero.");

    var aspectNumerator = reader.ReadBits(24);
    var aspectDenominator = reader.ReadBits(24);
    var colorSpace = (int)reader.ReadBits(8);
    var nominalBitrate = reader.ReadBits(24);
    var quality = (int)reader.ReadBits(6);
    var granuleShift = (int)reader.ReadBits(5);
    var pixelFormat = (TheoraPixelFormat)reader.ReadBits(2);
    var reserved = reader.ReadBits(3);

    if (pixelFormat == TheoraPixelFormat.Reserved)
      throw new NotSupportedException(
        "This Theora stream states pixel format 1, which Table 6.4 of the specification reserves and gives no meaning.");

    // The specification requires a decoder to refuse a stream whose reserved bits are set: they are
    // place holders for features a future version may add without changing the version number, so a
    // decoder that ignored them would silently misread the first stream to use one.
    if (reserved != 0)
      throw new NotSupportedException(
        $"The identification header's three reserved bits are {reserved} rather than zero, so this stream uses a feature the Theora I specification does not define.");

    reader.EnsureComplete("the identification header");

    return new() {
      VersionMajor = major,
      VersionMinor = minor,
      VersionRevision = revision,
      FrameMacroBlocksWide = macroBlocksWide,
      FrameMacroBlocksHigh = macroBlocksHigh,
      PictureWidth = pictureWidth,
      PictureHeight = pictureHeight,
      PictureX = pictureX,
      PictureY = pictureY,
      FrameRateNumerator = rateNumerator,
      FrameRateDenominator = rateDenominator,
      AspectNumerator = aspectNumerator,
      AspectDenominator = aspectDenominator,
      ColorSpace = colorSpace,
      NominalBitrate = nominalBitrate,
      Quality = quality,
      KeyFrameGranuleShift = granuleShift,
      PixelFormat = pixelFormat,
    };
  }
}
