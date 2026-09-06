using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// The adaptive CDF state a tile decodes against. A frame builds one of these from the default
/// tables, then every tile takes its own copy: AV1 resets the probabilities at each tile boundary so
/// that tiles stay independently decodable.
/// </summary>
internal sealed class Av1CdfContext {

  // Symbol counts, kept beside the arrays because the stride of a table is the widest CDF in it and
  // says nothing about how many symbols a particular read uses.
  public const int PartitionStride = Av1DefaultCdfTables.PartitionStride;
  public const int KeyFrameYModeStride = Av1DefaultCdfTables.KeyFrameYModeStride;
  public const int UvModeStride = Av1DefaultCdfTables.UvModeStride;
  public const int AngleDeltaStride = Av1DefaultCdfTables.AngleDeltaStride;
  public const int IntraExtTxStride = Av1DefaultCdfTables.IntraExtTxStride;
  public const int CflAlphaStride = Av1DefaultCdfTables.CflAlphaStride;
  public const int TxSizeStride = Av1DefaultCdfTables.TxSizeStride;

  private Av1CdfContext() { }

  public ushort[] Partition = null!;
  public ushort[] KeyFrameYMode = null!;
  public ushort[] UvMode = null!;
  public ushort[] AngleDelta = null!;
  public ushort[] FilterIntra = null!;
  public ushort[] FilterIntraMode = null!;
  public ushort[] CflSign = null!;
  public ushort[] CflAlpha = null!;
  public ushort[] Skip = null!;
  public ushort[] TxSize = null!;
  public ushort[] IntraExtTx = null!;
  public ushort[] DeltaQ = null!;
  public ushort[] DeltaLf = null!;
  public ushort[] DeltaLfMulti = null!;
  public ushort[] SegmentId = null!;
  public ushort[] PaletteYMode = null!;
  public ushort[] PaletteUvMode = null!;
  public ushort[] IntraBc = null!;
  public ushort[] WienerRestore = null!;
  public ushort[] SgrProjRestore = null!;
  public ushort[] SwitchableRestore = null!;

  public ushort[] TxbSkip = null!;
  public ushort[] EobExtra = null!;
  public ushort[] DcSign = null!;
  public ushort[] CoeffBase = null!;
  public ushort[] CoeffBaseEob = null!;
  public ushort[] CoeffBr = null!;

  /// <summary>End-of-block position CDFs, one table per transform area from 16 up to 1024
  /// coefficients, indexed by <c>txSizeLog2Minus4</c>.</summary>
  public ushort[][] EobPt = null!;

  /// <summary>Symbol count of the end-of-block CDF for each transform area.</summary>
  public static ReadOnlySpan<int> EobPtSymbols => [5, 6, 7, 8, 9, 10, 11];

  /// <summary>Row stride of the end-of-block CDF for each transform area.</summary>
  public static ReadOnlySpan<int> EobPtStrides => [
    Av1CoefficientCdfTables.EobPt16Stride, Av1CoefficientCdfTables.EobPt32Stride,
    Av1CoefficientCdfTables.EobPt64Stride, Av1CoefficientCdfTables.EobPt128Stride,
    Av1CoefficientCdfTables.EobPt256Stride, Av1CoefficientCdfTables.EobPt512Stride,
    Av1CoefficientCdfTables.EobPt1024Stride,
  ];

