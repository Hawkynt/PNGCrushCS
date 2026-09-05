using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>One decoded component plane at the precision the codestream declared.</summary>
internal sealed class Jp2DecodedImage {
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required Jp2Component[] Components { get; init; }

  /// <summary>One plane per component, in raster order over that component's own sample grid.</summary>
  public required int[][] Planes { get; init; }

  public required int[] PlaneWidths { get; init; }
  public required int[] PlaneHeights { get; init; }
  public required int DecompositionLevels { get; init; }
}

/// <summary>Parses a JPEG 2000 codestream and decodes it to component planes (ITU-T T.800 Annex A).</summary>
internal static class Jp2CodestreamDecoder {

  private const ushort _SOC = 0xFF4F;
  private const ushort _SIZ = 0xFF51;
  private const ushort _COD = 0xFF52;
  private const ushort _COC = 0xFF53;
  private const ushort _QCD = 0xFF5C;
  private const ushort _QCC = 0xFF5D;
  private const ushort _RGN = 0xFF5E;
  private const ushort _POC = 0xFF5F;
  private const ushort _PPM = 0xFF60;
  private const ushort _PPT = 0xFF61;
  private const ushort _SOT = 0xFF90;
  private const ushort _SOD = 0xFF93;
  private const ushort _EOC = 0xFFD9;

  private sealed class TileParts {
    internal MemoryStream Data { get; } = new();
    internal Jp2CodingStyle[]? Styles { get; set; }
    internal int Layers { get; set; } = -1;
    internal int ProgressionOrder { get; set; } = -1;
    internal int Mct { get; set; } = -1;
    internal bool UseSop { get; set; }
    internal bool UseEph { get; set; }
    internal bool Configured { get; set; }
  }

