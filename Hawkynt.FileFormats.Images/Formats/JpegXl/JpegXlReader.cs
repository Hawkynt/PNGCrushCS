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

    // A VarDCT frame is decoded for the diagnostic API but not handed back as a
    // picture. The pipeline runs end to end and lands within a couple of levels
    // of libjxl on the files it gets through, and a couple of levels is still a
    // different picture from the one that was encoded. Until it is exact this
    // reader says so rather than rounding the difference away.
    if (_TryDecodeSpec(codestream, out var metadata, out var imageMetadata, out var decoded)
        && metadata.IsModularFrame
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
      _AlignToByte(reader);
      var frame = JxlSpecFrameHeader.Decode(reader, imageMetadata);
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
      _AlignToByte(reader);
      var frame = JxlSpecFrameHeader.Decode(reader, imageMetadata);
      metadata = _Metadata(width, height, imageMetadata, frame);

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

        return _ValidateDecodedImage(image, width, height, totalChannels);
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
        numDcGroups: numDcGroups);
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
    if (metadata.BitsPerSample is < 1 or > 8 || metadata.IsFloatSample)
      return false;

    var pixelCount = checked(metadata.Width * metadata.Height);
    if (decoded is JxlVarDctImage vardct) {
      if (vardct.Channels.Length < 3)
        return false;
      var rgb = JxlXybColorTransform.XybPlanesToRgb24(
        vardct.Channels[0], vardct.Channels[1], vardct.Channels[2], vardct.Width, vardct.Height);
      if (rgb.Length != checked(pixelCount * 3))
        return false;
      file = new JpegXlFile {
        Width = metadata.Width,
        Height = metadata.Height,
        ComponentCount = 3,
        PixelData = rgb,
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

    var alphaIndex = -1;
    for (var i = 0; i < imageMetadata.ExtraChannelInfo.Length; ++i) {
      var extra = imageMetadata.ExtraChannelInfo[i];
      if (extra.Type == 0 && extra.DimShift == 0 && extra.BitDepth.BitsPerSample <= 8) {
        alphaIndex = baseChannels + i;
        break;
      }
    }
    var components = baseChannels + (alphaIndex >= 0 ? 1 : 0);
    var pixels = new byte[checked(pixelCount * components)];
    for (var i = 0; i < pixelCount; ++i) {
      var destination = i * components;
      for (var c = 0; c < baseChannels; ++c)
        pixels[destination + c] = _ToByte(modular.Channels[c].Pixels[i], metadata.BitsPerSample);
      if (alphaIndex >= 0) {
        if (alphaIndex >= modular.Channels.Length)
          return false;
        pixels[destination + baseChannels] = _ToByte(modular.Channels[alphaIndex].Pixels[i], metadata.BitsPerSample);
      }
    }

    file = new JpegXlFile {
      Width = metadata.Width,
      Height = metadata.Height,
      ComponentCount = components,
      PixelData = pixels,
      Brand = brand,
    };
    return true;
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

  private static void _AlignToByte(JxlBitReader reader) {
    var bits = (int)((8 - reader.BitsRead % 8) % 8);
    if (bits > 0)
      reader.ReadBits(bits);
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
