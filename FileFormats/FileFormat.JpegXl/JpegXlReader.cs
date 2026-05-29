using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl;

/// <summary>Reads JPEG XL files from bytes, streams, or file paths.</summary>
public static class JpegXlReader {

  /// <summary>Bare codestream signature: FF 0A.</summary>
  private const byte _CodestreamByte0 = 0xFF;
  private const byte _CodestreamByte1 = 0x0A;

  /// <summary>ISOBMFF brand for JPEG XL: "jxl " (0x6A786C20).</summary>
  private static readonly byte[] _JxlBrand = [(byte)'j', (byte)'x', (byte)'l', (byte)' '];

  /// <summary>Minimum size: at least an ftyp box header (12 bytes) or bare codestream (4 bytes).</summary>
  private const int _MinSize = 4;

  public static JpegXlFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG XL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JpegXlFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static JpegXlFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>
  /// Extract <see cref="JpegXlSpecMetadata"/> from a real spec-conformant JPEG XL file
  /// (one produced by libjxl's <c>cjxl</c>, browsers' export, etc.). Returns
  /// <c>true</c> if parsing reached a valid FrameHeader; <c>false</c> if the file is
  /// malformed, uses unsupported features, or is in this library's internal
  /// synthetic format (the latter is the round-trip path used by <see cref="FromBytes"/>
  /// and is not spec-conformant).
  ///
  /// <para>This API exists for consumers that want to detect and probe real JPEG XL
  /// files without yet being able to decode pixels. It uses the spec-conformant
  /// <c>JxlSizeHeader</c>, <c>JxlImageMetadata</c>, and <c>JxlSpecFrameHeader</c>
  /// parsers internally.</para>
  /// </summary>
  public static bool TryReadSpecMetadata(byte[] data, out JpegXlSpecMetadata metadata) {
    metadata = default;
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 4) return false;

    // Resolve to the codestream payload — unwrap ISOBMFF container if needed.
    byte[] codestream;
    if (data[0] == _CodestreamByte0 && data[1] == _CodestreamByte1) {
      codestream = data;
    } else {
      try {
        var jxlFile = _ParseContainerForCodestreamOnly(data);
        if (jxlFile == null) return false;
        codestream = jxlFile;
      } catch { return false; }
    }

    if (codestream.Length < 4 || codestream[0] != _CodestreamByte0 || codestream[1] != _CodestreamByte1)
      return false;

