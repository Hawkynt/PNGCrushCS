using System;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// State for JPEG XL's recursively progressive low-frequency pyramid.
/// </summary>
/// <remarks>
/// A hidden <see cref="JxlFrameType.DcFrame"/> at level N writes canonical XYB
/// into <c>dc_frames[N-1]</c>. A later frame whose <c>kUseDcFrame</c> flag is set
/// and whose own level is L reads <c>dc_frames[L]</c>. Thus a regular level-zero
/// frame consumes the level-one DC frame, while a level-one DC frame can itself
/// consume a level-two frame.
/// </remarks>
internal sealed class JxlProgressiveDcState {
  private const ulong _kUseDcFrame = 0x20;
  private const ulong _UnsupportedFeatures = 1 | 2 | 16; // noise, patches, splines
  private readonly float[][]?[] _levels = new float[4][][];

  /// <summary>Return the DC image consumed by a frame at <paramref name="consumerLevel"/>.</summary>
  public float[][] RequireForConsumerLevel(uint consumerLevel) {
    if (consumerLevel >= _levels.Length)
      throw new InvalidDataException(
        $"JPEG XL DC level {consumerLevel} cannot consume a deeper level in the four-level DC pyramid.");

    return _levels[consumerLevel]
           ?? throw new InvalidDataException(
             $"JPEG XL frame requires progressive DC level {consumerLevel + 1}, but that hidden frame was not decoded first.");
  }

  /// <summary>
  /// Decode one hidden DC frame, store its canonical XYB output in the slot a
  /// future frame will consume, and return the byte offset of the next frame.
  /// </summary>
  public int DecodeAndStore(
    byte[] codestream,
    JxlBitReader reader,
    JxlImageMetadata imageMetadata,
    JxlSpecFrameHeader frame,
    int imageWidth,
    int imageHeight
  ) {
    ArgumentNullException.ThrowIfNull(codestream);
    ArgumentNullException.ThrowIfNull(reader);
    ArgumentNullException.ThrowIfNull(imageMetadata);
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.FrameType != JxlFrameType.DcFrame)
      throw new ArgumentException("Only hidden DC frames can be stored as progressive DC state.", nameof(frame));
    if (frame.DcLevel is < 1 or > 4)
      throw new InvalidDataException($"JPEG XL DC frame states invalid level {frame.DcLevel}.");
    if ((frame.Flags & _UnsupportedFeatures) != 0)
      throw new NotSupportedException("A progressive DC frame carries noise, patches or splines, which are not valid DC-pyramid input here.");

    // FrameHeader::ToFrameDimensions: every DC level reduces both dimensions
    // by another factor of eight.
    var shift = checked(3 * (int)frame.DcLevel);
    var divisor = 1 << shift;
    var width = (imageWidth + divisor - 1) / divisor;
    var height = (imageHeight + divisor - 1) / divisor;
    if (width <= 0 || height <= 0)
      throw new InvalidDataException("A progressive DC frame has no pixels at its stated level.");

    var groupDim = 128 << (int)frame.GroupSizeShift;
    var groupsX = (width + groupDim - 1) / groupDim;
    var groupsY = (height + groupDim - 1) / groupDim;
    var numGroups = checked(groupsX * groupsY);
    var lfGroupDim = checked(groupDim * 8);
    var numDcGroups = checked(((width + lfGroupDim - 1) / lfGroupDim)
                              * ((height + lfGroupDim - 1) / lfGroupDim));
    var toc = JxlFrameToc.Decode(reader, numGroups, (int)frame.NumPasses, numDcGroups);
    var frameBody = checked((int)(reader.BitsRead / 8));

    // DequantMatrices::DecodeDC is present for every frame and precedes the
    // encoding-specific payload. For a modular XYB DC frame its values turn
    // stored [Y, X, B-Y] samples back into canonical XYB.
    var dcQuant = JxlFrameQuantizer.ReadDcQuantization(reader);
    var bitDepth = (int)imageMetadata.BitDepth.BitsPerSample;

    float[][] planes;
    if (frame.Encoding == JxlFrameEncoding.Modular) {
      var isGray = imageMetadata.ColorEncoding.ColorSpace == 1;
      var baseChannels = isGray ? 1 : 3;
      var totalChannels = checked(baseChannels + (int)imageMetadata.NumExtraChannels);
      var decoded = numGroups == 1
        ? JxlModularSpecDecoder.Decode(reader, width, height, totalChannels, bitDepth, isTopLevelFrame: true)
        : JxlModularSpecDecoder.DecodeMultiGroup(
          codestream, reader, width, height, totalChannels, bitDepth,
          groupDim, groupsX, groupsY, numDcGroups, (int)frame.NumPasses, toc, frameBody);

      if (frame.ColorTransform != JxlColorTransform.Xyb || decoded.Channels.Length < 3)
        throw new NotSupportedException("A progressive DC frame is not a three-channel XYB modular frame.");

      var count = checked(width * height);
      planes = [new float[count], new float[count], new float[count]];
      var yStored = decoded.Channels[0].Pixels;
      var xStored = decoded.Channels[1].Pixels;
      var bMinusYStored = decoded.Channels[2].Pixels;
      if (yStored.Length < count || xStored.Length < count || bMinusYStored.Length < count)
        throw new InvalidDataException("A progressive DC frame decoded fewer samples than its dimensions require.");

      // The hidden DC image is encoded as modular XYB in storage order
      // [Y, X, B-Y]. This is the same conversion used for modular XYB
      // reference-only frames elsewhere in the reader.
      for (var i = 0; i < count; ++i) {
        var y = yStored[i];
        planes[0][i] = xStored[i] * dcQuant[0];
        planes[1][i] = y * dcQuant[1];
        planes[2][i] = (bMinusYStored[i] + y) * dcQuant[2];
      }
    } else {
      var sourceDc = (frame.Flags & _kUseDcFrame) != 0
        ? RequireForConsumerLevel(frame.DcLevel)
        : null;
      var decoded = JxlVarDctSpecDecoder.Decode(
        reader,
        width,
        height,
        bitDepth,
        gaborishParams: frame.GaborishParameters,
        epfParams: frame.EpfParameters,
        dcQuant: dcQuant,
        xQmScale: frame.XQmScale,
        bQmScale: frame.BQmScale,
        codestream: codestream,
        toc: toc,
        frameBody: frameBody,
        groupSizeOverride: groupDim,
        numDcGroups: numDcGroups,
        numExtraChannels: (int)imageMetadata.NumExtraChannels,
        frameFlags: frame.Flags,
        numPasses: (int)frame.NumPasses,
        passShifts: frame.PassShifts,
        passDownsample: frame.PassDownsample,
        passLastPass: frame.PassLastPass,
        dcFrame: sourceDc);
      planes = decoded.Channels;
    }

    _levels[frame.DcLevel - 1] = planes;

    var totalSize = 0;
    foreach (var size in toc.SectionSizes)
      totalSize = checked(totalSize + size);
    var next = checked(frameBody + totalSize);
    if (next <= frameBody || next > codestream.Length)
      throw new InvalidDataException("A progressive DC frame's sections run past the end of the codestream.");
    return next;
  }
}