  /// <summary>
  /// Builds the frame's starting CDFs. The coefficient tables come in four sets and the frame's
  /// <c>base_q_idx</c> picks one, because coefficient statistics differ far more with the quantiser
  /// than the mode statistics do (libaom <c>get_q_ctx</c>).
  /// </summary>
  public static Av1CdfContext CreateDefault(int baseQIndex) {
    var q = baseQIndex <= 20 ? 0 : baseQIndex <= 60 ? 1 : baseQIndex <= 120 ? 2 : 3;

    var context = new Av1CdfContext {
      Partition = (ushort[])Av1DefaultCdfTables.Partition.Clone(),
      KeyFrameYMode = (ushort[])Av1DefaultCdfTables.KeyFrameYMode.Clone(),
      UvMode = (ushort[])Av1DefaultCdfTables.UvMode.Clone(),
      AngleDelta = (ushort[])Av1DefaultCdfTables.AngleDelta.Clone(),
      FilterIntra = (ushort[])Av1DefaultCdfTables.FilterIntra.Clone(),
      FilterIntraMode = (ushort[])Av1DefaultCdfTables.FilterIntraMode.Clone(),
      CflSign = (ushort[])Av1DefaultCdfTables.CflSign.Clone(),
      CflAlpha = (ushort[])Av1DefaultCdfTables.CflAlpha.Clone(),
      Skip = (ushort[])Av1DefaultCdfTables.Skip.Clone(),
      TxSize = (ushort[])Av1DefaultCdfTables.TxSize.Clone(),
      IntraExtTx = (ushort[])Av1DefaultCdfTables.IntraExtTx.Clone(),
      DeltaQ = (ushort[])Av1DefaultCdfTables.DeltaQ.Clone(),
      DeltaLf = (ushort[])Av1DefaultCdfTables.DeltaLf.Clone(),
      DeltaLfMulti = (ushort[])Av1DefaultCdfTables.DeltaLfMulti.Clone(),
      SegmentId = (ushort[])Av1DefaultCdfTables.SegmentId.Clone(),
      PaletteYMode = (ushort[])Av1DefaultCdfTables.PaletteYMode.Clone(),
      PaletteUvMode = (ushort[])Av1DefaultCdfTables.PaletteUvMode.Clone(),
      IntraBc = (ushort[])Av1DefaultCdfTables.IntraBc.Clone(),
      WienerRestore = (ushort[])Av1DefaultCdfTables.WienerRestore.Clone(),
      SgrProjRestore = (ushort[])Av1DefaultCdfTables.SgrProjRestore.Clone(),
      SwitchableRestore = (ushort[])Av1DefaultCdfTables.SwitchableRestore.Clone(),

      TxbSkip = _Slice(Av1CoefficientCdfTables.TxbSkip, q, 4),
      EobExtra = _Slice(Av1CoefficientCdfTables.EobExtra, q, 4),
      DcSign = _Slice(Av1CoefficientCdfTables.DcSign, q, 4),
      CoeffBase = _Slice(Av1CoefficientCdfTables.CoeffBase, q, 4),
      CoeffBaseEob = _Slice(Av1CoefficientCdfTables.CoeffBaseEob, q, 4),
      CoeffBr = _Slice(Av1CoefficientCdfTables.CoeffBr, q, 4),
    };

    context.EobPt = [
      _Slice(Av1CoefficientCdfTables.EobPt16, q, 4),
      _Slice(Av1CoefficientCdfTables.EobPt32, q, 4),
      _Slice(Av1CoefficientCdfTables.EobPt64, q, 4),
      _Slice(Av1CoefficientCdfTables.EobPt128, q, 4),
      _Slice(Av1CoefficientCdfTables.EobPt256, q, 4),
      _Slice(Av1CoefficientCdfTables.EobPt512, q, 4),
      _Slice(Av1CoefficientCdfTables.EobPt1024, q, 4),
    ];

    return context;
  }

  /// <summary>Takes the per-tile copy AV1 resets probabilities to at every tile boundary.</summary>
  public Av1CdfContext Clone() {
    var copy = (Av1CdfContext)this.MemberwiseClone();
    copy.Partition = (ushort[])this.Partition.Clone();
    copy.KeyFrameYMode = (ushort[])this.KeyFrameYMode.Clone();
    copy.UvMode = (ushort[])this.UvMode.Clone();
    copy.AngleDelta = (ushort[])this.AngleDelta.Clone();
    copy.FilterIntra = (ushort[])this.FilterIntra.Clone();
    copy.FilterIntraMode = (ushort[])this.FilterIntraMode.Clone();
    copy.CflSign = (ushort[])this.CflSign.Clone();
    copy.CflAlpha = (ushort[])this.CflAlpha.Clone();
    copy.Skip = (ushort[])this.Skip.Clone();
    copy.TxSize = (ushort[])this.TxSize.Clone();
    copy.IntraExtTx = (ushort[])this.IntraExtTx.Clone();
    copy.DeltaQ = (ushort[])this.DeltaQ.Clone();
    copy.DeltaLf = (ushort[])this.DeltaLf.Clone();
    copy.DeltaLfMulti = (ushort[])this.DeltaLfMulti.Clone();
    copy.SegmentId = (ushort[])this.SegmentId.Clone();
    copy.PaletteYMode = (ushort[])this.PaletteYMode.Clone();
    copy.PaletteUvMode = (ushort[])this.PaletteUvMode.Clone();
    copy.IntraBc = (ushort[])this.IntraBc.Clone();
    copy.WienerRestore = (ushort[])this.WienerRestore.Clone();
    copy.SgrProjRestore = (ushort[])this.SgrProjRestore.Clone();
    copy.SwitchableRestore = (ushort[])this.SwitchableRestore.Clone();
    copy.TxbSkip = (ushort[])this.TxbSkip.Clone();
    copy.EobExtra = (ushort[])this.EobExtra.Clone();
    copy.DcSign = (ushort[])this.DcSign.Clone();
    copy.CoeffBase = (ushort[])this.CoeffBase.Clone();
    copy.CoeffBaseEob = (ushort[])this.CoeffBaseEob.Clone();
    copy.CoeffBr = (ushort[])this.CoeffBr.Clone();
    copy.EobPt = new ushort[this.EobPt.Length][];
    for (var i = 0; i < this.EobPt.Length; ++i)
      copy.EobPt[i] = (ushort[])this.EobPt[i].Clone();
    return copy;
  }

  private static ushort[] _Slice(ushort[] table, int index, int count) {
    var size = table.Length / count;
    var result = new ushort[size];
    Array.Copy(table, index * size, result, 0, size);
    return result;
  }
}
