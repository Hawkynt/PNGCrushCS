using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Integer helpers for the coordinate arithmetic in ITU-T T.800 Annex B.</summary>
/// <remarks>
/// The band origin subtracts a power of two before dividing, so these have to be floor and ceiling
/// over the whole of the integers rather than the truncation C# gives for division.
/// </remarks>
internal static class Jp2Math {

  public static int CeilDiv(int numerator, int denominator) {
    var quotient = numerator / denominator;
    return numerator % denominator != 0 && (numerator > 0) == (denominator > 0) ? quotient + 1 : quotient;
  }

  public static int CeilDivPow2(int value, int exponent) => (value + (1 << exponent) - 1) >> exponent;

  public static int FloorDivPow2(int value, int exponent) => value >> exponent;
}

/// <summary>One image component's sample geometry and precision, from SIZ.</summary>
internal sealed class Jp2Component {
  public int Precision { get; init; } = 8;
  public bool Signed { get; init; }
  public int Dx { get; init; } = 1;
  public int Dy { get; init; } = 1;
}

/// <summary>The image grid and tile partition from SIZ.</summary>
internal sealed class Jp2Image {
  public int X0 { get; init; }
  public int Y0 { get; init; }
  public int X1 { get; init; }
  public int Y1 { get; init; }
  public int TileX0 { get; init; }
  public int TileY0 { get; init; }
  public int TileWidth { get; init; }
  public int TileHeight { get; init; }
  public Jp2Component[] Components { get; init; } = [];

  public int TilesWide => Jp2Math.CeilDiv(this.X1 - this.TileX0, this.TileWidth);
  public int TilesHigh => Jp2Math.CeilDiv(this.Y1 - this.TileY0, this.TileHeight);
  public int TileCount => this.TilesWide * this.TilesHigh;
}

/// <summary>Per-component coding style and quantization, from COD/COC and QCD/QCC.</summary>
internal sealed class Jp2CodingStyle {

  /// <summary>Number of wavelet decomposition levels; resolutions are one more than this.</summary>
  public int DecompositionLevels { get; set; } = 5;

  /// <summary>Base-two logarithm of the nominal code-block width.</summary>
  public int CodeBlockWidthExp { get; set; } = 6;

  public int CodeBlockHeightExp { get; set; } = 6;

  /// <summary>SPcod code-block style flags (arithmetic bypass, causal contexts, and so on).</summary>
  public int CodeBlockStyle { get; set; }

  /// <summary>1 for the reversible 5/3 filter, 0 for the irreversible 9/7.</summary>
  public int Transform { get; set; } = 1;

  /// <summary>Base-two logarithm of the precinct width, one entry per resolution.</summary>
  public int[] PrecinctWidthExp { get; set; } = [];

  public int[] PrecinctHeightExp { get; set; } = [];

  /// <summary>0 = no quantization, 1 = scalar derived, 2 = scalar expounded.</summary>
  public int QuantizationStyle { get; set; }

  public int GuardBits { get; set; } = 2;

  /// <summary>Quantization exponents indexed by subband, LL first then HL/LH/HH per resolution.</summary>
  public int[] QuantExponents { get; set; } = [];

  public int[] QuantMantissas { get; set; } = [];

  public Jp2CodingStyle Clone() => new() {
    DecompositionLevels = this.DecompositionLevels,
    CodeBlockWidthExp = this.CodeBlockWidthExp,
    CodeBlockHeightExp = this.CodeBlockHeightExp,
    CodeBlockStyle = this.CodeBlockStyle,
    Transform = this.Transform,
    PrecinctWidthExp = (int[])this.PrecinctWidthExp.Clone(),
    PrecinctHeightExp = (int[])this.PrecinctHeightExp.Clone(),
    QuantizationStyle = this.QuantizationStyle,
    GuardBits = this.GuardBits,
    QuantExponents = (int[])this.QuantExponents.Clone(),
    QuantMantissas = (int[])this.QuantMantissas.Clone(),
  };

