using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl;

/// <summary>Reads JPEG XL bare codestreams and ISO BMFF containers.</summary>
public static class JpegXlReader {

  private const byte _CodestreamByte0 = 0xFF;
  private const byte _CodestreamByte1 = 0x0A;
  private const int _MinSize = 4;

  public static JpegXlFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG XL file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JpegXlFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static JpegXlFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static JpegXlFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MinSize)
      throw new InvalidDataException("Data too small for a valid JPEG XL file.");

    var bytes = data.ToArray();
    if (!_TryExtractCodestream(bytes, out var codestream, out var brand))
      throw new InvalidDataException("Input is neither a JPEG XL bare codestream nor a valid JPEG XL container.");

    // A modular frame comes back sample for sample. A VarDCT frame comes back
    // within one eight-bit level of libjxl, which is where the difference
    // between two float pipelines lands once the coefficients agree: measured
    // before rounding it is a ten-thousandth of a level, and what shows at eight
    // bits is a sample sitting nearer the boundary than that. Holding it back on
    // that ground would mean refusing every lossy file forever, since no decoder
    // that is not libjxl's own arithmetic gets closer.
    if (_TryDecodeSpec(codestream, out var metadata, out var imageMetadata, out var decoded)
        && _TryPackDecoded(metadata, imageMetadata!, decoded, brand, out var file))
      return file;

    // Compatibility with files produced by early versions of this project. The fallback is deliberately
    // narrow: the private layout must have a 1/3 component marker and either an exact raw raster length
    // or the historical 'M' modular marker. Arbitrary real JXL bitstreams are never returned as pixels.
    if (_TryParseLegacySynthetic(codestream, brand, out file))
      return file;

    throw new NotSupportedException("The JPEG XL container and metadata are valid, but its pixel coding uses syntax not yet decoded by this implementation.");
  }

  /// <summary>Read JPEG XL dimensions and frame metadata without requiring pixel decode.</summary>
  public static bool TryReadSpecMetadata(byte[] data, out JpegXlSpecMetadata metadata) {
    metadata = default;
    ArgumentNullException.ThrowIfNull(data);
    if (!_TryExtractCodestream(data, out var codestream, out _))
      return false;

    var width = 0;
    var height = 0;
    JxlImageMetadata? imageMetadata = null;
    try {
      var reader = new JxlBitReader(codestream, 2);
      (width, height) = JxlSizeHeader.Decode(reader);
      imageMetadata = JxlImageMetadata.Decode(reader);
      JxlCustomTransformData.Decode(reader, imageMetadata.XybEncoded);
      reader.ZeroPadToByte();
      var frame = JxlSpecFrameHeader.Decode(reader, imageMetadata, width, height);
      metadata = _Metadata(width, height, imageMetadata, frame);
      return true;
    } catch (InvalidDataException) {
      _PopulatePartial(width, height, imageMetadata, ref metadata);
      return width > 0 && height > 0;
    } catch (InvalidOperationException) {
      _PopulatePartial(width, height, imageMetadata, ref metadata);
      return width > 0 && height > 0;
    } catch (NotImplementedException) {
      _PopulatePartial(width, height, imageMetadata, ref metadata);
      return width > 0 && height > 0;
    } catch (NotSupportedException) {
      _PopulatePartial(width, height, imageMetadata, ref metadata);
      return width > 0 && height > 0;
    } catch (ArgumentOutOfRangeException) {
      _PopulatePartial(width, height, imageMetadata, ref metadata);
      return width > 0 && height > 0;
    }
  }

  /// <summary>Attempt full standards-based JPEG XL pixel decode.</summary>
  public static bool TryReadSpecImage(byte[] data, out JpegXlSpecMetadata metadata, out object? rawImage) {
    metadata = default;
    rawImage = null;
    ArgumentNullException.ThrowIfNull(data);
    if (!_TryExtractCodestream(data, out var codestream, out _))
      return false;
    return _TryDecodeSpec(codestream, out metadata, out _, out rawImage);
  }

  /// <summary>Attempt full decode and convert the result to byte-packed RGB24.</summary>
  public static bool TryReadSpecRgb24(byte[] data, out int width, out int height, out byte[]? rgb24) {
    width = 0;
    height = 0;
    rgb24 = null;
    if (!_TryExtractCodestream(data, out var codestream, out _))
      return false;
    if (!_TryDecodeSpec(codestream, out var metadata, out var imageMetadata, out var image))
      return false;

    width = metadata.Width;
    height = metadata.Height;
    var pixelCount = checked(width * height);

    if (image is JxlVarDctImage vardct) {
      if (vardct.Channels.Length < 3)
        return false;
      rgb24 = JxlXybColorTransform.XybPlanesToRgb24(
        vardct.Channels[0], vardct.Channels[1], vardct.Channels[2], vardct.Width, vardct.Height);
      return rgb24.Length == checked(pixelCount * 3);
    }

    if (image is not JxlModularImage modular)
      return false;
    var isGray = imageMetadata!.ColorEncoding.ColorSpace == 1;
    var baseChannels = isGray ? 1 : 3;
    if (modular.Channels.Length < baseChannels)
      return false;

    rgb24 = new byte[checked(pixelCount * 3)];
    for (var i = 0; i < pixelCount; ++i) {
      if (isGray) {
        var value = _ToByte(modular.Channels[0].Pixels[i], metadata.BitsPerSample);
        rgb24[i * 3] = value;
        rgb24[i * 3 + 1] = value;
        rgb24[i * 3 + 2] = value;
      } else {
        rgb24[i * 3] = _ToByte(modular.Channels[0].Pixels[i], metadata.BitsPerSample);
        rgb24[i * 3 + 1] = _ToByte(modular.Channels[1].Pixels[i], metadata.BitsPerSample);
        rgb24[i * 3 + 2] = _ToByte(modular.Channels[2].Pixels[i], metadata.BitsPerSample);
      }
    }
    return true;
  }

  private static bool _TryDecodeSpec(
    byte[] codestream,
    out JpegXlSpecMetadata metadata,
    out JxlImageMetadata? imageMetadata,
    out object? image
  ) {
    metadata = default;
    imageMetadata = null;
    image = null;
    if (codestream.Length < 4 || codestream[0] != _CodestreamByte0 || codestream[1] != _CodestreamByte1)
      return false;

    try {
      var reader = new JxlBitReader(codestream, 2);
      var (width, height) = JxlSizeHeader.Decode(reader);
      imageMetadata = JxlImageMetadata.Decode(reader);
      // The bundle between the metadata and the first frame is one bit when the
      // file leaves it alone, and skipping it only shows up when the metadata
      // ended on a byte boundary and the alignment below has nothing to swallow.
      JxlCustomTransformData.Decode(reader, imageMetadata.XybEncoded);
      reader.ZeroPadToByte();

      // Frames kept aside for a later one to draw from are read first and never
      // shown. A file that patches itself opens with one: the thing that
      // repeats, coded once.
      var references = new float[_ReferenceSlots][][];
      var referenceSizes = new (int Width, int Height)[_ReferenceSlots];
      var frame = JxlSpecFrameHeader.Decode(reader, imageMetadata, width, height);
      while (frame.FrameType == JxlFrameType.ReferenceOnly) {
        var next = _DecodeReferenceFrame(
          codestream, reader, imageMetadata, frame, references, referenceSizes);
        reader = new JxlBitReader(codestream, next);
        frame = JxlSpecFrameHeader.Decode(reader, imageMetadata, width, height);
      }

      metadata = _Metadata(width, height, imageMetadata, frame);

      // A frame that is not the last one is a layer, not the picture: what a
      // caller should see is every frame composed in order, with the blending
      // each states.
      if (!frame.IsLast) {
        image = _DecodeComposed(codestream, imageMetadata, width, height);
        return image != null;
      }

      // A group is 128 pixels shifted by what the frame header states, and a
      // low-frequency group is eight of those across.
      var groupDim = 128 << (int)frame.GroupSizeShift;
      var numGroupsX = (width + groupDim - 1) / groupDim;
      var numGroupsY = (height + groupDim - 1) / groupDim;
      var numGroups = checked(numGroupsX * numGroupsY);
      var lfGroupDim = groupDim * 8;
      var numDcGroups = checked(((width + lfGroupDim - 1) / lfGroupDim) * ((height + lfGroupDim - 1) / lfGroupDim));
      var toc = JxlFrameToc.Decode(reader, numGroups, (int)frame.NumPasses, numDcGroups);

      // The table of contents ends byte-aligned on the frame's first section.
      var frameBody = checked((int)(reader.BitsRead / 8));

      // The frame's global section opens with whatever it states in its header
      // flags — patches, then splines, then noise — and only then the
      // quantization tables. Reading them in any other order, or not at all,
      // puts every field behind them at the wrong offset.
      var patches = (frame.Flags & _FlagPatches) != 0
        ? JxlPatches.Decode(reader, width, height, (int)imageMetadata.NumExtraChannels, referenceSizes)
        : null;

      var splines = (frame.Flags & _FlagSplines) != 0
        ? JxlSplines.Decode(reader, checked((long)width * height))
        : null;

      // The noise field is generated per group from a seed that includes the
      // group's position, and a group's edges need the numbers of the group
      // next to it. Only a frame that is one group is worked out here.
      float[]? noiseLut = null;
      if ((frame.Flags & _FlagNoise) != 0) {
        if (numGroups != 1)
          throw new NotSupportedException(
            "This JPEG XL frame adds noise and is coded in several groups, which this decoder does not follow.");
        noiseLut = JxlNoise.Decode(reader);
      }

      // Every frame carries the DC quantization defaults, including modular frames.
      var dcQuant = JxlFrameQuantizer.ReadDcQuantization(reader);

      if (frame.Encoding == JxlFrameEncoding.Modular) {
        var isGray = imageMetadata.ColorEncoding.ColorSpace == 1;
        var baseChannels = isGray ? 1 : 3;
        var totalChannels = checked(baseChannels + (int)imageMetadata.NumExtraChannels);
        var bits = (int)imageMetadata.BitDepth.BitsPerSample;

        image = numGroups == 1
          ? JxlModularSpecDecoder.Decode(reader, width, height, totalChannels, bits, isTopLevelFrame: true)
          : JxlModularSpecDecoder.DecodeMultiGroup(
            codestream, reader, width, height, totalChannels, bits,
            groupDim, numGroupsX, numGroupsY, numDcGroups, (int)frame.NumPasses, toc, frameBody);

        if (!_ValidateDecodedImage(image, width, height, totalChannels))
          return false;

        // A modular frame states no colour correlation of its own, so a spline's
        // colour is taken as it stands.
        if (splines != null)
          _DrawSplines((JxlModularImage)image!, splines, width, height, bits, isGray);
        return true;
      }

      image = JxlVarDctSpecDecoder.Decode(
        reader,
        width,
        height,
        bitDepth: (int)imageMetadata.BitDepth.BitsPerSample,
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
        frameFlags: frame.Flags);

      // Patches are stamped on after the filters and before the noise, which is
      // the order libjxl's pipeline puts them in.
      if (patches != null && image is JxlVarDctImage patched)
        JxlPatches.Apply(
          patched.Channels, patched.Width, patched.Height, patches, references, referenceSizes,
          _PremultipliedAlphas(imageMetadata));

      // Noise goes on after the smoothing and edge-preserving filters and
      // before the colour transform, which is exactly where the VarDCT decoder
      // leaves off.
      if (noiseLut != null && image is JxlVarDctImage noised)
        JxlNoise.Apply(
          noised.Channels, noised.Width, noised.Height, noiseLut,
          JxlColorCorrelationMap.DefaultYtoXRatio, JxlColorCorrelationMap.DefaultYtoBRatio);

      return image is JxlVarDctImage vardct
             && vardct.Width == width
             && vardct.Height == height
             && vardct.Channels.Length >= 3
             && vardct.Channels[0].Length >= checked(width * height)
             && vardct.Channels[1].Length >= checked(width * height)
             && vardct.Channels[2].Length >= checked(width * height);
    } catch (InvalidDataException) {
      return false;
    } catch (InvalidOperationException) {
      return false;
    } catch (NotImplementedException) {
      return false;
    } catch (NotSupportedException) {
      return false;
    } catch (ArgumentOutOfRangeException) {
      return false;
    } catch (OverflowException) {
      return false;
    }
  }

  /// <summary>libjxl <c>FrameHeader::Flags</c>.</summary>
  private const ulong _FlagNoise = 1;
  private const ulong _FlagPatches = 2;
  private const ulong _FlagSplines = 16;

  /// <summary>How many frames a file may keep aside to draw over later.</summary>
  private const int _ReferenceSlots = 4;

  /// <summary>
  /// Read every frame in the file and compose them into the picture.
  /// </summary>
  /// <remarks>
  /// Each frame states where it sits, how it combines with what is under it,
  /// and which of up to four kept-aside frames that is. The result of each is
  /// what the next one draws over, and the last frame's result is the picture.
  ///
  /// <para>Composition is in float over the colour planes followed by the extra
  /// ones, because the alpha a frame blends by is one of the extra channels.
  /// Only modular frames are composed: blending VarDCT frames happens in XYB
  /// before the colour transform, and nothing here has been measured against
  /// libjxl doing that, so such a file is refused by name instead.</para>
  /// </remarks>
  private static object? _DecodeComposed(byte[] codestream, JxlImageMetadata imageMetadata, int width, int height) {
    var isGray = imageMetadata.ColorEncoding.ColorSpace == 1;
    var baseChannels = isGray ? 1 : 3;
    var extraChannels = (int)imageMetadata.NumExtraChannels;
    var totalChannels = checked(baseChannels + extraChannels);
    var bits = (int)imageMetadata.BitDepth.BitsPerSample;
    var scale = 1.0f / ((1 << Math.Clamp(bits, 1, 30)) - 1);

    // Three colour planes even for a grey picture, so that a spline's colour
    // and a blend have somewhere to go.
    var planeCount = 3 + extraChannels;
    var alphaPlane = _AlphaPlane(imageMetadata);
    var premultiplied = alphaPlane >= 3
                        && imageMetadata.ExtraChannelInfo[alphaPlane - 3].AlphaAssociated;

    var references = new float[_ReferenceSlots][][];
    float[][]? composed = null;

    var at = 0;
    for (var frameIndex = 0; ; ++frameIndex) {
      if (frameIndex > 512)
        throw new NotSupportedException("This JPEG XL file states more frames than this decoder will follow.");

      JxlBitReader reader;
      JxlSpecFrameHeader frame;
      if (frameIndex == 0) {
        reader = new JxlBitReader(codestream, 2);
        JxlSizeHeader.Decode(reader);
        JxlImageMetadata.Decode(reader);
        JxlCustomTransformData.Decode(reader, imageMetadata.XybEncoded);
        reader.ZeroPadToByte();
      } else
        reader = new JxlBitReader(codestream, at);

      frame = JxlSpecFrameHeader.Decode(reader, imageMetadata, width, height);
      if (frame.Encoding != JxlFrameEncoding.Modular)
        throw new NotSupportedException(
          "This JPEG XL file has several frames and at least one is lossy; composing those is not implemented.");
      if (frame.FrameType != JxlFrameType.Regular)
        throw new NotSupportedException(
          $"This JPEG XL file has a {frame.FrameType} frame, which this decoder does not compose.");

      var frameWidth = frame.FrameWidth > 0 ? frame.FrameWidth : width;
      var frameHeight = frame.FrameHeight > 0 ? frame.FrameHeight : height;

      var groupDim = 128 << (int)frame.GroupSizeShift;
      var numGroupsX = (frameWidth + groupDim - 1) / groupDim;
      var numGroupsY = (frameHeight + groupDim - 1) / groupDim;
      var numGroups = checked(numGroupsX * numGroupsY);
      var lfGroupDim = groupDim * 8;
      var numDcGroups = checked(((frameWidth + lfGroupDim - 1) / lfGroupDim)
                                * ((frameHeight + lfGroupDim - 1) / lfGroupDim));
      var toc = JxlFrameToc.Decode(reader, numGroups, (int)frame.NumPasses, numDcGroups);
      var frameBody = checked((int)(reader.BitsRead / 8));

      if ((frame.Flags & _FlagPatches) != 0)
        throw new NotSupportedException("This JPEG XL frame overlays patches, which this decoder does not read yet.");

      var splines = (frame.Flags & _FlagSplines) != 0
        ? JxlSplines.Decode(reader, checked((long)frameWidth * frameHeight))
        : null;

      if ((frame.Flags & _FlagNoise) != 0)
        throw new NotSupportedException("This JPEG XL frame adds noise, which this decoder does not read yet.");

      JxlFrameQuantizer.ReadDcQuantization(reader);

      var decoded = numGroups == 1
        ? JxlModularSpecDecoder.Decode(reader, frameWidth, frameHeight, totalChannels, bits, isTopLevelFrame: true)
        : JxlModularSpecDecoder.DecodeMultiGroup(
          codestream, reader, frameWidth, frameHeight, totalChannels, bits,
          groupDim, numGroupsX, numGroupsY, numDcGroups, (int)frame.NumPasses, toc, frameBody);
      if (!_ValidateDecodedImage(decoded, frameWidth, frameHeight, totalChannels))
        return null;

      if (splines != null)
        _DrawSplines((JxlModularImage)decoded!, splines, frameWidth, frameHeight, bits, isGray);

      var foreground = _ToPlanes(decoded, frameWidth, frameHeight, planeCount, baseChannels, scale);
      composed = JxlFrameComposer.Compose(
        references[frame.BlendSource % _ReferenceSlots], foreground, planeCount,
        width, height, frameWidth, frameHeight, frame.OriginX, frame.OriginY,
        frame.BlendMode, alphaPlane, frame.BlendClamp, premultiplied);

      if (frame.SaveAsReference != 0)
        references[frame.SaveAsReference % _ReferenceSlots] = composed;

      if (frame.IsLast)
        break;

      // An animation is not a stack of layers: its frames follow one another in
      // time, and what a still picture means for one is the first frame a
      // viewer would show. A frame of no duration is a layer of the next one,
      // so composition carries on through those and stops at the first frame
      // that is actually shown.
      if (imageMetadata.HaveAnimation && frame.Duration > 0)
        break;

      var total = 0;
      foreach (var size in toc.SectionSizes)
        total = checked(total + size);
      at = checked(frameBody + total);
      if (at <= 0 || at >= codestream.Length)
        throw new InvalidDataException("A frame's sections run past the end of the file.");
    }

    return composed == null
      ? null
      : new JxlComposedImage {
        Width = width,
        Height = height,
        Planes = composed,
        AlphaPlane = alphaPlane,
      };
  }

  /// <summary>Whether each extra channel's alpha is already carried in the colour.</summary>
  private static bool[] _PremultipliedAlphas(JxlImageMetadata imageMetadata) {
    var flags = new bool[imageMetadata.ExtraChannelInfo.Length];
    for (var i = 0; i < flags.Length; ++i)
      flags[i] = imageMetadata.ExtraChannelInfo[i].AlphaAssociated;
    return flags;
  }

  /// <summary>
  /// Read a frame that is kept aside rather than shown, and put it in the slot
  /// it names.
  /// </summary>
  /// <remarks>
  /// A kept-aside frame is stored the way the frame that draws from it will
  /// want it, which for a picture coded in XYB means XYB and not colour. A
  /// modular frame carrying XYB states it as Y, X and B minus Y, each in units
  /// of that channel's own DC quantisation step — so the planes have to be put
  /// back in order and the Y added into the B before anything can be stamped
  /// from them.
  /// </remarks>
  /// <returns>The offset of the frame that follows.</returns>
  private static int _DecodeReferenceFrame(
    byte[] codestream,
    JxlBitReader reader,
    JxlImageMetadata imageMetadata,
    JxlSpecFrameHeader frame,
    float[][]?[] references,
    (int Width, int Height)[] referenceSizes
  ) {
    if (frame.Encoding != JxlFrameEncoding.Modular)
      throw new NotSupportedException("This JPEG XL file keeps a lossy frame aside, which this decoder does not read.");

    var frameWidth = frame.FrameWidth;
    var frameHeight = frame.FrameHeight;
    if (frameWidth <= 0 || frameHeight <= 0)
      throw new InvalidDataException("A kept-aside frame states no size.");

    var groupDim = 128 << (int)frame.GroupSizeShift;
    var numGroupsX = (frameWidth + groupDim - 1) / groupDim;
    var numGroupsY = (frameHeight + groupDim - 1) / groupDim;
    var numGroups = checked(numGroupsX * numGroupsY);
    var lfGroupDim = groupDim * 8;
    var numDcGroups = checked(((frameWidth + lfGroupDim - 1) / lfGroupDim)
                              * ((frameHeight + lfGroupDim - 1) / lfGroupDim));
    var toc = JxlFrameToc.Decode(reader, numGroups, (int)frame.NumPasses, numDcGroups);
    var frameBody = checked((int)(reader.BitsRead / 8));

    if ((frame.Flags & (_FlagPatches | _FlagSplines | _FlagNoise)) != 0)
      throw new NotSupportedException("A kept-aside frame states image features this decoder does not read.");

    var dcQuant = JxlFrameQuantizer.ReadDcQuantization(reader);

    var isGray = imageMetadata.ColorEncoding.ColorSpace == 1;
    var baseChannels = isGray ? 1 : 3;
    var extraChannels = (int)imageMetadata.NumExtraChannels;
    var totalChannels = checked(baseChannels + extraChannels);
    var bits = (int)imageMetadata.BitDepth.BitsPerSample;

    var decoded = numGroups == 1
      ? JxlModularSpecDecoder.Decode(reader, frameWidth, frameHeight, totalChannels, bits, isTopLevelFrame: true)
      : JxlModularSpecDecoder.DecodeMultiGroup(
        codestream, reader, frameWidth, frameHeight, totalChannels, bits,
        groupDim, numGroupsX, numGroupsY, numDcGroups, (int)frame.NumPasses, toc, frameBody);
    if (!_ValidateDecodedImage(decoded, frameWidth, frameHeight, totalChannels))
      throw new InvalidDataException("A kept-aside frame did not decode.");

    var count = checked(frameWidth * frameHeight);
    var planeCount = 3 + extraChannels;
    var planes = new float[planeCount][];
    for (var p = 0; p < planeCount; ++p)
      planes[p] = new float[count];

    var channels = decoded.Channels;
    if (frame.ColorTransform == JxlColorTransform.Xyb) {
      // Stored as Y, X, B-Y; wanted as X, Y, B.
      for (var i = 0; i < count; ++i) {
        var y = channels[0].Pixels[i];
        planes[0][i] = channels[1].Pixels[i] * dcQuant[0];
        planes[1][i] = y * dcQuant[1];
        planes[2][i] = (channels[2].Pixels[i] + y) * dcQuant[2];
      }
    } else {
      var scale = 1.0f / ((1 << Math.Clamp(bits, 1, 30)) - 1);
      for (var p = 0; p < 3; ++p) {
        var source = channels[Math.Min(p, baseChannels - 1)].Pixels;
        for (var i = 0; i < count; ++i)
          planes[p][i] = source[i] * scale;
      }
    }

    var extraScale = 1.0f / ((1 << Math.Clamp(bits, 1, 30)) - 1);
    for (var p = 3; p < planeCount; ++p) {
      var source = channels[baseChannels + (p - 3)].Pixels;
      for (var i = 0; i < count; ++i)
        planes[p][i] = source[i] * extraScale;
    }

    var slot = (int)(frame.SaveAsReference % _ReferenceSlots);
    references[slot] = planes;
    referenceSizes[slot] = (frameWidth, frameHeight);

    var total = 0;
    foreach (var size in toc.SectionSizes)
      total = checked(total + size);
    var next = checked(frameBody + total);
    if (next <= 0 || next >= codestream.Length)
      throw new InvalidDataException("A kept-aside frame's sections run past the end of the file.");
    return next;
  }

  /// <summary>Which plane carries the alpha, or -1 when the picture has none.</summary>
  private static int _AlphaPlane(JxlImageMetadata imageMetadata) {
    for (var i = 0; i < imageMetadata.ExtraChannelInfo.Length; ++i)
      if (imageMetadata.ExtraChannelInfo[i].Type == 0)
        return 3 + i;
    return -1;
  }

  /// <summary>
  /// A decoded frame as float planes: three colour ones followed by its extra
  /// channels, all as fractions of full scale.
  /// </summary>
  private static float[][] _ToPlanes(
    object? decoded, int frameWidth, int frameHeight, int planeCount, int baseChannels, float scale
  ) {
    var modular = (JxlModularImage)decoded!;
    var count = checked(frameWidth * frameHeight);
    var planes = new float[planeCount][];

    for (var p = 0; p < planeCount; ++p) {
      planes[p] = new float[count];
      // A grey frame's one channel stands for all three colour planes.
      var source = p < 3
        ? Math.Min(p, baseChannels - 1)
        : baseChannels + (p - 3);
      if (source >= modular.Channels.Length)
        continue;

      // Splines are drawn in float and leave the samples underneath alone, so
      // where they ran the planes they produced are the picture.
      if (p < 3 && modular.ColorPlanes is { } drawn) {
        Array.Copy(drawn[p], planes[p], Math.Min(drawn[p].Length, count));
        continue;
      }

      var pixels = modular.Channels[source].Pixels;
      for (var i = 0; i < count && i < pixels.Length; ++i)
        planes[p][i] = pixels[i] * scale;
    }

    return planes;
  }

  /// <summary>
  /// Finish a modular frame by drawing its splines on top, in the fractions of
  /// full scale libjxl works in rather than in whole samples.
  /// </summary>
  private static void _DrawSplines(
    JxlModularImage modular, SplineList splines, int width, int height, int bits, bool isGray
  ) {
    // A spline's colour is stated against the luma channel the same way a
    // block's is, and it uses the frame's base correlation rather than any
    // per-tile one. A modular frame states no correlation map, so the base is
    // what it started as — and the B half of that is one, not zero, which is
    // the whole difference between a blue channel that agrees with libjxl and
    // one that is out by half its range.
    var segments = JxlSplines.BuildSegments(
      splines, width, height,
      yToX: JxlColorCorrelationMap.DefaultYtoXRatio,
      yToB: JxlColorCorrelationMap.DefaultYtoBRatio);
    if (segments.Count == 0)
      return;

    var count = checked(width * height);
    var scale = 1.0f / ((1 << Math.Clamp(bits, 1, 30)) - 1);
    var planes = new float[3][];
    for (var c = 0; c < 3; ++c) {
      planes[c] = new float[count];
      // A grey frame carries one channel and all three planes are drawn on it,
      // which is what makes a coloured spline show up on a grey picture.
      var source = modular.Channels[isGray ? 0 : Math.Min(c, modular.Channels.Length - 1)].Pixels;
      for (var i = 0; i < count; ++i)
        planes[c][i] = source[i] * scale;
    }

    JxlSplines.AddTo(segments, planes, width, height);
    modular.ColorPlanes = planes;
  }

  private static bool _ValidateDecodedImage(object? image, int width, int height, int channels) {
    if (image is not JxlModularImage modular || modular.Channels.Length < channels)
      return false;
    var count = checked(width * height);
    for (var c = 0; c < channels; ++c)
      if (modular.Channels[c].Width != width || modular.Channels[c].Height != height || modular.Channels[c].Pixels.Length < count)
        return false;
    return true;
  }

  private static bool _TryPackDecoded(
    JpegXlSpecMetadata metadata,
    JxlImageMetadata imageMetadata,
    object? decoded,
    string brand,
    out JpegXlFile file
  ) {
    file = default;
    // Floating-point samples have no home in either raster this hands back.
    if (metadata.BitsPerSample is < 1 or > 16 || metadata.IsFloatSample)
      return false;

    // Anything deeper than eight bits is carried at sixteen rather than
    // narrowed, because narrowing is a decision the caller should get to make.
    var deep = metadata.BitsPerSample > 8;
    var bytesPerSample = deep ? 2 : 1;

    var pixelCount = checked(metadata.Width * metadata.Height);
    if (decoded is JxlVarDctImage vardct) {
      if (vardct.Channels.Length < 3)
        return false;
      var rgb = deep
        ? JxlXybColorTransform.XybPlanesToRgb48(
          vardct.Channels[0], vardct.Channels[1], vardct.Channels[2], vardct.Width, vardct.Height)
        : JxlXybColorTransform.XybPlanesToRgb24(
          vardct.Channels[0], vardct.Channels[1], vardct.Channels[2], vardct.Width, vardct.Height);
      if (rgb.Length != checked(pixelCount * 3 * bytesPerSample))
        return false;
      file = new JpegXlFile {
        Width = metadata.Width,
        Height = metadata.Height,
        ComponentCount = 3,
        BitsPerSample = deep ? 16 : 8,
        PixelData = rgb,
        Brand = brand,
      };
      return true;
    }

    // A composed picture is already blended and already in float; all that is
    // left is to round it once and drop the extra channels that are not alpha.
    if (decoded is JxlComposedImage composed) {
      var keepsAlpha = composed.AlphaPlane >= 3;
      var parts = keepsAlpha ? 4 : 3;
      var maximum = deep ? 65535.0f : 255.0f;
      var blended = new byte[checked(pixelCount * parts * bytesPerSample)];
      for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < parts; ++c) {
        var plane = c < 3 ? c : composed.AlphaPlane;
        var value = Math.Clamp(composed.Planes[plane][i], 0.0f, 1.0f) * maximum + 0.5f;
        var at = (i * parts + c) * bytesPerSample;
        if (deep) {
          var sample = (ushort)Math.Clamp((int)value, 0, 65535);
          blended[at] = (byte)(sample >> 8);
          blended[at + 1] = (byte)sample;
        } else
          blended[at] = (byte)Math.Clamp((int)value, 0, 255);
      }

      file = new JpegXlFile {
        Width = metadata.Width,
        Height = metadata.Height,
        ComponentCount = parts,
        BitsPerSample = deep ? 16 : 8,
        PixelData = blended,
        Brand = brand,
      };
      return true;
    }

    if (decoded is not JxlModularImage modular)
      return false;
    var gray = imageMetadata.ColorEncoding.ColorSpace == 1;
    var baseChannels = gray ? 1 : 3;
    if (modular.Channels.Length < baseChannels)
      return false;

    // Once something has been drawn on top, the picture is the float planes and
    // the samples underneath are only what it was drawn over. A grey frame that
    // was drawn on comes back in colour, because a spline has a colour.
    if (modular.ColorPlanes is { } drawn) {
      var maximum = deep ? 65535.0f : 255.0f;
      var painted = new byte[checked(pixelCount * 3 * bytesPerSample)];
      for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 3; ++c) {
        var value = Math.Clamp(drawn[c][i], 0.0f, 1.0f) * maximum + 0.5f;
        var at = (i * 3 + c) * bytesPerSample;
        if (deep) {
          var sample = (ushort)Math.Clamp((int)value, 0, 65535);
          painted[at] = (byte)(sample >> 8);
          painted[at + 1] = (byte)sample;
        } else
          painted[at] = (byte)Math.Clamp((int)value, 0, 255);
      }

      file = new JpegXlFile {
        Width = metadata.Width,
        Height = metadata.Height,
        ComponentCount = 3,
        BitsPerSample = deep ? 16 : 8,
        PixelData = painted,
        Brand = brand,
      };
      return true;
    }

    var alphaIndex = -1;
    for (var i = 0; i < imageMetadata.ExtraChannelInfo.Length; ++i) {
      var extra = imageMetadata.ExtraChannelInfo[i];
      if (extra.Type == 0 && extra.DimShift == 0 && extra.BitDepth.BitsPerSample <= 8) {
        alphaIndex = baseChannels + i;
        break;
      }
    }
    var components = baseChannels + (alphaIndex >= 0 ? 1 : 0);
    // Two components at sixteen bits is Gray+Alpha, which has no deep format
    // here, so that one combination stays refused rather than narrowed.
    if (deep && components == 2)
      return false;

    var pixels = new byte[checked(pixelCount * components * bytesPerSample)];
    for (var i = 0; i < pixelCount; ++i) {
      var destination = i * components * bytesPerSample;
      for (var c = 0; c < baseChannels; ++c)
        _Store(pixels, destination + c * bytesPerSample, modular.Channels[c].Pixels[i], metadata.BitsPerSample, deep);
      if (alphaIndex >= 0) {
        if (alphaIndex >= modular.Channels.Length)
          return false;
        _Store(pixels, destination + baseChannels * bytesPerSample,
          modular.Channels[alphaIndex].Pixels[i], metadata.BitsPerSample, deep);
      }
    }

    file = new JpegXlFile {
      Width = metadata.Width,
      Height = metadata.Height,
      ComponentCount = components,
      BitsPerSample = deep ? 16 : 8,
      PixelData = pixels,
      Brand = brand,
    };
    return true;
  }

  private static void _Store(byte[] pixels, int at, int value, int bitsPerSample, bool deep) {
    if (!deep) {
      pixels[at] = _ToByte(value, bitsPerSample);
      return;
    }

    var sample = _ToUInt16(value, bitsPerSample);
    pixels[at] = (byte)(sample >> 8);
    pixels[at + 1] = (byte)sample;
  }

  /// <summary>A sample at the file's own depth, spread over the full 16-bit
  /// range so that the deepest value the file can state is the deepest
  /// value here.</summary>
  private static ushort _ToUInt16(int value, int bitsPerSample) {
    var max = (1 << Math.Clamp(bitsPerSample, 1, 16)) - 1;
    var clamped = Math.Clamp(value, 0, max);
    return (ushort)((clamped * 65535 + max / 2) / max);
  }

  private static byte _ToByte(int value, int bitsPerSample) {
    if (bitsPerSample <= 8)
      return (byte)Math.Clamp(value, 0, (1 << bitsPerSample) - 1);
    var max = (1 << Math.Min(bitsPerSample, 30)) - 1;
    return (byte)Math.Clamp((int)Math.Round(value * 255.0 / max), 0, 255);
  }

  private static JpegXlSpecMetadata _Metadata(int width, int height, JxlImageMetadata image, JxlSpecFrameHeader frame)
    => new(
      Width: width,
      Height: height,
      BitsPerSample: (int)image.BitDepth.BitsPerSample,
      IsFloatSample: image.BitDepth.FloatingPoint,
      NumExtraChannels: (int)image.NumExtraChannels,
      IsXybEncoded: image.XybEncoded,
      IsModularFrame: frame.Encoding == JxlFrameEncoding.Modular,
      IsProgressiveFrame: frame.NumPasses > 1);

  private static void _PopulatePartial(int width, int height, JxlImageMetadata? image, ref JpegXlSpecMetadata metadata) {
    metadata = new JpegXlSpecMetadata(
      Width: width,
      Height: height,
      BitsPerSample: image != null ? (int)image.BitDepth.BitsPerSample : 8,
      IsFloatSample: image?.BitDepth.FloatingPoint ?? false,
      NumExtraChannels: image != null ? (int)image.NumExtraChannels : 0,
      IsXybEncoded: image?.XybEncoded ?? false,
      IsModularFrame: false,
      IsProgressiveFrame: false);
  }

  private static bool _TryExtractCodestream(byte[] data, out byte[] codestream, out string brand) {
    codestream = [];
    brand = "jxl ";
    if (data.Length < 2)
      return false;
    if (data[0] == _CodestreamByte0 && data[1] == _CodestreamByte1) {
      codestream = data;
      return true;
    }

    var offset = 0;
    if (data.Length >= 12
        && data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 12
        && data[4] == (byte)'J' && data[5] == (byte)'X' && data[6] == (byte)'L' && data[7] == (byte)' '
        && data[8] == 0x0D && data[9] == 0x0A && data[10] == 0x87 && data[11] == 0x0A)
      offset = 12;

    if (offset + 12 > data.Length)
      return false;
    if (!_TryReadBox(data, offset, out var ftypSize, out var ftypHeader, out var ftypType) || ftypType != "ftyp")
      return false;
    if (ftypSize < ftypHeader + 4)
      return false;
    brand = System.Text.Encoding.ASCII.GetString(data, offset + ftypHeader, 4);
    if (brand != "jxl ")
      return false;
    offset = checked(offset + (int)ftypSize);

    byte[]? direct = null;
    var parts = new List<(uint Sequence, bool Last, byte[] Data)>();
    while (offset + 8 <= data.Length) {
      if (!_TryReadBox(data, offset, out var size, out var headerSize, out var type))
        return false;
      var payloadStart = checked(offset + headerSize);
      var payloadLength = checked((int)size - headerSize);

      if (type == "jxlc") {
        if (direct != null || parts.Count != 0)
          return false;
        direct = data.AsSpan(payloadStart, payloadLength).ToArray();
      } else if (type == "jxlp") {
        if (direct != null || payloadLength < 4)
          return false;
        var sequenceField = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payloadStart, 4));
        parts.Add((sequenceField & 0x7FFF_FFFFu, (sequenceField & 0x8000_0000u) != 0,
          data.AsSpan(payloadStart + 4, payloadLength - 4).ToArray()));
      }
      offset = checked(offset + (int)size);
    }

    if (direct != null) {
      codestream = direct;
      return codestream.Length >= 2 && codestream[0] == _CodestreamByte0 && codestream[1] == _CodestreamByte1;
    }
    if (parts.Count == 0)
      return false;

    parts.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
    for (var i = 0; i < parts.Count; ++i)
      if (parts[i].Sequence != (uint)i)
        return false;
    if (!parts[^1].Last)
      return false;

    using var assembled = new MemoryStream();
    foreach (var part in parts)
      assembled.Write(part.Data);
    codestream = assembled.ToArray();
    return codestream.Length >= 2 && codestream[0] == _CodestreamByte0 && codestream[1] == _CodestreamByte1;
  }

  private static bool _TryReadBox(byte[] data, int offset, out long size, out int headerSize, out string type) {
    size = 0;
    headerSize = 0;
    type = "";
    if (offset < 0 || offset + 8 > data.Length)
      return false;
    var size32 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    type = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);
    headerSize = 8;
    if (size32 == 1) {
      if (offset + 16 > data.Length)
        return false;
      var size64 = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 8, 8));
      if (size64 > long.MaxValue)
        return false;
      size = (long)size64;
      headerSize = 16;
    } else if (size32 == 0)
      size = data.Length - offset;
    else
      size = size32;
    return size >= headerSize && size <= data.Length - offset;
  }

  private static bool _TryParseLegacySynthetic(byte[] codestream, string brand, out JpegXlFile file) {
    file = default;
    try {
      if (codestream.Length < 4 || codestream[0] != _CodestreamByte0 || codestream[1] != _CodestreamByte1)
        return false;
      var (width, height, consumed) = JpegXlSizeHeader.Decode(codestream.AsSpan(2));
      var at = checked(2 + consumed);
      if (at >= codestream.Length)
        return false;
      var components = codestream[at++];
      if (components is not 1 and not 3)
        return false;
      var expected = checked(width * height * components);
      var remaining = codestream.Length - at;
      byte[] pixels;
      if (remaining == expected)
        pixels = codestream.AsSpan(at, expected).ToArray();
      else if (remaining > 1 && codestream[at] == 0x4D) {
        pixels = JxlFrameDecoder.DecodeFrame(codestream, at + 1, width, height, components, 8);
        if (pixels.Length != expected)
          return false;
      } else
        return false;

      file = new JpegXlFile {
        Width = width,
        Height = height,
        ComponentCount = components,
        PixelData = pixels,
        Brand = brand,
      };
      return true;
    } catch {
      return false;
    }
  }
}