  public static Jp2DecodedImage Decode(byte[] data, int offset, int length) {
    ArgumentNullException.ThrowIfNull(data);
    var end = offset + length;
    var position = offset;

    if (position + 2 > end || BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position)) != _SOC)
      throw new InvalidDataException("JPEG 2000 codestream does not start with the SOC marker.");

    position += 2;

    Jp2Image? image = null;
    Jp2CodingStyle[]? mainStyles = null;
    var mainLayers = 1;
    var mainProgression = 0;
    var mainMct = 0;
    var mainSop = false;
    var mainEph = false;
    var tiles = new Dictionary<int, TileParts>();

    while (position + 2 <= end) {
      var marker = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position));
      position += 2;

      if (marker == _EOC)
        break;

      if (marker == _SOD)
        throw new InvalidDataException("JPEG 2000 codestream has an SOD marker outside a tile-part header.");

      if ((marker & 0xFF00) != 0xFF00)
        throw new InvalidDataException($"JPEG 2000 codestream lost marker alignment at offset {position - 2}.");

      if (position + 2 > end)
        throw new InvalidDataException("JPEG 2000 marker segment has no length.");

      var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position));
      if (segmentLength < 2 || position + segmentLength > end)
        throw new InvalidDataException($"JPEG 2000 marker 0x{marker:X4} declares an impossible length.");

      var payload = data.AsSpan(position + 2, segmentLength - 2);

      switch (marker) {
        case _SIZ:
          image = _ParseSiz(payload);
          mainStyles = new Jp2CodingStyle[image.Components.Length];
          for (var c = 0; c < mainStyles.Length; ++c) {
            mainStyles[c] = new();
            mainStyles[c].UseDefaultPrecincts();
          }
          break;

        case _COD:
          _RequireImage(image);
          _ParseCod(payload, mainStyles!, out mainProgression, out mainLayers, out mainMct, out mainSop, out mainEph);
          break;

        case _COC:
          _RequireImage(image);
          _ParseCoc(payload, mainStyles!, image!.Components.Length);
          break;

        case _QCD:
          _RequireImage(image);
          _ParseQuantization(payload, mainStyles!, -1, image!.Components.Length);
          break;

        case _QCC:
          _RequireImage(image);
          _ParseQcc(payload, mainStyles!, image!.Components.Length);
          break;

        case _RGN:
          throw new NotSupportedException("JPEG 2000 region-of-interest coding is not implemented.");

        case _POC:
          throw new NotSupportedException("JPEG 2000 progression-order changes are not implemented.");

        case _PPM:
        case _PPT:
          throw new NotSupportedException("JPEG 2000 packed packet headers are not implemented.");

        case _SOT:
          position = _ReadTilePart(
            data, position, end, segmentLength, payload, image, mainStyles, tiles,
            mainLayers, mainProgression, mainMct, mainSop, mainEph);
          continue;
      }

      position += segmentLength;
    }

    if (image == null || mainStyles == null)
      throw new InvalidDataException("JPEG 2000 codestream has no SIZ marker.");

    return _DecodeTiles(image, mainStyles, tiles, mainLayers, mainProgression, mainMct, mainSop, mainEph);
  }

  private static void _RequireImage(Jp2Image? image) {
    if (image == null)
      throw new InvalidDataException("JPEG 2000 codestream has a coding-style marker before SIZ.");
  }

  private static int _ReadTilePart(
    byte[] data,
    int position,
    int end,
    int segmentLength,
    ReadOnlySpan<byte> payload,
    Jp2Image? image,
    Jp2CodingStyle[]? mainStyles,
    Dictionary<int, TileParts> tiles,
    int mainLayers,
    int mainProgression,
    int mainMct,
    bool mainSop,
    bool mainEph
  ) {
    _RequireImage(image);
    if (payload.Length < 8)
      throw new InvalidDataException("JPEG 2000 SOT segment is too short.");

    var tileIndex = BinaryPrimitives.ReadUInt16BigEndian(payload);
    var tilePartLength = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[2..]);
    var tilePartStart = position - 2;

    if (!tiles.TryGetValue(tileIndex, out var tile)) {
      tile = new();
      tiles[tileIndex] = tile;
    }

    var cursor = position + segmentLength;

    // Only the first tile-part carries a tile header; later ones resume the same tile's packets.
    while (cursor + 2 <= end) {
      var marker = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor));
      cursor += 2;

      if (marker == _SOD)
        break;

      if (cursor + 2 > end)
        throw new InvalidDataException("JPEG 2000 tile-part header marker has no length.");

      var innerLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor));
      if (innerLength < 2 || cursor + innerLength > end)
        throw new InvalidDataException($"JPEG 2000 tile-part marker 0x{marker:X4} declares an impossible length.");

      var innerPayload = data.AsSpan(cursor + 2, innerLength - 2);
      tile.Styles ??= _CloneStyles(mainStyles!);

      switch (marker) {
        case _COD:
          _ParseCod(
            innerPayload, tile.Styles, out var progression, out var layers, out var mct,
            out var sop, out var eph);
          tile.ProgressionOrder = progression;
          tile.Layers = layers;
          tile.Mct = mct;
          tile.UseSop = sop;
          tile.UseEph = eph;
          tile.Configured = true;
          break;

        case _COC:
          _ParseCoc(innerPayload, tile.Styles, image!.Components.Length);
          break;

        case _QCD:
          _ParseQuantization(innerPayload, tile.Styles, -1, image!.Components.Length);
          break;

        case _QCC:
          _ParseQcc(innerPayload, tile.Styles, image!.Components.Length);
          break;

        case _RGN:
          throw new NotSupportedException("JPEG 2000 region-of-interest coding is not implemented.");

        case _PPT:
          throw new NotSupportedException("JPEG 2000 packed packet headers are not implemented.");
      }

      cursor += innerLength;
    }

    if (!tile.Configured) {
      tile.Styles ??= _CloneStyles(mainStyles!);
      if (tile.Layers < 0) {
        tile.Layers = mainLayers;
        tile.ProgressionOrder = mainProgression;
        tile.Mct = mainMct;
        tile.UseSop = mainSop;
        tile.UseEph = mainEph;
      }
    }

    // Psot counts from the SOT marker itself; a zero means this tile-part runs to the end.
    var bodyEnd = tilePartLength > 0 ? Math.Min(tilePartStart + tilePartLength, end) : end;
    if (bodyEnd > end || bodyEnd < cursor)
      throw new InvalidDataException("JPEG 2000 tile-part length runs outside the codestream.");

    if (tilePartLength == 0) {
      // Trim the terminating EOC, which is not packet data.
      while (bodyEnd - 2 >= cursor && BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(bodyEnd - 2)) == _EOC)
        bodyEnd -= 2;
    }

    tile.Data.Write(data, cursor, bodyEnd - cursor);
    return bodyEnd;
  }

  private static Jp2CodingStyle[] _CloneStyles(Jp2CodingStyle[] styles) {
    var result = new Jp2CodingStyle[styles.Length];
    for (var i = 0; i < styles.Length; ++i)
      result[i] = styles[i].Clone();

    return result;
  }

  private static Jp2Image _ParseSiz(ReadOnlySpan<byte> payload) {
    if (payload.Length < 36)
      throw new InvalidDataException("JPEG 2000 SIZ segment is too short.");

    var x1 = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[2..]);
    var y1 = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[6..]);
    var x0 = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[10..]);
    var y0 = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[14..]);
    var tileWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[18..]);
    var tileHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[22..]);
    var tileX0 = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[26..]);
    var tileY0 = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[30..]);
    var count = BinaryPrimitives.ReadUInt16BigEndian(payload[34..]);

    if (count <= 0 || payload.Length < 36 + 3 * count)
      throw new InvalidDataException("JPEG 2000 SIZ segment does not describe all its components.");
    if (x1 <= x0 || y1 <= y0 || tileWidth <= 0 || tileHeight <= 0)
      throw new InvalidDataException("JPEG 2000 SIZ segment has a degenerate image or tile grid.");

    var components = new Jp2Component[count];
    for (var c = 0; c < count; ++c) {
      var ssiz = payload[36 + 3 * c];
      components[c] = new() {
        Precision = (ssiz & 0x7F) + 1,
        Signed = (ssiz & 0x80) != 0,
        Dx = payload[37 + 3 * c],
        Dy = payload[38 + 3 * c],
      };

      if (components[c].Dx <= 0 || components[c].Dy <= 0)
        throw new InvalidDataException("JPEG 2000 SIZ segment has a zero component sub-sampling factor.");
    }

    return new() {
      X0 = x0,
      Y0 = y0,
      X1 = x1,
      Y1 = y1,
      TileX0 = tileX0,
      TileY0 = tileY0,
      TileWidth = tileWidth,
      TileHeight = tileHeight,
      Components = components,
    };
  }

  private static void _ParseCod(
    ReadOnlySpan<byte> payload,
    Jp2CodingStyle[] styles,
    out int progressionOrder,
    out int layers,
    out int mct,
    out bool useSop,
    out bool useEph
  ) {
    if (payload.Length < 10)
      throw new InvalidDataException("JPEG 2000 COD segment is too short.");

    var scod = payload[0];
    progressionOrder = payload[1];
    layers = BinaryPrimitives.ReadUInt16BigEndian(payload[2..]);
    mct = payload[4];
    useSop = (scod & 2) != 0;
    useEph = (scod & 4) != 0;

    if (layers <= 0)
      throw new InvalidDataException("JPEG 2000 COD segment declares no quality layers.");

    foreach (var style in styles)
      _ParseCodingStyleParameters(payload[5..], style, (scod & 1) != 0);
  }

  private static void _ParseCoc(ReadOnlySpan<byte> payload, Jp2CodingStyle[] styles, int componentCount) {
    var indexBytes = componentCount < 257 ? 1 : 2;
    if (payload.Length < indexBytes + 6)
      throw new InvalidDataException("JPEG 2000 COC segment is too short.");

    var component = indexBytes == 1 ? payload[0] : BinaryPrimitives.ReadUInt16BigEndian(payload);
    if (component >= componentCount)
      throw new InvalidDataException("JPEG 2000 COC segment names a component the image does not have.");

    var scoc = payload[indexBytes];
    _ParseCodingStyleParameters(payload[(indexBytes + 1)..], styles[component], (scoc & 1) != 0);
  }

  private static void _ParseCodingStyleParameters(ReadOnlySpan<byte> parameters, Jp2CodingStyle style, bool explicitPrecincts) {
    if (parameters.Length < 5)
      throw new InvalidDataException("JPEG 2000 coding-style parameters are too short.");

    style.DecompositionLevels = parameters[0];
    style.CodeBlockWidthExp = (parameters[1] & 0x0F) + 2;
    style.CodeBlockHeightExp = (parameters[2] & 0x0F) + 2;
    style.CodeBlockStyle = parameters[3];
    style.Transform = parameters[4];

    // T.800 allows 32, but the thirty-second level's coordinate arithmetic no longer fits the Int32
    // this decoder indexes with, and no image is large enough to reach it.
    if (style.DecompositionLevels > 31)
      throw new InvalidDataException($"JPEG 2000 coding style declares {style.DecompositionLevels} decomposition levels; this decoder handles up to 31.");
    if (style.CodeBlockWidthExp + style.CodeBlockHeightExp > 12)
      throw new InvalidDataException("JPEG 2000 code-blocks may hold at most 4096 coefficients.");

    var unsupported = style.CodeBlockStyle & ~(Tier1Coder.STYLE_VERTICALLY_CAUSAL | Tier1Coder.STYLE_SEGMENTATION_SYMBOLS);
    if (unsupported != 0)
      throw new NotSupportedException($"JPEG 2000 code-block style 0x{style.CodeBlockStyle:X2} uses coding modes this decoder does not implement.");

    style.UseDefaultPrecincts();
    if (!explicitPrecincts)
      return;

    var count = style.DecompositionLevels + 1;
    if (parameters.Length < 5 + count)
      throw new InvalidDataException("JPEG 2000 coding style promises precinct sizes it does not carry.");

    for (var r = 0; r < count; ++r) {
      style.PrecinctWidthExp[r] = parameters[5 + r] & 0x0F;
      style.PrecinctHeightExp[r] = (parameters[5 + r] >> 4) & 0x0F;
      if (r > 0 && (style.PrecinctWidthExp[r] == 0 || style.PrecinctHeightExp[r] == 0))
        throw new InvalidDataException("JPEG 2000 precinct sizes below the top resolution must exceed one sample.");
    }
  }

  private static void _ParseQcc(ReadOnlySpan<byte> payload, Jp2CodingStyle[] styles, int componentCount) {
    var indexBytes = componentCount < 257 ? 1 : 2;
    if (payload.Length < indexBytes + 1)
      throw new InvalidDataException("JPEG 2000 QCC segment is too short.");

    var component = indexBytes == 1 ? payload[0] : BinaryPrimitives.ReadUInt16BigEndian(payload);
    if (component >= componentCount)
      throw new InvalidDataException("JPEG 2000 QCC segment names a component the image does not have.");

    _ParseQuantization(payload[indexBytes..], styles, component, componentCount);
  }

  private static void _ParseQuantization(
    ReadOnlySpan<byte> payload,
    Jp2CodingStyle[] styles,
    int onlyComponent,
    int componentCount
  ) {
    _ = componentCount;
    if (payload.Length < 1)
      throw new InvalidDataException("JPEG 2000 quantization segment is too short.");

    var sqcd = payload[0];
    var quantStyle = sqcd & 0x1F;
    var guardBits = (sqcd >> 5) & 0x07;
    var values = payload[1..];

    int[] exponents;
    int[] mantissas;

    switch (quantStyle) {
      case 0: {
        exponents = new int[values.Length];
        mantissas = new int[values.Length];
        for (var i = 0; i < values.Length; ++i)
          exponents[i] = values[i] >> 3;
        break;
      }
      case 1: {
        if (values.Length < 2)
          throw new InvalidDataException("JPEG 2000 derived quantization needs one step size.");

        var word = BinaryPrimitives.ReadUInt16BigEndian(values);
        exponents = [word >> 11];
        mantissas = [word & 0x7FF];
        break;
      }
      case 2: {
        var count = values.Length / 2;
        exponents = new int[count];
        mantissas = new int[count];
        for (var i = 0; i < count; ++i) {
          var word = BinaryPrimitives.ReadUInt16BigEndian(values[(2 * i)..]);
          exponents[i] = word >> 11;
          mantissas[i] = word & 0x7FF;
        }
        break;
      }
      default:
        throw new InvalidDataException($"JPEG 2000 quantization style {quantStyle} is not one of the three T.800 defines.");
    }

    for (var c = 0; c < styles.Length; ++c) {
      if (onlyComponent >= 0 && c != onlyComponent)
        continue;

      var style = styles[c];
      style.QuantizationStyle = quantStyle;
      style.GuardBits = guardBits;

      if (quantStyle != 1) {
        style.QuantExponents = (int[])exponents.Clone();
        style.QuantMantissas = (int[])mantissas.Clone();
        continue;
      }

      // E.1.1: a derived step size names the lowest band and the others follow from the level.
      var bands = 3 * style.DecompositionLevels + 1;
      var derivedExponents = new int[bands];
      var derivedMantissas = new int[bands];
      derivedExponents[0] = exponents[0];
      derivedMantissas[0] = mantissas[0];
      for (var b = 1; b < bands; ++b) {
        derivedExponents[b] = Math.Max(0, exponents[0] - (b - 1) / 3);
        derivedMantissas[b] = mantissas[0];
      }

      style.QuantExponents = derivedExponents;
      style.QuantMantissas = derivedMantissas;
    }
  }

  private static Jp2DecodedImage _DecodeTiles(
    Jp2Image image,
    Jp2CodingStyle[] mainStyles,
    Dictionary<int, TileParts> tiles,
    int mainLayers,
    int mainProgression,
    int mainMct,
    bool mainSop,
    bool mainEph
  ) {
    var componentCount = image.Components.Length;
    var planeWidths = new int[componentCount];
    var planeHeights = new int[componentCount];
    var planes = new int[componentCount][];
    var levels = mainStyles[0].DecompositionLevels;

    for (var c = 0; c < componentCount; ++c) {
      planeWidths[c] = Jp2Math.CeilDiv(image.X1, image.Components[c].Dx) - Jp2Math.CeilDiv(image.X0, image.Components[c].Dx);
      planeHeights[c] = Jp2Math.CeilDiv(image.Y1, image.Components[c].Dy) - Jp2Math.CeilDiv(image.Y0, image.Components[c].Dy);
      planes[c] = new int[planeWidths[c] * planeHeights[c]];
    }

    for (var tileIndex = 0; tileIndex < image.TileCount; ++tileIndex) {
      if (!tiles.TryGetValue(tileIndex, out var parts))
        continue;

      var styles = parts.Styles ?? mainStyles;
      var tile = Jp2StructureBuilder.Build(
        image,
        tileIndex,
        styles,
        parts.Layers >= 0 ? parts.Layers : mainLayers,
        parts.ProgressionOrder >= 0 ? parts.ProgressionOrder : mainProgression,
        (parts.Mct >= 0 ? parts.Mct : mainMct) != 0,
        parts.Layers >= 0 ? parts.UseSop : mainSop,
        parts.Layers >= 0 ? parts.UseEph : mainEph,
        allocateCoefficients: true);

      var data = parts.Data.GetBuffer();
      Tier2Decoder.ReadPackets(data, 0, (int)parts.Data.Length, image, tile);
      _DecodeTile(image, tile);

      for (var c = 0; c < componentCount; ++c) {
        var component = tile.Components[c];
        var originX = Jp2Math.CeilDiv(image.X0, image.Components[c].Dx);
        var originY = Jp2Math.CeilDiv(image.Y0, image.Components[c].Dy);

        for (var y = 0; y < component.Height; ++y) {
          var targetRow = (component.Y0 - originY + y) * planeWidths[c];
          for (var x = 0; x < component.Width; ++x)
            planes[c][targetRow + component.X0 - originX + x] = component.Samples[y * component.Width + x];
        }
      }
    }

    return new() {
      Width = image.X1 - image.X0,
      Height = image.Y1 - image.Y0,
      Components = image.Components,
      Planes = planes,
      PlaneWidths = planeWidths,
      PlaneHeights = planeHeights,
      DecompositionLevels = levels,
    };
  }

  private static void _DecodeTile(Jp2Image image, Jp2Tile tile) {
    foreach (var component in tile.Components) {
      foreach (var resolution in component.Resolutions)
        foreach (var band in resolution.Bands) {
          if (band.Width <= 0 || band.Height <= 0)
            continue;

          foreach (var precinct in band.Precincts)
            foreach (var block in precinct.CodeBlocks) {
              if (!block.Included || block.TotalPasses <= 0 || block.Width <= 0 || block.Height <= 0)
                continue;

              var coefficients = Tier1Coder.Decode(
                block.Data.ToArray(),
                block.Width,
                block.Height,
                block.TotalPasses,
                band.MagnitudeBits - block.ZeroBitPlanes,
                band.Orientation,
                component.Style.CodeBlockStyle,
                component.Style.Transform == 1);

              for (var y = 0; y < block.Height; ++y) {
                var target = (block.Y0 - band.Y0 + y) * band.Width + block.X0 - band.X0;
                Array.Copy(coefficients, y * block.Width, band.Coefficients, target, block.Width);
              }
            }
        }

      Jp2Wavelet.InverseTransform(component);
    }

    if (tile.UseMct && tile.Components.Length >= 3)
      _InverseComponentTransform(tile);

    _ = image;
  }

  /// <summary>
  /// Undoes the multiple-component transform. The reversible one is exact in integers and pairs
  /// with the 5/3 filter; the irreversible one is the usual luminance and two colour differences.
  /// </summary>
  private static void _InverseComponentTransform(Jp2Tile tile) {
    var first = tile.Components[0];
    var count = first.Samples.Length;
    for (var c = 1; c < 3; ++c)
      if (tile.Components[c].Samples.Length != count)
        throw new InvalidDataException("JPEG 2000 component transform needs its three components on one sample grid.");

    var a = first.Samples;
    var b = tile.Components[1].Samples;
    var c2 = tile.Components[2].Samples;

    if (first.Style.Transform == 1) {
      for (var i = 0; i < count; ++i) {
        var green = a[i] - ((b[i] + c2[i]) >> 2);
        var red = c2[i] + green;
        var blue = b[i] + green;
        a[i] = red;
        b[i] = green;
        c2[i] = blue;
      }

      return;
    }

    for (var i = 0; i < count; ++i) {
      var y = a[i];
      var cb = b[i];
      var cr = c2[i];
      a[i] = (int)MathF.Round(y + 1.402f * cr);
      b[i] = (int)MathF.Round(y - 0.344136f * cb - 0.714136f * cr);
      c2[i] = (int)MathF.Round(y + 1.772f * cb);
    }
  }
}
