using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000;

/// <summary>Writes JPEG 2000 files: one tile, one quality layer, reversible 5/3, lossless.</summary>
/// <remarks>
/// The authoring profile is deliberately narrow — a single tile with the default precinct partition,
/// LRCP progression, 64 by 64 code-blocks and no coding-mode switches — but everything it does emit
/// is what T.800 specifies, packet headers included, so other decoders read it.
/// </remarks>
public static class Jpeg2000Writer {

  private const ushort _SOC = 0xFF4F;
  private const ushort _SIZ = 0xFF51;
  private const ushort _COD = 0xFF52;
  private const ushort _QCD = 0xFF5C;
  private const ushort _SOT = 0xFF90;
  private const ushort _SOD = 0xFF93;
  private const ushort _EOC = 0xFFD9;

  private const int _CODE_BLOCK_EXPONENT = 6;
  private const int _GUARD_BITS = 2;

  /// <summary>Wraps the codestream in a JP2 container.</summary>
  public static byte[] ToBytes(Jpeg2000File file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    return _BuildJp2Container(file, ToCodestreamBytes(file));
  }

  /// <summary>Builds the bare J2K codestream.</summary>
  public static byte[] ToCodestreamBytes(Jpeg2000File file) {
    _Validate(file);

    var levels = _ChooseDecompositionLevels(file);
    var image = _BuildImage(file, out var componentCount);
    var styles = new Jp2CodingStyle[componentCount];
    for (var c = 0; c < componentCount; ++c) {
      styles[c] = new() {
        DecompositionLevels = levels,
        CodeBlockWidthExp = _CODE_BLOCK_EXPONENT,
        CodeBlockHeightExp = _CODE_BLOCK_EXPONENT,
        CodeBlockStyle = 0,
        Transform = 1,
        QuantizationStyle = 0,
        GuardBits = _GUARD_BITS,
        QuantExponents = _BuildExponents(levels, file.BitsPerComponent),
        QuantMantissas = new int[3 * levels + 1],
      };
      styles[c].UseDefaultPrecincts();
    }

    var useMct = componentCount == 3;
    var tile = Jp2StructureBuilder.Build(image, 0, styles, 1, 0, useMct, false, false, allocateCoefficients: true);

    _LoadSamples(file, tile, componentCount);
    if (useMct)
      _ForwardComponentTransform(tile);

    foreach (var component in tile.Components)
      Jp2Wavelet.ForwardTransform(component);

    var guardBits = _EncodeCodeBlocks(tile);
    if (guardBits != _GUARD_BITS)
      foreach (var style in styles)
        style.GuardBits = guardBits;

    var tileData = Tier2Encoder.AssemblePackets(image, tile);

    using var output = new MemoryStream();
    _WriteMarker(output, _SOC);
    _WriteSiz(output, image, file.BitsPerComponent);
    _WriteCod(output, levels, useMct);
    _WriteQcd(output, styles[0]);
    _WriteSot(output, tileData.Length);
    _WriteMarker(output, _SOD);
    output.Write(tileData);
    _WriteMarker(output, _EOC);

    return output.ToArray();
  }