  /// <summary>Fills in the default precinct partition, one precinct covering each resolution.</summary>
  public void UseDefaultPrecincts() {
    var count = this.DecompositionLevels + 1;
    this.PrecinctWidthExp = new int[count];
    this.PrecinctHeightExp = new int[count];
    Array.Fill(this.PrecinctWidthExp, 15);
    Array.Fill(this.PrecinctHeightExp, 15);
  }
}

/// <summary>One code-block: its rectangle in subband coordinates plus the packet-header state.</summary>
internal sealed class Jp2CodeBlock {
  public int X0 { get; init; }
  public int Y0 { get; init; }
  public int X1 { get; init; }
  public int Y1 { get; init; }

  public int Width => this.X1 - this.X0;
  public int Height => this.Y1 - this.Y0;

  /// <summary>Whether any layer has included this block yet.</summary>
  public bool Included { get; set; }

  /// <summary>Length-signalling state from B.10.7.1, three until a packet header widens it.</summary>
  public int Lblock { get; set; } = 3;

  public int ZeroBitPlanes { get; set; }

  public int TotalPasses { get; set; }

  public MemoryStream Data { get; } = new();

  /// <summary>Encoder side: the MQ codeword for the whole block.</summary>
  public byte[] Encoded { get; set; } = [];

  /// <summary>Encoder side: magnitude bit-planes the block actually needed.</summary>
  public int MagnitudeBits { get; set; }
}

/// <summary>One precinct's slice of a subband and the code-blocks inside it.</summary>
internal sealed class Jp2Precinct {
  public int CodeBlocksWide { get; init; }
  public int CodeBlocksHigh { get; init; }
  public Jp2CodeBlock[] CodeBlocks { get; init; } = [];
  public TagTree? Inclusion { get; set; }
  public TagTree? ZeroBitPlanes { get; set; }
}

/// <summary>One subband of one resolution level.</summary>
internal sealed class Jp2Band {
  /// <summary>0 = LL, 1 = HL, 2 = LH, 3 = HH.</summary>
  public int Orientation { get; init; }

  public int X0 { get; init; }
  public int Y0 { get; init; }
  public int X1 { get; init; }
  public int Y1 { get; init; }

  public int Width => this.X1 - this.X0;
  public int Height => this.Y1 - this.Y0;

  /// <summary>Mb from E.1: guard bits plus exponent less one.</summary>
  /// <remarks>
  /// Settable because an encoder only learns how much dynamic range the transform actually produced
  /// after it has run, and the guard bits it then writes have to reach the bands as well.
  /// </remarks>
  public int MagnitudeBits { get; set; }

  /// <summary>Reconstruction step size for the irreversible path.</summary>
  public float StepSize { get; init; } = 1f;

  public Jp2Precinct[] Precincts { get; init; } = [];

  /// <summary>Coefficients in raster order over the band rectangle.</summary>
  public int[] Coefficients { get; init; } = [];
}

/// <summary>One resolution level of one tile-component.</summary>
internal sealed class Jp2Resolution {
  public int X0 { get; init; }
  public int Y0 { get; init; }
  public int X1 { get; init; }
  public int Y1 { get; init; }
  public int PrecinctWidthExp { get; init; }
  public int PrecinctHeightExp { get; init; }
  public int PrecinctsWide { get; init; }
  public int PrecinctsHigh { get; init; }
  public Jp2Band[] Bands { get; init; } = [];

  public int PrecinctCount => this.PrecinctsWide * this.PrecinctsHigh;
}

/// <summary>One component of one tile, with all its resolutions built out.</summary>
internal sealed class Jp2TileComponent {
  public int X0 { get; init; }
  public int Y0 { get; init; }
  public int X1 { get; init; }
  public int Y1 { get; init; }
  public Jp2Resolution[] Resolutions { get; init; } = [];
  public Jp2CodingStyle Style { get; init; } = new();

