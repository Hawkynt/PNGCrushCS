using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Pdf.Rendering.Icc;

/// <summary>The four ICC rendering intents, numbered as the profile header and PDF's <c>ri</c> number them.</summary>
internal enum IccIntent { Perceptual = 0, RelativeColorimetric = 1, Saturation = 2, AbsoluteColorimetric = 3 }

/// <summary>
/// An ICC profile (ICC.1:2010) read far enough to evaluate it in the device-to-PCS direction, and in the
/// PCS-to-device direction where black-point detection needs it.
/// </summary>
/// <remarks>
/// Converted from Little CMS 2 (MIT): the tag readers of cmstypes.c, the pipeline assembly of cmsio1.c
/// (_cmsReadInputLUT / _cmsReadOutputLUT with their v2 Lab and trilinear adjustments) and the
/// capability tests cmsIsMatrixShaper / cmsIsCLUT. Values flow through the pipeline in Little CMS's own
/// 0..1 encodings so every quantisation it makes is made here at the same place.
/// </remarks>
internal sealed class IccProfile {
  public const uint Gray = 0x47524159, Rgb = 0x52474220, Cmyk = 0x434D594B, Cmy = 0x434D5920, Lab = 0x4C616220, Xyz = 0x58595A20;
  private const uint _OutputClass = 0x70727472, _LinkClass = 0x6C696E6B, _AbstractClass = 0x61627374, _NamedClass = 0x6E6D636C, _DisplayClass = 0x6D6E7472;

  private static readonly uint[] _Device2Pcs = [_Sig("A2B0"), _Sig("A2B1"), _Sig("A2B2"), _Sig("A2B1")];
  private static readonly uint[] _Pcs2Device = [_Sig("B2A0"), _Sig("B2A1"), _Sig("B2A2"), _Sig("B2A1")];

  private readonly byte[] _data;
  private readonly Dictionary<uint, (int Offset, int Length)> _tags = [];

  public uint Version { get; }
  public uint DeviceClass { get; }
  public uint ColorSpace { get; }
  public uint Pcs { get; }