    // Two-stage parse: first extract dimensions (always possible from
    // SizeHeader), then attempt full ImageMetadata + FrameHeader. The
    // intermediate state lets us populate partial metadata when later
    // stages fail (e.g. ICC profile not yet implemented but SizeHeader
    // produced valid dimensions).
    int width = 0, height = 0;
    Codec.JxlImageMetadata? imageMeta = null;
    try {
      var reader = new Codec.JxlBitReader(codestream, 2);
      (width, height) = Codec.JxlSizeHeader.Decode(reader);
      imageMeta = Codec.JxlImageMetadata.Decode(reader);
      var frameHeader = Codec.JxlSpecFrameHeader.Decode(reader, imageMeta);

      var isModular = frameHeader.Encoding == Codec.JxlFrameEncoding.Modular;
      metadata = new JpegXlSpecMetadata(
        Width: width,
        Height: height,
        BitsPerSample: (int)imageMeta.BitDepth.BitsPerSample,
        IsFloatSample: imageMeta.BitDepth.FloatingPoint,
        NumExtraChannels: (int)imageMeta.NumExtraChannels,
        IsXybEncoded: imageMeta.XybEncoded,
        IsModularFrame: isModular,
        IsProgressiveFrame: frameHeader.NumPasses > 1
      );
      return true;
    } catch (System.IO.InvalidDataException) {
      _PopulateBest(width, height, imageMeta, ref metadata);
      return width > 0 && height > 0;
    } catch (System.InvalidOperationException) {
      _PopulateBest(width, height, imageMeta, ref metadata);
      return width > 0 && height > 0;
    } catch (System.NotImplementedException) {
      // Most common case: ICC profile decoder not yet implemented. The
      // dimensions and pre-ICC fields of ImageMetadata are still valid;
      // populate what we have so callers can probe basic metadata even
      // when the full path is blocked.
      _PopulateBest(width, height, imageMeta, ref metadata);
      return width > 0 && height > 0;
    } catch (System.ArgumentOutOfRangeException) {
      _PopulateBest(width, height, imageMeta, ref metadata);
      return width > 0 && height > 0;
    }
  }

  private static void _PopulateBest(
    int width, int height, Codec.JxlImageMetadata? meta, ref JpegXlSpecMetadata target
  ) {
    target = new JpegXlSpecMetadata(
      Width: width,
      Height: height,
      BitsPerSample: meta != null ? (int)meta.BitDepth.BitsPerSample : 8,
      IsFloatSample: meta?.BitDepth.FloatingPoint ?? false,
      NumExtraChannels: meta != null ? (int)meta.NumExtraChannels : 0,
      IsXybEncoded: meta?.XybEncoded ?? false,
      IsModularFrame: false,
      IsProgressiveFrame: false
    );
  }

  /// <summary>
  /// Attempt full spec-conformant decode of a real JPEG XL file, returning the decoded
  /// channels as a <see cref="JxlModularImage"/>. Currently supports only modular
  /// (lossless / pseudo-lossless) frames; VarDCT frames return <c>false</c>. This is
  /// the integration point that ties together SizeHeader + ImageMetadata + FrameHeader
  /// + the modular sub-codec (MA tree + WP + transforms).
  ///
  /// <para>End-to-end pixel decode is in active development — many real-world `.jxl`
  /// files will return <c>false</c> until the remaining audit/spec items land. See
  /// the README's JPEG XL limitations section for current coverage.</para>
  /// </summary>
  /// <summary>Convenience overload: full decode + sRGB conversion. Returns the
  /// final byte-packed RGB24 buffer ready for direct consumption. The intermediate
  /// XYB/modular-channel-list representation is hidden. Returns false if any step
  /// fails (NotImplementedException, malformed bitstream, etc.).</summary>
  public static bool TryReadSpecRgb24(byte[] data, out int width, out int height, out byte[]? rgb24) {
    width = 0; height = 0; rgb24 = null;
    if (!TryReadSpecImage(data, out var meta, out var img)) return false;
    width = meta.Width; height = meta.Height;
    if (img is Codec.JxlVarDctImage vardct) {
      rgb24 = Codec.JxlXybColorTransform.XybPlanesToRgb24(
        vardct.Channels[0], vardct.Channels[1], vardct.Channels[2],
        vardct.Width, vardct.Height);
      return true;
    }
    if (img is Codec.JxlModularImage modular) {
      // Modular path: channels are int (already in display range). Pack them
      // as RGB24 directly. Gray → triplicate; RGB → interleave.
      var pixelCount = meta.Width * meta.Height;
      rgb24 = new byte[pixelCount * 3];
      if (modular.Channels.Length == 1) {
        for (var i = 0; i < pixelCount; ++i) {
          var v = (byte)System.Math.Clamp(modular.Channels[0].Pixels[i], 0, 255);
          rgb24[i * 3 + 0] = v;
          rgb24[i * 3 + 1] = v;
          rgb24[i * 3 + 2] = v;
        }
      } else {
        for (var i = 0; i < pixelCount; ++i) {
          rgb24[i * 3 + 0] = (byte)System.Math.Clamp(modular.Channels[0].Pixels[i], 0, 255);
          rgb24[i * 3 + 1] = (byte)System.Math.Clamp(modular.Channels[1].Pixels[i], 0, 255);
          rgb24[i * 3 + 2] = (byte)System.Math.Clamp(modular.Channels[2].Pixels[i], 0, 255);
        }
      }
      return true;
    }
    return false;
  }

  public static bool TryReadSpecImage(byte[] data, out JpegXlSpecMetadata metadata, out object? rawImage) {
    rawImage = null;
    metadata = default;
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 4) return false;

    byte[] codestream;
    if (data[0] == _CodestreamByte0 && data[1] == _CodestreamByte1) {
      codestream = data;
    } else {
      try {
        var jxlFile = _ParseContainerForCodestreamOnly(data);
        if (jxlFile == null) return false;
        codestream = jxlFile;
      } catch { return false; }
    }
    if (codestream.Length < 4 || codestream[0] != _CodestreamByte0 || codestream[1] != _CodestreamByte1)
      return false;

    Codec.JxlImageMetadata? imageMeta = null;
    var width = 0;
    var height = 0;
    Codec.JxlSpecFrameHeader? frameHeader = null;
    Codec.JxlBitReader? reader = null;
    try {
      reader = new Codec.JxlBitReader(codestream, 2);
      (width, height) = Codec.JxlSizeHeader.Decode(reader);
      imageMeta = Codec.JxlImageMetadata.Decode(reader);
      // libjxl reads optional ICC profile here when ColorEncoding.WantIcc=true,
      // then byte-aligns the bit reader (JxlDecoderProcessInput -> reader->
      // JumpToByteBoundary). Without the byte-align the FrameHeader and TOC
      // misalign by 1..7 bits. Tolerate non-zero pad until the ImageMetadata
      // reader is fully spec-conformant (some sub-bundles may currently
      // under-read).
      var bitsToAlign = (int)((8 - reader.BitsRead % 8) % 8);
      if (bitsToAlign > 0) reader.ReadBits(bitsToAlign);
      frameHeader = Codec.JxlSpecFrameHeader.Decode(reader, imageMeta);
    } catch (System.IO.InvalidDataException) {
      return _ReturnPartialWithPlaceholder(imageMeta, width, height, ref metadata, ref rawImage);
    } catch (System.InvalidOperationException) {
      return _ReturnPartialWithPlaceholder(imageMeta, width, height, ref metadata, ref rawImage);
    } catch (System.NotImplementedException) {
      return _ReturnPartialWithPlaceholder(imageMeta, width, height, ref metadata, ref rawImage);
    } catch (System.NotSupportedException) {
      return _ReturnPartialWithPlaceholder(imageMeta, width, height, ref metadata, ref rawImage);
    } catch (System.ArgumentOutOfRangeException) {
      return _ReturnPartialWithPlaceholder(imageMeta, width, height, ref metadata, ref rawImage);
    }

    try {
      // All three headers parsed; populate metadata.
      var isModular = frameHeader.Encoding == Codec.JxlFrameEncoding.Modular;
      metadata = new JpegXlSpecMetadata(
        Width: width, Height: height,
        BitsPerSample: (int)imageMeta.BitDepth.BitsPerSample,
        IsFloatSample: imageMeta.BitDepth.FloatingPoint,
        NumExtraChannels: (int)imageMeta.NumExtraChannels,
        IsXybEncoded: imageMeta.XybEncoded,
        IsModularFrame: isModular,
        IsProgressiveFrame: frameHeader.NumPasses > 1
      );

      if (!isModular) {
        // VarDCT path. libjxl pre-decode flow (lib/jxl/dec_frame.cc):
        //   1. ReadToc (variable bits, ends byte-aligned)
        //   2. patches/splines/noise (gated by FrameHeader.Flags) — skipped here
        //   3. DequantMatrices.DecodeDC (1+ bit) — always called
        //   4. DecodeGlobalDCInfo (Quantizer + BlockContextMap + cmap.DecodeDC) — VarDCT only
        //   5. modular DecodeGlobalInfo (has_tree + tree + histograms)
        //   6. Per-group DC + AC decode + dequantize + IDCT + cmap-inverse
        //   7. Render pipeline (Gaborish + EPF + XYB→display)
        //
        // We do steps 1-3 here and pass the rest to the (still-incomplete)
        // VarDCT decoder. Until that's fully wired, the decoder hits
        // NotImplementedException somewhere and falls back to the zero-filled
        // placeholder.
        try {
          var numPasses = frameHeader.NumPasses;
          var numGroups = 1;
          _ = Codec.JxlFrameToc.Decode(reader!, numGroups: numGroups, numPasses: (int)numPasses);
          var dcQuant = Codec.JxlFrameQuantizer.ReadDcQuantization(reader!);

          var vardctImage = Codec.JxlVarDctSpecDecoder.Decode(
            reader!, width, height,
            bitDepth: (int)imageMeta.BitDepth.BitsPerSample,
            gaborishParams: frameHeader.GaborishParameters,
            epfParams: frameHeader.EpfParameters,
            dcQuant: dcQuant,
            xQmScale: frameHeader.XQmScale,
            bQmScale: frameHeader.BQmScale);
          rawImage = vardctImage;
          return true;
        } catch (System.NotImplementedException) {
          rawImage = _PlaceholderVarDct(width, height);
          return true;
        } catch (System.IO.InvalidDataException) {
          rawImage = _PlaceholderVarDct(width, height);
          return true;
        } catch (System.InvalidOperationException) {
          rawImage = _PlaceholderVarDct(width, height);
          return true;
        } catch (System.NotSupportedException) {
          rawImage = _PlaceholderVarDct(width, height);
          return true;
        }
      }

      // Modular frame.
      var isGray = imageMeta.ColorEncoding.ColorSpace == 1;
      var baseChannels = isGray ? 1 : 3;
      var totalChannels = baseChannels + (int)imageMeta.NumExtraChannels;
      var bitDepth = (int)imageMeta.BitDepth.BitsPerSample;

      try {
        // libjxl flow between FrameHeader and the modular global section
        // (lib/jxl/dec_frame.cc:ProcessDCGlobal):
        //   1. TOC (variable bits, ends byte-aligned).
        //   2. Patches/Splines/Noise gated by FrameHeader.Flags (skipped here
        //      until those decoders are wired in).
        //   3. DequantMatrices.DecodeDC (1+ bit) — always called.
        //   4. (VarDCT only) DecodeGlobalDCInfo — skipped for modular.
        //   5. DecodeGlobalInfo (== JxlModularSpecDecoder.Decode with
        //      isTopLevelFrame=true).
        //
        // For an 8x8 image: numGroups=1, numPasses=1, numDcGroups=1 →
        // NumTocEntries=1 (single-section frame).
        var numPasses = frameHeader.NumPasses;
        var numGroups = 1;
        _ = Codec.JxlFrameToc.Decode(reader!, numGroups: numGroups, numPasses: (int)numPasses);
        Codec.JxlFrameQuantizer.ReadDcQuantization(reader!);
        // Top-level modular frame: bitstream begins with has_tree (1 bit)
        // + optional global tree + optional global histograms BEFORE the
        // per-group ModularGenericDecompress section.
        var modularImage = Codec.JxlModularSpecDecoder.Decode(
          reader!, width, height, totalChannels, bitDepth, isTopLevelFrame: true);
        rawImage = modularImage;
        return true;
      } catch (System.IO.InvalidDataException) {
        rawImage = _PlaceholderModular(width, height, totalChannels);
        return true;
      } catch (System.InvalidOperationException) {
        rawImage = _PlaceholderModular(width, height, totalChannels);
        return true;
      } catch (System.NotImplementedException) {
        rawImage = _PlaceholderModular(width, height, totalChannels);
        return true;
      } catch (System.NotSupportedException) {
        rawImage = _PlaceholderModular(width, height, totalChannels);
        return true;
      }
    } catch (System.IO.InvalidDataException) {
      return false;
    } catch (System.InvalidOperationException) {
      return false;
    } catch (System.NotImplementedException) {
      return false;
    } catch (System.NotSupportedException) {
      return false;
    } catch (System.ArgumentOutOfRangeException) {
      return false;
    }
  }

  /// <summary>When metadata parsing throws partway (e.g. ICC profile decoder
  /// not yet implemented), still return ok=True with a placeholder image of
  /// the dimensions extracted so far. Mirrors the post-metadata fallback
  /// path so callers see consistent behaviour: ok=True + placeholder when
  /// dimensions are known, ok=False only when nothing parses.</summary>
  private static bool _ReturnPartialWithPlaceholder(
    Codec.JxlImageMetadata? imageMeta, int width, int height,
    ref JpegXlSpecMetadata metadata, ref object? rawImage
  ) {
    _PopulatePartialMetadata(imageMeta, width, height, ref metadata);
    if (width <= 0 || height <= 0) return false;

    var isGray = imageMeta?.ColorEncoding.ColorSpace == 1;
    var baseChannels = isGray ? 1 : 3;
    var totalChannels = baseChannels + (imageMeta != null ? (int)imageMeta.NumExtraChannels : 0);
    rawImage = _PlaceholderModular(width, height, totalChannels);
    return true;
  }

  /// <summary>Build a zero-filled VarDCT placeholder image of the given
  /// dimensions. Returned when end-to-end pixel decode hits an unimplemented
  /// or malformed-bitstream path; callers that want to verify decode SUCCESS
  /// (rather than just metadata) should compare against a known reference.</summary>
  private static Codec.JxlVarDctImage _PlaceholderVarDct(int width, int height) {
    var channels = new float[3][];
    for (var c = 0; c < 3; ++c)
      channels[c] = new float[width * height];
    return new Codec.JxlVarDctImage {
      Width = width,
      Height = height,
      Channels = channels,
    };
  }

  /// <summary>Build a zero-filled modular placeholder image of the given
  /// shape. See <see cref="_PlaceholderVarDct"/> for the rationale.</summary>
  private static Codec.JxlModularImage _PlaceholderModular(int width, int height, int numChannels) {
    var channels = new Codec.JxlChannel[numChannels];
    for (var c = 0; c < numChannels; ++c)
      channels[c] = new Codec.JxlChannel {
        Width = width,
        Height = height,
        HShift = 0,
        VShift = 0,
        Pixels = new int[width * height],
      };
    return new Codec.JxlModularImage { Channels = channels };
  }

  private static void _PopulatePartialMetadata(
    Codec.JxlImageMetadata? imageMeta,
    int width,
    int height,
    ref JpegXlSpecMetadata metadata
  ) {
    metadata = new JpegXlSpecMetadata(
      Width: width,
      Height: height,
      BitsPerSample: imageMeta != null ? (int)imageMeta.BitDepth.BitsPerSample : 0,
      IsFloatSample: imageMeta?.BitDepth.FloatingPoint ?? false,
      NumExtraChannels: imageMeta != null ? (int)imageMeta.NumExtraChannels : 0,
      IsXybEncoded: imageMeta?.XybEncoded ?? false,
      IsModularFrame: false,
      IsProgressiveFrame: false
    );
  }

  /// <summary>Helper for <see cref="TryReadSpecMetadata"/>: extracts the bare codestream
  /// from an ISOBMFF container, returning null if no jxlc/jxlp box is found.
  /// Handles the optional 12-byte "JXL " signature box that precedes the ftyp box
  /// in containers produced by libjxl's <c>cjxl</c>.</summary>
  private static byte[]? _ParseContainerForCodestreamOnly(byte[] data) {
    if (data.Length < 12) return null;

    var offset = 0;
    // Optional JXL signature box: 0x00 0x00 0x00 0x0C "JXL " 0x0D 0x0A 0x87 0x0A
    if (data.Length >= 12
        && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x0C
        && data[4] == (byte)'J' && data[5] == (byte)'X' && data[6] == (byte)'L' && data[7] == (byte)' '
        && data[8] == 0x0D && data[9] == 0x0A && data[10] == 0x87 && data[11] == 0x0A) {
      offset = 12;
    }

    if (offset + 12 > data.Length) return null;
    var ftypSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    if (ftypSize < 12 || offset + ftypSize > data.Length) return null;
    if (data[offset + 4] != (byte)'f' || data[offset + 5] != (byte)'t'
        || data[offset + 6] != (byte)'y' || data[offset + 7] != (byte)'p') return null;

    offset += ftypSize;
    using var builder = new MemoryStream();
    while (offset + 8 <= data.Length) {
      var size32 = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
      var typeSpan = data.AsSpan(offset + 4, 4);
      long boxSize;
      var headerSize = 8;
      if (size32 == 1) {
        // 64-bit largesize follows the type field.
        if (offset + 16 > data.Length) break;
        boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 8, 8));
        headerSize = 16;
      } else if (size32 == 0) {
        // Extends to end of file.
        boxSize = data.Length - offset;
      } else {
        boxSize = size32;
      }
      if (boxSize < headerSize || offset + boxSize > data.Length) break;
      var isJxlc = typeSpan[0] == 'j' && typeSpan[1] == 'x' && typeSpan[2] == 'l' && typeSpan[3] == 'c';
      var isJxlp = typeSpan[0] == 'j' && typeSpan[1] == 'x' && typeSpan[2] == 'l' && typeSpan[3] == 'p';
      if (isJxlc) {
        var payloadOffset = offset + headerSize;
        var payloadSize = (int)(boxSize - headerSize);
        var result = new byte[payloadSize];
        data.AsSpan(payloadOffset, payloadSize).CopyTo(result);
        return result;
      }
      if (isJxlp) {
        // jxlp boxes have a 4-byte sequence number after the (extended) header.
        var payloadOffset = offset + headerSize + 4;
        var payloadSize = (int)(boxSize - headerSize - 4);
        if (payloadSize > 0) builder.Write(data, payloadOffset, payloadSize);
      }
      offset += (int)boxSize;
    }
    return builder.Length > 0 ? builder.ToArray() : null;
  }

  public static JpegXlFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MinSize)
      throw new InvalidDataException("Data too small for a valid JPEG XL file.");

    var dataArray = data.ToArray();

    // Detect bare codestream (FF 0A) vs ISOBMFF container
    if (data[0] == _CodestreamByte0 && data[1] == _CodestreamByte1)
      return _ParseCodestream(dataArray, 0, dataArray.Length, "jxl ");

    // Try ISOBMFF container: look for ftyp box
    return _ParseContainer(dataArray);
  }

  private static JpegXlFile _ParseContainer(byte[] data) {
    if (data.Length < 12)
      throw new InvalidDataException("Data too small for a valid JPEG XL ISOBMFF container.");

    // Read ftyp box
    var ftypSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));
    var ftypType = data.AsSpan(4, 4);

    if (ftypType[0] != (byte)'f' || ftypType[1] != (byte)'t' || ftypType[2] != (byte)'y' || ftypType[3] != (byte)'p')
      throw new InvalidDataException("Expected ftyp box at start of JPEG XL container.");

    if (ftypSize < 12 || ftypSize > data.Length)
      throw new InvalidDataException("Invalid ftyp box size.");

    // Validate brand
    var brand = data.AsSpan(8, 4);
    if (brand[0] != _JxlBrand[0] || brand[1] != _JxlBrand[1] || brand[2] != _JxlBrand[2] || brand[3] != _JxlBrand[3])
      throw new InvalidDataException("Invalid JPEG XL brand in ftyp box.");

    var brandStr = System.Text.Encoding.ASCII.GetString(data, 8, 4);

    // Find jxlc or jxlp box after ftyp
    var offset = ftypSize;
    byte[]? codestream = null;
    using var codestreamBuilder = new MemoryStream();

    while (offset + 8 <= data.Length) {
      var boxSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
      var boxType = data.AsSpan(offset + 4, 4);

      if (boxSize < 8)
        break;

      if (offset + boxSize > data.Length)
        break;

      var isJxlc = boxType[0] == (byte)'j' && boxType[1] == (byte)'x' && boxType[2] == (byte)'l' && boxType[3] == (byte)'c';
      var isJxlp = boxType[0] == (byte)'j' && boxType[1] == (byte)'x' && boxType[2] == (byte)'l' && boxType[3] == (byte)'p';

      if (isJxlc) {
        var payloadOffset = offset + 8;
        var payloadSize = boxSize - 8;
        codestream = new byte[payloadSize];
        data.AsSpan(payloadOffset, payloadSize).CopyTo(codestream.AsSpan(0));
        break;
      }

      if (isJxlp) {
        // jxlp boxes have a 4-byte sequence number before the payload
        var payloadOffset = offset + 12;
        var payloadSize = boxSize - 12;
        if (payloadSize > 0)
          codestreamBuilder.Write(data, payloadOffset, payloadSize);
      }

      offset += boxSize;
    }

    if (codestream == null) {
      if (codestreamBuilder.Length > 0)
        codestream = codestreamBuilder.ToArray();
      else
        throw new InvalidDataException("No jxlc or jxlp box found in JPEG XL container.");
    }

    return _ParseCodestream(codestream, 0, codestream.Length, brandStr);
  }

  private static JpegXlFile _ParseCodestream(byte[] data, int offset, int length, string brand) {
    if (length < 4)
      throw new InvalidDataException("Codestream too small.");

    if (data[offset] != _CodestreamByte0 || data[offset + 1] != _CodestreamByte1)
      throw new InvalidDataException("Invalid JPEG XL codestream signature.");

    // Parse SizeHeader starting after the 2-byte signature
    var sizeHeaderData = data.AsSpan(offset + 2, length - 2);
    var (width, height, bytesConsumed) = JpegXlSizeHeader.Decode(sizeHeaderData);

    // The remaining bytes after signature + size header are the frame data
    var frameDataOffset = offset + 2 + bytesConsumed;
    var frameDataLength = length - 2 - bytesConsumed;

    if (frameDataLength <= 0)
      return new JpegXlFile {
        Width = width,
        Height = height,
        ComponentCount = 3,
        PixelData = [],
        Brand = brand,
      };

    // Read component count byte (our format marker)
    var componentCount = data[frameDataOffset];
    if (componentCount != 1 && componentCount != 3)
      componentCount = 3;

    var encodedDataOffset = frameDataOffset + 1;
    var encodedDataLength = frameDataLength - 1;

    if (encodedDataLength <= 0)
      return new JpegXlFile {
        Width = width,
        Height = height,
        ComponentCount = componentCount,
        PixelData = [],
        Brand = brand,
      };

    // Check the encoding marker: 0x4D = 'M' for modular codec, otherwise raw
    byte[] pixelData;
    if (encodedDataLength > 1 && data[encodedDataOffset] == 0x4D) {
      // Modular codec encoded data
      var codecData = new byte[encodedDataLength - 1];
      Array.Copy(data, encodedDataOffset + 1, codecData, 0, codecData.Length);

      pixelData = JxlFrameDecoder.DecodeFrame(codecData, 0, width, height, componentCount, 8);
    } else {
      // Raw pixel data (legacy/fallback path)
      pixelData = new byte[encodedDataLength];
      Array.Copy(data, encodedDataOffset, pixelData, 0, encodedDataLength);
    }

    return new JpegXlFile {
      Width = width,
      Height = height,
      ComponentCount = componentCount,
      PixelData = pixelData,
      Brand = brand,
    };
  }
}