  public int Width => this.X1 - this.X0;
  public int Height => this.Y1 - this.Y0;

  /// <summary>Reconstructed samples in raster order, before the level shift.</summary>
  public int[] Samples { get; set; } = [];
}

/// <summary>One tile: its rectangle on the reference grid and one structure per component.</summary>
internal sealed class Jp2Tile {
  public int Index { get; init; }
  public int X0 { get; init; }
  public int Y0 { get; init; }
  public int X1 { get; init; }
  public int Y1 { get; init; }
  public Jp2TileComponent[] Components { get; init; } = [];

  /// <summary>Quality layers this tile's packets are split into.</summary>
  public int Layers { get; init; } = 1;

  /// <summary>Progression order from SGcod, 0 = LRCP through 4 = CPRL.</summary>
  public int ProgressionOrder { get; init; }

  public bool UseMct { get; init; }
  public bool UseSop { get; init; }
  public bool UseEph { get; init; }
}

/// <summary>Builds the Annex B tile structure for one tile of an image.</summary>
internal static class Jp2StructureBuilder {

  public static Jp2Tile Build(
    Jp2Image image,
    int tileIndex,
    Jp2CodingStyle[] componentStyles,
    int layers,
    int progressionOrder,
    bool useMct,
    bool useSop,
    bool useEph,
    bool allocateCoefficients
  ) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(componentStyles);

    var tilesWide = image.TilesWide;
    var tileX = tileIndex % tilesWide;
    var tileY = tileIndex / tilesWide;

    var tx0 = Math.Max(image.TileX0 + tileX * image.TileWidth, image.X0);
    var ty0 = Math.Max(image.TileY0 + tileY * image.TileHeight, image.Y0);
    var tx1 = Math.Min(image.TileX0 + (tileX + 1) * image.TileWidth, image.X1);
    var ty1 = Math.Min(image.TileY0 + (tileY + 1) * image.TileHeight, image.Y1);

    var components = new Jp2TileComponent[image.Components.Length];
    for (var c = 0; c < components.Length; ++c)
      components[c] = _BuildComponent(image.Components[c], componentStyles[c], tx0, ty0, tx1, ty1, allocateCoefficients);