  private static void _Validate(Jpeg2000File file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), "JPEG 2000 dimensions must be positive.");
    if (file.ComponentCount is not 1 and not 3)
      throw new NotSupportedException("The JPEG 2000 writer authors one-component grey or three-component colour.");
    if (file.BitsPerComponent is < 1 or > 16)
      throw new NotSupportedException("The JPEG 2000 writer authors components of 1 to 16 bits.");

    var expected = checked(file.Width * file.Height * file.ComponentCount);
    if (file.PixelData == null || file.PixelData.Length != expected)
      throw new InvalidDataException($"JPEG 2000 pixel buffer has {file.PixelData?.Length ?? 0} bytes; expected {expected}.");
  }

  /// <summary>
  /// Caps the requested depth so no resolution level collapses; a decomposition finer than the
  /// picture is not something other encoders emit and not something every decoder accepts.
  /// </summary>
  private static int _ChooseDecompositionLevels(Jpeg2000File file) {
    var levels = Math.Clamp(file.DecompositionLevels, 0, 32);
    var smallest = Math.Min(file.Width, file.Height);
    while (levels > 0 && smallest >> levels == 0)
      --levels;

    return levels;
  }

  private static Jp2Image _BuildImage(Jpeg2000File file, out int componentCount) {
    componentCount = file.ComponentCount;
    var components = new Jp2Component[componentCount];
    for (var c = 0; c < componentCount; ++c)
      components[c] = new() { Precision = file.BitsPerComponent, Signed = false, Dx = 1, Dy = 1 };

    return new() {
      X0 = 0,
      Y0 = 0,
      X1 = file.Width,
      Y1 = file.Height,
      TileX0 = 0,
      TileY0 = 0,
      TileWidth = file.Width,
      TileHeight = file.Height,
      Components = components,
    };
  }

  /// <summary>E.1.1: the reversible exponent for a subband is the source precision plus its gain.</summary>
  private static int[] _BuildExponents(int levels, int precision) {
    var exponents = new int[3 * levels + 1];
    exponents[0] = precision;
    for (var resolution = 1; resolution <= levels; ++resolution) {
      exponents[3 * (resolution - 1) + 1] = precision + 1; // HL
      exponents[3 * (resolution - 1) + 2] = precision + 1; // LH
      exponents[3 * (resolution - 1) + 3] = precision + 2; // HH
    }

    return exponents;
  }

  private static void _LoadSamples(Jpeg2000File file, Jp2Tile tile, int componentCount) {
    var shift = 1 << (file.BitsPerComponent - 1);
    var count = file.Width * file.Height;

    for (var c = 0; c < componentCount; ++c) {
      var samples = tile.Components[c].Samples;
      for (var i = 0; i < count; ++i)
        samples[i] = file.PixelData[i * componentCount + c] - shift;
    }
  }

  /// <summary>The reversible colour transform, exact in integers and the partner of the 5/3 filter.</summary>
  private static void _ForwardComponentTransform(Jp2Tile tile) {
    var red = tile.Components[0].Samples;
    var green = tile.Components[1].Samples;
    var blue = tile.Components[2].Samples;

    for (var i = 0; i < red.Length; ++i) {
      var r = red[i];
      var g = green[i];
      var b = blue[i];
      red[i] = (r + 2 * g + b) >> 2;
      green[i] = b - g;
      blue[i] = r - g;
    }
  }

  /// <summary>
  /// Runs tier-1 over every code-block and reports the guard bits the result needs, which is two
  /// unless the transform produced more dynamic range than E.1's nominal range allows for.
  /// </summary>
  private static int _EncodeCodeBlocks(Jp2Tile tile) {
    var guardBits = _GUARD_BITS;

    foreach (var component in tile.Components)
      foreach (var resolution in component.Resolutions)
        foreach (var band in resolution.Bands) {
          if (band.Width <= 0 || band.Height <= 0)
            continue;

          foreach (var precinct in band.Precincts)
            foreach (var block in precinct.CodeBlocks) {
              if (block.Width <= 0 || block.Height <= 0)
                continue;

              var coefficients = new int[block.Width * block.Height];
              for (var y = 0; y < block.Height; ++y)
                Array.Copy(
                  band.Coefficients, (block.Y0 - band.Y0 + y) * band.Width + block.X0 - band.X0,
                  coefficients, y * block.Width, block.Width);

              block.Encoded = Tier1Coder.Encode(
                coefficients, block.Width, block.Height, band.Orientation, 0,
                out var passes, out var magnitudeBits);
              block.TotalPasses = passes;
              block.MagnitudeBits = magnitudeBits;

              var needed = _GUARD_BITS + Math.Max(0, magnitudeBits - band.MagnitudeBits);
              if (needed > guardBits)
                guardBits = needed;
            }
        }

    if (guardBits > 7)
      throw new InvalidDataException("JPEG 2000 wavelet coefficients need more guard bits than a quantization marker can carry.");

    if (guardBits != _GUARD_BITS)
      foreach (var component in tile.Components)
        foreach (var resolution in component.Resolutions)
          foreach (var band in resolution.Bands)
            band.MagnitudeBits += guardBits - _GUARD_BITS;

    foreach (var component in tile.Components)
      foreach (var resolution in component.Resolutions)
        foreach (var band in resolution.Bands)
          foreach (var precinct in band.Precincts)
            foreach (var block in precinct.CodeBlocks)
              block.ZeroBitPlanes = block.Encoded.Length > 0 ? band.MagnitudeBits - block.MagnitudeBits : band.MagnitudeBits;

    return guardBits;
  }

  private static byte[] _BuildJp2Container(Jpeg2000File file, byte[] codestream) {
    using var output = new MemoryStream();

    output.Write(Jp2Box.JP2_SIGNATURE_BYTES);
    Jp2Box.WriteBox(output, Jp2Box.TYPE_FILE_TYPE, _BuildFileTypeBox());
    Jp2Box.WriteBox(output, Jp2Box.TYPE_JP2_HEADER, _BuildJp2HeaderBox(file));
    Jp2Box.WriteBox(output, Jp2Box.TYPE_CODESTREAM, codestream);

    return output.ToArray();
  }

  private static byte[] _BuildFileTypeBox() {
    var data = new byte[12];
    "jp2 "u8.CopyTo(data);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 0);
    "jp2 "u8.CopyTo(data.AsSpan(8));
    return data;
  }

  private static byte[] _BuildJp2HeaderBox(Jpeg2000File file) {
    using var output = new MemoryStream();

    var header = new byte[14];
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), (uint)file.Height);
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)file.ComponentCount);
    header[10] = (byte)(file.BitsPerComponent - 1);
    header[11] = 7; // compression type: JPEG 2000
    header[12] = 0; // colourspace is known
    header[13] = 0; // no intellectual property box
    Jp2Box.WriteBox(output, Jp2Box.TYPE_IMAGE_HEADER, header);

    var colour = new byte[7];
    colour[0] = 1; // enumerated colourspace
    BinaryPrimitives.WriteUInt32BigEndian(colour.AsSpan(3), file.ComponentCount == 1 ? 17u : 16u);
    Jp2Box.WriteBox(output, Jp2Box.TYPE_COLOUR_SPEC, colour);

    return output.ToArray();
  }

  private static void _WriteMarker(Stream output, ushort marker) {
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, marker);
    output.Write(buffer);
  }

  private static void _WriteSiz(Stream output, Jp2Image image, int precision) {
    _WriteMarker(output, _SIZ);

    var count = image.Components.Length;
    var data = new byte[38 + 3 * count];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), (ushort)data.Length);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 0);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)image.X1);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)image.Y1);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), (uint)image.X0);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), (uint)image.Y0);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), (uint)image.TileWidth);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), (uint)image.TileHeight);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)image.TileX0);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(32), (uint)image.TileY0);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(36), (ushort)count);

    for (var c = 0; c < count; ++c) {
      data[38 + 3 * c] = (byte)(precision - 1);
      data[39 + 3 * c] = 1;
      data[40 + 3 * c] = 1;
    }

    output.Write(data);
  }

  private static void _WriteCod(Stream output, int levels, bool useMct) {
    _WriteMarker(output, _COD);

    var data = new byte[12];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), (ushort)data.Length);
    data[2] = 0; // Scod: default precincts, no SOP, no EPH
    data[3] = 0; // LRCP
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 1);
    data[6] = (byte)(useMct ? 1 : 0);
    data[7] = (byte)levels;
    data[8] = _CODE_BLOCK_EXPONENT - 2;
    data[9] = _CODE_BLOCK_EXPONENT - 2;
    data[10] = 0; // no code-block style switches
    data[11] = 1; // reversible 5/3
    output.Write(data);
  }

  private static void _WriteQcd(Stream output, Jp2CodingStyle style) {
    _WriteMarker(output, _QCD);

    var bands = style.QuantExponents.Length;
    var data = new byte[3 + bands];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), (ushort)data.Length);
    data[2] = (byte)(style.GuardBits << 5); // no quantization
    for (var b = 0; b < bands; ++b)
      data[3 + b] = (byte)(style.QuantExponents[b] << 3);

    output.Write(data);
  }

  private static void _WriteSot(Stream output, int tileDataLength) {
    _WriteMarker(output, _SOT);

    var data = new byte[10];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 10);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 0); // tile zero
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)(2 + 10 + 2 + tileDataLength));
    data[8] = 0; // first tile-part
    data[9] = 1; // and the only one
    output.Write(data);
  }
}