  private IccProfile(byte[] data) {
    this._data = data;
    this.Version = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
    this.DeviceClass = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
    this.ColorSpace = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16));
    this.Pcs = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20));
    var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(128));
    for (var i = 0; i < count && 144 + 12 * i <= data.Length; ++i) {
      var at = 132 + 12 * i;
      var sig = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
      var offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4));
      var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 8));
      if (offset < data.Length && length >= 8 && offset + length <= data.Length)
        this._tags.TryAdd(sig, ((int)offset, (int)length));
    }
  }

  /// <summary>Reads a profile, or null when the header or tag table is not one.</summary>
  public static IccProfile? TryParse(byte[] data) {
    if (data.Length < 132 || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(36)) != 0x61637370) // 'acsp'
      return null;

    var profile = new IccProfile(data);
    return profile.Pcs is Xyz or Lab ? profile : null;
  }

  /// <summary>How many components the profile's colour space has, or 0 for one this does not handle.</summary>
  public int Channels => ChannelsOf(this.ColorSpace);

  public static int ChannelsOf(uint space) => space switch {
    Gray => 1,
    Rgb or Cmy or Lab or Xyz or 0x48535620 or 0x484C5320 or 0x59436272 or 0x59787920 or 0x4C757620 => 3,
    Cmyk => 4,
    _ when (space & 0xFFFFFF00) == 0x4D434800 => _HexDigit(space & 0xFF), // MCH1..MCHF
    _ when (space & 0x00FFFFFF) == 0x00434C52 => _HexDigit(space >> 24), // 2CLR..FCLR
    _ => 0,
  };

  private static int _HexDigit(uint c) => c is >= '1' and <= '9' ? (int)(c - '0') : c is >= 'A' and <= 'F' ? (int)(c - 'A' + 10) : 0;

  public bool IsLinkOrAbstractOrNamed => this.DeviceClass is _LinkClass or _AbstractClass or _NamedClass;
  public bool IsOutputClass => this.DeviceClass == _OutputClass;
  public bool IsV4 => this.Version >= 0x4000000;

  /// <summary>Whether the colour space is one printed with inks (cmssamp.c isInkColorspace).</summary>
  public bool IsInkSpace => this.ColorSpace is Cmyk or Cmy || (this.ColorSpace & 0x00FFFFFF) == 0x00434C52 || (this.ColorSpace & 0xFFFFFF00) == 0x4D434800;

  public bool HasTag(string signature) => this._tags.ContainsKey(_Sig(signature));

  /// <summary>cmsIsMatrixShaper.</summary>
  public bool IsMatrixShaper => this.ColorSpace switch {
    Gray => this.HasTag("kTRC"),
    Rgb => this.HasTag("rXYZ") && this.HasTag("gXYZ") && this.HasTag("bXYZ") && this.HasTag("rTRC") && this.HasTag("gTRC") && this.HasTag("bTRC"),
    _ => false,
  };

  /// <summary>cmsIsIntentSupported: a LUT for exactly that intent in that direction, or a matrix-shaper.</summary>
  public bool SupportsIntent(IccIntent intent, bool asInput)
    => this._tags.ContainsKey((asInput ? _Device2Pcs : _Pcs2Device)[(int)intent]) || this.IsMatrixShaper;

  /// <summary>The media white point the way _cmsReadMediaWhitePoint gives it: D50 when absent or for a v2 display profile.</summary>
  public (double X, double Y, double Z) MediaWhitePoint {
    get {
      var wp = this._ReadXyz(_Sig("wtpt"));
      if (wp == null || (!this.IsV4 && this.DeviceClass == _DisplayClass))
        return (IccMath.D50X, IccMath.D50Y, IccMath.D50Z);
      return wp.Value;
    }
  }

  /// <summary>The device-to-PCS pipeline for an intent (_cmsReadInputLUT), output in the PCS's 0..1 encoding.</summary>
  public IccPipeline? InputPipeline(IccIntent intent) {
    var tag = _Device2Pcs[(int)intent];
    if (!this._tags.ContainsKey(tag))
      tag = _Device2Pcs[0];

    if (this._tags.ContainsKey(tag)) {
      var lut = this._ReadLut(tag, out var isLut16);
      if (lut == null)
        return null;

      if (!isLut16 || this.Pcs != Lab)
        return lut;

      if (this.ColorSpace == Lab)
        lut.Stages.Insert(0, IccStage.Scale(3, 65280.0 / 65535.0));
      lut.Stages.Add(IccStage.Scale(3, 65535.0 / 65280.0));
      return lut;
    }

    if (this.ColorSpace == Gray) {
      var trc = this._ReadCurve(_Sig("kTRC"));
      if (trc == null)
        return null;

      var pipeline = new IccPipeline(1, 3);
      if (this.Pcs == Lab) {
        var empty = IccToneCurve.Table([0x8080, 0x8080]);
        pipeline.Stages.Add(IccStage.Matrix(1, 3, [1, 1, 1], null));
        pipeline.Stages.Add(IccStage.Curves([trc, empty, empty]));
      } else {
        pipeline.Stages.Add(IccStage.Curves([trc]));
        pipeline.Stages.Add(IccStage.Matrix(1, 3, [IccMath.InpAdj * IccMath.D50X, IccMath.InpAdj * IccMath.D50Y, IccMath.InpAdj * IccMath.D50Z], null));
      }

      return pipeline;
    }

    if (this.ColorSpace != Rgb)
      return null;

    var r = this._ReadXyz(_Sig("rXYZ"));
    var g = this._ReadXyz(_Sig("gXYZ"));
    var b = this._ReadXyz(_Sig("bXYZ"));
    var rt = this._ReadCurve(_Sig("rTRC"));
    var gt = this._ReadCurve(_Sig("gTRC"));
    var bt = this._ReadCurve(_Sig("bTRC"));
    if (r == null || g == null || b == null || rt == null || gt == null || bt == null)
      return null;

    double[] m = [
      r.Value.X * IccMath.InpAdj, g.Value.X * IccMath.InpAdj, b.Value.X * IccMath.InpAdj,
      r.Value.Y * IccMath.InpAdj, g.Value.Y * IccMath.InpAdj, b.Value.Y * IccMath.InpAdj,
      r.Value.Z * IccMath.InpAdj, g.Value.Z * IccMath.InpAdj, b.Value.Z * IccMath.InpAdj,
    ];
    var shaper = new IccPipeline(3, 3);
    shaper.Stages.Add(IccStage.Curves([rt, gt, bt]));
    shaper.Stages.Add(IccStage.Matrix(3, 3, m, null));
    if (this.Pcs == Lab)
      shaper.Stages.Add(IccStage.XyzToLab());
    return shaper;
  }

  /// <summary>
  /// The PCS-to-device pipeline for an intent (_cmsReadOutputLUT), LUT-based profiles only; null for
  /// matrix-shapers, which black-point detection never needs in this direction.
  /// </summary>
  public IccPipeline? OutputPipeline(IccIntent intent) {
    var tag = _Pcs2Device[(int)intent];
    if (!this._tags.ContainsKey(tag))
      tag = _Pcs2Device[0];
    if (!this._tags.ContainsKey(tag))
      return null;

    var lut = this._ReadLut(tag, out var isLut16);
    if (lut == null)
      return null;

    // "for 3D LUTs using Lab as indexer space, trilinear interpolation should be used"
    if (this.Pcs == Lab)
      foreach (var stage in lut.Stages)
        if (stage is IccStage.ClutStage clut)
          clut.Trilinear = true;

    if (!isLut16 || this.Pcs != Lab)
      return lut;

    lut.Stages.Insert(0, IccStage.Scale(3, 65280.0 / 65535.0));
    if (this.ColorSpace == Lab)
      lut.Stages.Add(IccStage.Scale(3, 65535.0 / 65280.0));
    return lut;
  }

  private static uint _Sig(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);

  private uint _U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(this._data.AsSpan(at));
  private ushort _U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(this._data.AsSpan(at));
  private double _S15(int at) => BinaryPrimitives.ReadInt32BigEndian(this._data.AsSpan(at)) / 65536.0;

  private void _Need(int at, long length) {
    if (at < 0 || at + length > this._data.Length)
      throw new InvalidOperationException("ICC tag data runs past the end of the profile.");
  }

  private (double X, double Y, double Z)? _ReadXyz(uint tag) {
    if (!this._tags.TryGetValue(tag, out var t) || t.Length < 20 || this._U32(t.Offset) != Xyz)
      return null;
    return (this._S15(t.Offset + 8), this._S15(t.Offset + 12), this._S15(t.Offset + 16));
  }

  private IccToneCurve? _ReadCurve(uint tag) {
    if (!this._tags.TryGetValue(tag, out var t))
      return null;
    try {
      return this._CurveAt(t.Offset, out _);
    } catch (InvalidOperationException) {
      return null;
    }
  }

  /// <summary>A curv or para element at an offset (ReadEmbeddedCurve), with its length in bytes.</summary>
  private IccToneCurve _CurveAt(int at, out int length) {
    this._Need(at, 12);
    var type = this._U32(at);
    if (type == 0x63757276) { // 'curv'
      var count = (int)this._U32(at + 8);
      length = 12 + 2 * count;
      this._Need(at, length);
      switch (count) {
        case 0:
          return IccToneCurve.Parametric(1, [1.0]);
        case 1:
          return IccToneCurve.Parametric(1, [this._U16(at + 12) / 256.0]);
      }

      var table = new ushort[count];
      for (var i = 0; i < count; ++i)
        table[i] = this._U16(at + 12 + 2 * i);
      return IccToneCurve.Table(table);
    }

    if (type == 0x70617261) { // 'para'
      var function = this._U16(at + 8);
      int[] counts = [1, 3, 4, 5, 7];
      if (function >= counts.Length)
        throw new InvalidOperationException("Unknown ICC parametric curve type.");

      var n = counts[function];
      length = 12 + 4 * n;
      this._Need(at, length);
      var parameters = new double[n];
      for (var i = 0; i < n; ++i)
        parameters[i] = this._S15(at + 12 + 4 * i);
      return IccToneCurve.Parametric(function + 1, parameters);
    }

    throw new InvalidOperationException("Unknown ICC curve type.");
  }

  private IccPipeline? _ReadLut(uint tag, out bool isLut16) {
    isLut16 = false;
    if (!this._tags.TryGetValue(tag, out var t))
      return null;
    try {
      var type = this._U32(t.Offset);
      switch (type) {
        case 0x6D667431: // 'mft1'
          return this._ReadMft(t.Offset, false);
        case 0x6D667432: // 'mft2'
          isLut16 = true;
          return this._ReadMft(t.Offset, true);
        case 0x6D414220: // 'mAB '
          return this._ReadMab(t.Offset, true);
        case 0x6D424120: // 'mBA '
          return this._ReadMab(t.Offset, false);
        default:
          return null;
      }
    } catch (InvalidOperationException) {
      return null;
    }
  }

  /// <summary>lut8Type / lut16Type (Type_LUT8_Read, Type_LUT16_Read).</summary>
  private IccPipeline _ReadMft(int at, bool wide) {
    this._Need(at, 48);
    int inputs = this._data[at + 8], outputs = this._data[at + 9], points = this._data[at + 10];
    if (inputs is 0 or > IccMath.MaxChannels || outputs is 0 or > IccMath.MaxChannels || points == 1)
      throw new InvalidOperationException("Bad ICC lut header.");

    var pipeline = new IccPipeline(inputs, outputs);
    var matrix = new double[9];
    for (var i = 0; i < 9; ++i)
      matrix[i] = this._S15(at + 12 + 4 * i);
    if (inputs == 3 && !IccMath.IsIdentity(matrix))
      pipeline.Stages.Add(IccStage.Matrix(3, 3, matrix, null));

    var p = at + 48;
    int inEntries = 256, outEntries = 256;
    if (wide) {
      this._Need(at, 52);
      inEntries = this._U16(at + 48);
      outEntries = this._U16(at + 50);
      p = at + 52;
    }

    p = this._Tables(p, inputs, inEntries, wide, pipeline);
    var size = _GridSize(points, inputs, outputs);
    if (size > 0) {
      this._Need(p, size * (wide ? 2 : 1));
      var table = new ushort[size];
      for (var i = 0; i < size; ++i)
        table[i] = wide ? this._U16(p + 2 * i) : (ushort)(this._data[p + i] * 257);
      p += (int)size * (wide ? 2 : 1);
      var grid = new int[inputs];
      Array.Fill(grid, points);
      pipeline.Stages.Add(new IccStage.ClutStage(grid, outputs, table));
    }

    this._Tables(p, outputs, outEntries, wide, pipeline);
    return pipeline;
  }

  private int _Tables(int p, int channels, int entries, bool wide, IccPipeline pipeline) {
    if (wide && entries == 0)
      return p;
    if (entries < 2)
      throw new InvalidOperationException("Bad ICC lut table size.");

    this._Need(p, (long)channels * entries * (wide ? 2 : 1));
    var curves = new IccToneCurve[channels];
    for (var c = 0; c < channels; ++c) {
      var table = new ushort[entries];
      for (var i = 0; i < entries; ++i, p += wide ? 2 : 1)
        table[i] = wide ? this._U16(p) : (ushort)(this._data[p] * 257);
      curves[c] = IccToneCurve.Table(table);
    }

    pipeline.Stages.Add(IccStage.Curves(curves));
    return p;
  }

  private static long _GridSize(int points, int inputs, int outputs) {
    if (points == 0)
      return 0;
    long size = outputs;
    for (var i = 0; i < inputs; ++i) {
      size *= points;
      if (size > 1 << 26)
        throw new InvalidOperationException("ICC lut grid too large.");
    }

    return size;
  }

  /// <summary>lutAtoBType / lutBtoAType (Type_LUTA2B_Read, Type_LUTB2A_Read).</summary>
  private IccPipeline _ReadMab(int at, bool aToB) {
    this._Need(at, 32);
    int inputs = this._data[at + 8], outputs = this._data[at + 9];
    if (inputs is 0 or > IccMath.MaxChannels || outputs is 0 or > IccMath.MaxChannels)
      throw new InvalidOperationException("Bad ICC lutAtoB header.");

    var offB = (int)this._U32(at + 12);
    var offMatrix = (int)this._U32(at + 16);
    var offM = (int)this._U32(at + 20);
    var offClut = (int)this._U32(at + 24);
    var offA = (int)this._U32(at + 28);
    var pipeline = new IccPipeline(inputs, outputs);
    if (aToB) {
      if (offA != 0) pipeline.Stages.Add(this._CurveSet(at + offA, inputs));
      if (offClut != 0) pipeline.Stages.Add(this._Clut(at + offClut, inputs, outputs));
      if (offM != 0) pipeline.Stages.Add(this._CurveSet(at + offM, outputs));
      if (offMatrix != 0) pipeline.Stages.Add(this._MabMatrix(at + offMatrix));
      if (offB != 0) pipeline.Stages.Add(this._CurveSet(at + offB, outputs));
    } else {
      if (offB != 0) pipeline.Stages.Add(this._CurveSet(at + offB, inputs));
      if (offMatrix != 0) pipeline.Stages.Add(this._MabMatrix(at + offMatrix));
      if (offM != 0) pipeline.Stages.Add(this._CurveSet(at + offM, inputs));
      if (offClut != 0) pipeline.Stages.Add(this._Clut(at + offClut, inputs, outputs));
      if (offA != 0) pipeline.Stages.Add(this._CurveSet(at + offA, outputs));
    }

    return pipeline;
  }

  private IccStage _CurveSet(int at, int count) {
    var curves = new IccToneCurve[count];
    for (var i = 0; i < count; ++i) {
      curves[i] = this._CurveAt(at, out var length);
      at += (length + 3) & ~3;
    }

    return IccStage.Curves(curves);
  }

  private IccStage _MabMatrix(int at) {
    this._Need(at, 48);
    var m = new double[9];
    var offset = new double[3];
    for (var i = 0; i < 9; ++i)
      m[i] = this._S15(at + 4 * i);
    for (var i = 0; i < 3; ++i)
      offset[i] = this._S15(at + 36 + 4 * i);
    return IccStage.Matrix(3, 3, m, offset);
  }

  private IccStage _Clut(int at, int inputs, int outputs) {
    this._Need(at, 20);
    var grid = new int[inputs];
    long size = outputs;
    for (var i = 0; i < inputs; ++i) {
      grid[i] = this._data[at + i];
      if (grid[i] == 1)
        throw new InvalidOperationException("Bad ICC CLUT grid.");
      size *= grid[i];
      if (size > 1 << 26)
        throw new InvalidOperationException("ICC CLUT grid too large.");
    }

    var precision = this._data[at + 16];
    if (precision is not (1 or 2))
      throw new InvalidOperationException("Unknown ICC CLUT precision.");

    var p = at + 20;
    this._Need(p, size * precision);
    var table = new ushort[size];
    for (var i = 0; i < size; ++i)
      table[i] = precision == 2 ? this._U16(p + 2 * i) : (ushort)(this._data[p + i] * 257);
    return new IccStage.ClutStage(grid, outputs, table);
  }
}