    return new() {
      Index = tileIndex,
      X0 = tx0,
      Y0 = ty0,
      X1 = tx1,
      Y1 = ty1,
      Components = components,
      Layers = layers,
      ProgressionOrder = progressionOrder,
      UseMct = useMct,
      UseSop = useSop,
      UseEph = useEph,
    };
  }

  private static Jp2TileComponent _BuildComponent(
    Jp2Component component,
    Jp2CodingStyle style,
    int tx0,
    int ty0,
    int tx1,
    int ty1,
    bool allocateCoefficients
  ) {
    var x0 = Jp2Math.CeilDiv(tx0, component.Dx);
    var y0 = Jp2Math.CeilDiv(ty0, component.Dy);
    var x1 = Jp2Math.CeilDiv(tx1, component.Dx);
    var y1 = Jp2Math.CeilDiv(ty1, component.Dy);

    var levels = style.DecompositionLevels;
    var resolutions = new Jp2Resolution[levels + 1];

    for (var resolution = 0; resolution <= levels; ++resolution) {
      var levelNo = levels - resolution;
      var rx0 = Jp2Math.CeilDivPow2(x0, levelNo);
      var ry0 = Jp2Math.CeilDivPow2(y0, levelNo);
      var rx1 = Jp2Math.CeilDivPow2(x1, levelNo);
      var ry1 = Jp2Math.CeilDivPow2(y1, levelNo);

      var ppx = style.PrecinctWidthExp[resolution];
      var ppy = style.PrecinctHeightExp[resolution];

      var precinctLeft = Jp2Math.FloorDivPow2(rx0, ppx) << ppx;
      var precinctTop = Jp2Math.FloorDivPow2(ry0, ppy) << ppy;
      var precinctRight = Jp2Math.CeilDivPow2(rx1, ppx) << ppx;
      var precinctBottom = Jp2Math.CeilDivPow2(ry1, ppy) << ppy;

      var precinctsWide = rx0 == rx1 ? 0 : (precinctRight - precinctLeft) >> ppx;
      var precinctsHigh = ry0 == ry1 ? 0 : (precinctBottom - precinctTop) >> ppy;

      // B.7: below the top resolution the code-block group lives in the subband, which is half the
      // size of the resolution, so both the group origin and its exponent halve with it.
      int groupLeft, groupTop, groupWidthExp, groupHeightExp, bandCount;
      if (resolution == 0) {
        groupLeft = precinctLeft;
        groupTop = precinctTop;
        groupWidthExp = ppx;
        groupHeightExp = ppy;
        bandCount = 1;
      } else {
        groupLeft = Jp2Math.CeilDivPow2(precinctLeft, 1);
        groupTop = Jp2Math.CeilDivPow2(precinctTop, 1);
        groupWidthExp = ppx - 1;
        groupHeightExp = ppy - 1;
        bandCount = 3;
      }

      var codeBlockWidthExp = Math.Min(style.CodeBlockWidthExp, groupWidthExp);
      var codeBlockHeightExp = Math.Min(style.CodeBlockHeightExp, groupHeightExp);

      var bands = new Jp2Band[bandCount];
      for (var b = 0; b < bandCount; ++b) {
        var orientation = resolution == 0 ? 0 : b + 1;
        int bx0, by0, bx1, by1;
        if (resolution == 0) {
          bx0 = Jp2Math.CeilDivPow2(x0, levelNo);
          by0 = Jp2Math.CeilDivPow2(y0, levelNo);
          bx1 = Jp2Math.CeilDivPow2(x1, levelNo);
          by1 = Jp2Math.CeilDivPow2(y1, levelNo);
        } else {
          var xob = orientation & 1;
          var yob = (orientation >> 1) & 1;
          bx0 = Jp2Math.CeilDivPow2(x0 - (xob << levelNo), levelNo + 1);
          by0 = Jp2Math.CeilDivPow2(y0 - (yob << levelNo), levelNo + 1);
          bx1 = Jp2Math.CeilDivPow2(x1 - (xob << levelNo), levelNo + 1);
          by1 = Jp2Math.CeilDivPow2(y1 - (yob << levelNo), levelNo + 1);
        }

        var bandIndex = resolution == 0 ? 0 : 3 * (resolution - 1) + b + 1;
        _GetQuantization(style, component, bandIndex, orientation, out var magnitudeBits, out var stepSize);

        var precincts = new Jp2Precinct[precinctsWide * precinctsHigh];
        for (var p = 0; p < precincts.Length; ++p)
          precincts[p] = _BuildPrecinct(
            p, precinctsWide, groupLeft, groupTop, groupWidthExp, groupHeightExp,
            codeBlockWidthExp, codeBlockHeightExp, bx0, by0, bx1, by1);

        var size = Math.Max(0, bx1 - bx0) * Math.Max(0, by1 - by0);
        bands[b] = new() {
          Orientation = orientation,
          X0 = bx0,
          Y0 = by0,
          X1 = bx1,
          Y1 = by1,
          MagnitudeBits = magnitudeBits,
          StepSize = stepSize,
          Precincts = precincts,
          Coefficients = allocateCoefficients ? new int[size] : [],
        };
      }

      resolutions[resolution] = new() {
        X0 = rx0,
        Y0 = ry0,
        X1 = rx1,
        Y1 = ry1,
        PrecinctWidthExp = ppx,
        PrecinctHeightExp = ppy,
        PrecinctsWide = precinctsWide,
        PrecinctsHigh = precinctsHigh,
        Bands = bands,
      };
    }

    return new() {
      X0 = x0,
      Y0 = y0,
      X1 = x1,
      Y1 = y1,
      Resolutions = resolutions,
      Style = style,
      Samples = allocateCoefficients ? new int[Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0)] : [],
    };
  }

  private static Jp2Precinct _BuildPrecinct(
    int precinctIndex,
    int precinctsWide,
    int groupLeft,
    int groupTop,
    int groupWidthExp,
    int groupHeightExp,
    int codeBlockWidthExp,
    int codeBlockHeightExp,
    int bx0,
    int by0,
    int bx1,
    int by1
  ) {
    var groupX0 = groupLeft + (precinctIndex % precinctsWide) * (1 << groupWidthExp);
    var groupY0 = groupTop + (precinctIndex / precinctsWide) * (1 << groupHeightExp);
    var groupX1 = groupX0 + (1 << groupWidthExp);
    var groupY1 = groupY0 + (1 << groupHeightExp);

    var px0 = Math.Max(groupX0, bx0);
    var py0 = Math.Max(groupY0, by0);
    var px1 = Math.Min(groupX1, bx1);
    var py1 = Math.Min(groupY1, by1);

    if (px0 >= px1 || py0 >= py1)
      return new() { CodeBlocksWide = 0, CodeBlocksHigh = 0, CodeBlocks = [] };

    var blockLeft = Jp2Math.FloorDivPow2(px0, codeBlockWidthExp) << codeBlockWidthExp;
    var blockTop = Jp2Math.FloorDivPow2(py0, codeBlockHeightExp) << codeBlockHeightExp;
    var blockRight = Jp2Math.CeilDivPow2(px1, codeBlockWidthExp) << codeBlockWidthExp;
    var blockBottom = Jp2Math.CeilDivPow2(py1, codeBlockHeightExp) << codeBlockHeightExp;

    var wide = (blockRight - blockLeft) >> codeBlockWidthExp;
    var high = (blockBottom - blockTop) >> codeBlockHeightExp;

    var blocks = new Jp2CodeBlock[wide * high];
    for (var i = 0; i < blocks.Length; ++i) {
      var cx0 = blockLeft + (i % wide) * (1 << codeBlockWidthExp);
      var cy0 = blockTop + (i / wide) * (1 << codeBlockHeightExp);
      blocks[i] = new() {
        X0 = Math.Max(cx0, px0),
        Y0 = Math.Max(cy0, py0),
        X1 = Math.Min(cx0 + (1 << codeBlockWidthExp), px1),
        Y1 = Math.Min(cy0 + (1 << codeBlockHeightExp), py1),
      };
    }

    return new() { CodeBlocksWide = wide, CodeBlocksHigh = high, CodeBlocks = blocks };
  }

  /// <summary>E.1: turns the exponent and mantissa for one subband into Mb and a step size.</summary>
  private static void _GetQuantization(
    Jp2CodingStyle style,
    Jp2Component component,
    int bandIndex,
    int orientation,
    out int magnitudeBits,
    out float stepSize
  ) {
    var exponent = bandIndex < style.QuantExponents.Length ? style.QuantExponents[bandIndex] : component.Precision;
    var mantissa = bandIndex < style.QuantMantissas.Length ? style.QuantMantissas[bandIndex] : 0;

    magnitudeBits = exponent + style.GuardBits - 1;

    // E.1.1's nominal range. The subband gain belongs to the reversible filter, whose coefficients
    // grow with each high-pass stage; the irreversible filter is normalised and takes none.
    var gain = style.Transform == 1 ? orientation switch { 0 => 0, 3 => 2, _ => 1 } : 0;
    var rangeBits = component.Precision + gain;
    stepSize = (float)((1.0 + mantissa / 2048.0) * Math.Pow(2.0, rangeBits - exponent));
  }
}
