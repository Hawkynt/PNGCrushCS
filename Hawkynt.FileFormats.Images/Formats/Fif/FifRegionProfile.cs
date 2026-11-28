using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FileFormat.Fif;

/// <summary>A reference from an FTC 3.x file to the FTT template it draws on.</summary>
internal sealed record FifTemplateReference(int Width, int Height, uint Identifier, string FileName);

/// <summary>The region-based FTC 3.1, 3.2 and 3.3 profiles, codec profiles 1 (bit/run-length) and 2 (arithmetic).</summary>
internal sealed class FifRegionProfile {

  private sealed record Region(int X, int Y, int Width, int Height);

  private int _version;
  private int _codecProfile;
  private bool _color;
  private bool _errorUv;
  private int _rangeSize;
  private int _domainStep;
  private int _symmetryBits;
  private FifQuantizer[] _quantizers = [];
  private int _templateStep;
  private int _templateSymmetryBits;
  private FifQuantizer _templateQuantizer = null!;
  private Region[] _regions = [];
  private byte[][] _segments = [];
  private sbyte[] _deltas = [];

  public int OutputWidth { get; private set; }
  public int OutputHeight { get; private set; }
  public List<FifTemplateReference> Templates { get; } = [];

  public static bool Matches(ReadOnlySpan<byte> data)
    => data.Length >= 6 && data[0] == 'F' && data[1] == 'T' && data[2] == 'C' && data[3] == 0 && data[5] == 3;

  public static FifRegionProfile Parse(byte[] data) {
    var profile = new FifRegionProfile();
    profile._Parse(data);
    return profile;
  }

  private static ushort _U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
  private static short _S16(byte[] d, int o) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o));
  private static uint _U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));

  private void _Parse(byte[] data) {
    if (data.Length < 48)
      throw new InvalidDataException("File is shorter than the FTC 3.x fixed header.");
    if (data[5] != 3 || data[4] is < 1 or > 4)
      throw new InvalidDataException("Unsupported FTC 3.x outer-header generation.");

    var colorCode = _U16(data, 6);
    var depth = _U16(data, 12);
    if (colorCode == 0x0101 && depth == 8)
      (_color, _errorUv) = (false, false);
    else if (colorCode is 0x0102 or 0x0104 && depth == 24)
      (_color, _errorUv) = (true, colorCode == 0x0104);
    else
      throw new InvalidDataException($"Unsupported FTC colour/depth combination 0x{colorCode:x4}/{depth}.");

    _codecProfile = _U16(data, 14);
    if (_codecProfile is not (1 or 2))
      throw new InvalidDataException($"Unsupported FTC transform codec profile {_codecProfile}.");

    int mainSize = _U16(data, 16);
    int auxiliarySize = _U16(data, 18);
    var payloadSize = (long)_U32(data, 20);
    var secondarySize = (long)_U32(data, 24);
    if (mainSize < 48 || mainSize + auxiliarySize > data.Length)
      throw new InvalidDataException("Invalid FTC header lengths.");

    // Generations 1 and 2 count a trailing encoder stamp; 3 and 4 a RIFF/WAVE behind a 106-byte descriptor.
    var trailing = data[4] is 1 or 2 ? secondarySize : secondarySize != 0 ? 106 + secondarySize : 0;
    if (mainSize + auxiliarySize + payloadSize + trailing != data.Length)
      throw new InvalidDataException("FTC header and payload lengths do not equal the file length.");

    _version = data[28] * 10 + data[29];
    if (_version is not (31 or 32 or 33))
      throw new InvalidDataException($"Unsupported FTC transform version {data[28]}.{data[29]}.");
    if (_U16(data, 32) != 0 || _U16(data, 34) != 0)
      throw new InvalidDataException("A nonzero FTC image origin is not supported.");

    OutputWidth = _U16(data, 36);
    OutputHeight = _U16(data, 38);
    if (OutputWidth == 0 || OutputHeight == 0)
      throw new InvalidDataException("FTC image dimensions are zero.");
    if (_U16(data, 40) != OutputWidth || _U16(data, 42) != OutputHeight)
      throw new InvalidDataException("FTC stored and original dimensions disagree.");

    int extensionSize = _U16(data, 44);
    var position = 46 + extensionSize;
    if (position + 2 > mainSize)
      throw new InvalidDataException("Truncated FTC transform-header extension.");
    for (var i = 46; i < position; ++i)
      if (data[i] != 0)
        throw new InvalidDataException("Unsupported nonempty FTC transform-header extension.");

    int regionSize = _U16(data, position);
    position += 2;
    if (_U16(data, 30) != regionSize + 20)
      throw new InvalidDataException("Inconsistent FTC transform-header length.");
    if (position + regionSize > mainSize)
      throw new InvalidDataException("Truncated FTC region directory.");

    _ParseDirectory(data.AsSpan(position, regionSize).ToArray());
    position += regionSize;

    var descriptor = position;
    if (descriptor + 20 > mainSize)
      throw new InvalidDataException("Truncated FTC transform descriptor.");

    _rangeSize = _U16(data, descriptor);
    int domainSize = _U16(data, descriptor + 2);
    _domainStep = _U16(data, descriptor + 4);
    _symmetryBits = data[descriptor + 6];
    if (!((_rangeSize, domainSize, _domainStep, _symmetryBits) is (4, 4, 2, 3) or (8, 8, 2, 3)))
      throw new InvalidDataException("Unsupported FTC range/domain geometry.");

    var specs = new List<(int Kind, int Bits, int Step, int Base)>();
    int count, cursor;
    if (_version < 32) {
      int bits = data[descriptor + 7], step = data[descriptor + 8];
      int @base = _S16(data, descriptor + 9);
      count = _U16(data, descriptor + 11);
      cursor = descriptor + 13;
      if (cursor + count * 2 > mainSize)
        throw new InvalidDataException("Truncated FTC quantizer list.");
      for (var i = 0; i < count; ++i)
        specs.Add((_U16(data, cursor + i * 2), bits, step, @base));
      cursor += count * 2;
    } else {
      count = _U16(data, descriptor + 7);
      cursor = descriptor + 9;
      if (cursor + count * 6 > mainSize)
        throw new InvalidDataException("Truncated FTC quantizer list.");
      for (var i = 0; i < count; ++i) {
        var at = cursor + i * 6;
        specs.Add((_U16(data, at), data[at + 2], data[at + 3], _S16(data, at + 4)));
      }

      cursor += count * 6;
    }

    if (count is not (1 or 2))
      throw new InvalidDataException($"Unsupported FTC quantizer count {count}.");

    _quantizers = specs.Select(s => _MakeQuantizer(s.Kind, s.Bits, s.Step, s.Base)).ToArray();

    if (cursor + 13 > mainSize)
      throw new InvalidDataException("Truncated FTC descriptor tail.");

    var tail = data.AsSpan(cursor, 13);
    if (!(tail[0] == 8 && tail[1] == 0 && _U16(data, cursor + 2) is 1 or 3 && tail[4] == 0 && tail[5] == 0
          && tail[6] == 1 && tail[7] == 0 && tail[8] == 3 && tail[9] is >= 1 and <= 12 && tail[10] != 0))
      throw new InvalidDataException("Unsupported FTC descriptor-tail geometry.");

    _templateStep = _U16(data, cursor + 6);
    _templateSymmetryBits = tail[8];
    _templateQuantizer = _MakeTemplateQuantizer(tail[9], tail[10], _S16(data, cursor + 11));
    cursor += 13;

    if (_version >= 33) {
      if (cursor >= mainSize)
        throw new InvalidDataException("Missing FTC 3.3 quantizer-delta count.");

      int deltaCount = data[cursor++];
      if (cursor + deltaCount > mainSize)
        throw new InvalidDataException("Truncated FTC 3.3 quantizer-delta list.");

      _deltas = new sbyte[deltaCount];
      for (var i = 0; i < deltaCount; ++i)
        _deltas[i] = (sbyte)data[cursor + i];
      cursor += deltaCount;
    }

    var statistics = mainSize - cursor;
    if (_version >= 33 ? statistics != 4 : count == 1 ? statistics != 0 : statistics != 5 || data[cursor] != 0)
      throw new InvalidDataException("Unexpected FTC encoder-statistics fields.");

    if (auxiliarySize != 0) {
      if (auxiliarySize < 2 || auxiliarySize != 2 + _U16(data, mainSize) * 3)
        throw new InvalidDataException("FTC auxiliary-palette length is inconsistent.");
    }

    var payloadStart = mainSize + auxiliarySize;
    var segments = new List<byte[]>();
    long offset = payloadStart;
    var payloadEnd = payloadStart + payloadSize;
    foreach (var _ in _regions) {
      if (offset + 4 > payloadEnd)
        throw new InvalidDataException("Missing compressed length for an FTC region.");

      var length = _U32(data, (int)offset);
      offset += 4;
      if (length == 0 || offset + length > payloadEnd)
        throw new InvalidDataException("Invalid compressed length for an FTC region.");

      segments.Add(data.AsSpan((int)offset, (int)length).ToArray());
      offset += length;
    }

    if (offset != payloadEnd)
      throw new InvalidDataException("Bytes remain after the final FTC region stream.");

    _segments = segments.ToArray();
  }

  private void _ParseDirectory(byte[] directory) {
    if (directory.Length < 15)
      throw new InvalidDataException("FTC region directory is too short.");
    if (directory[8] != 0)
      throw new InvalidDataException("Nonzero FTC region-directory reserved byte.");

    var regionCount = directory[13] != 0 ? directory[13] : (int)_U32(directory, 9);
    if (regionCount <= 0)
      throw new InvalidDataException("FTC region directory contains no regions.");

    var cursor = 14 + regionCount * 8;
    if (cursor >= directory.Length)
      throw new InvalidDataException("FTC region directory omits its FTT-reference count.");

    int templateCount = directory[cursor++];
    for (var i = 0; i < templateCount; ++i) {
      if (cursor + 9 > directory.Length)
        throw new InvalidDataException("Truncated FTC FTT reference.");

      int width = _U16(directory, cursor), height = _U16(directory, cursor + 2);
      var identifier = _U32(directory, cursor + 4);
      int nameLength = directory[cursor + 8];
      cursor += 9;
      if (width == 0 || height == 0 || nameLength == 0 || cursor + nameLength > directory.Length)
        throw new InvalidDataException("Invalid FTC FTT reference.");

      var name = directory.AsSpan(cursor, nameLength);
      cursor += nameLength;
      var terminator = name.IndexOf((byte)0);
      if (terminator >= 0)
        name = name[..terminator];

      Templates.Add(new(width, height, identifier, Encoding.Latin1.GetString(name)));
    }

    if (cursor != directory.Length)
      throw new InvalidDataException("Bytes remain after the FTC FTT-reference list.");

    _regions = new Region[regionCount];
    for (var i = 0; i < regionCount; ++i) {
      var at = 14 + i * 8;
      var region = new Region(_U16(directory, at), _U16(directory, at + 2), _U16(directory, at + 4), _U16(directory, at + 6));
      if (region.Width == 0 || region.Height == 0 || ((region.X | region.Y | region.Width | region.Height) & 7) != 0)
        throw new InvalidDataException("FTC regions must be nonempty and eight-pixel aligned.");

      _regions[i] = region;
    }
  }

  private FifQuantizer _MakeQuantizer(int kind, int bits, int step, int @base) {
    if (kind is < 4 or > 8 || bits is < 1 or > 12 || step == 0)
      throw new InvalidDataException($"Unsupported FTC affine quantizer {kind}/{bits}/{step}.");

    var values = FifTransforms.QuantizerValues(kind, bits, step, @base, _version);
    if (values.Any(v => v is < 0 or >= 8192))
      throw new InvalidDataException("FTC quantizer generates an affine index outside 0..8191.");

    return new(kind, bits, step, @base, values);
  }

  /// <summary>The FTT transforms' signed brightness offsets, saturating at -0x200.</summary>
  private static FifQuantizer _MakeTemplateQuantizer(int bits, int step, int @base) {
    var current = @base - 0x100;
    var values = new int[1 << bits];
    for (var i = 0; i < values.Length; ++i, current += step)
      values[i] = Math.Max(current, -0x200) - 0x2000;

    return new(8, bits, step, @base, values);
  }

  // ============================================================================================
  // Decoding
  // ============================================================================================

  public (int Width, int Height, byte[] Rgb) Decode(FifTemplateRaster? template, bool deblock)
    => Decode(template, deblock, FifOutputPlan.For(OutputWidth, OutputHeight, OutputWidth, OutputHeight));

  public (int Width, int Height, byte[] Rgb) Decode(FifTemplateRaster? template, bool deblock, FifOutputPlan plan) {
    if (_rangeSize != 4)
      throw new InvalidDataException("FTC 8×8 range blocks are not supported.");
    if (Templates.Count > 1)
      throw new InvalidDataException("FTC images with several FTT references are not supported.");

    var canvasWidth = _regions.Max(r => r.X + r.Width);
    var canvasHeight = _regions.Max(r => r.Y + r.Height);
    if (OutputWidth > canvasWidth || OutputHeight > canvasHeight)
      throw new InvalidDataException("The FTC picture is larger than the regions that code it.");

    var covered = new bool[canvasWidth * canvasHeight];
    foreach (var region in _regions)
      for (var row = 0; row < region.Height; ++row) {
        var target = (region.Y + row) * canvasWidth + region.X;
        for (var i = 0; i < region.Width; ++i)
          if (covered[target + i])
            throw new InvalidDataException("FTC regions overlap.");
          else
            covered[target + i] = true;
      }

    for (var row = 0; row < OutputHeight; ++row)
    for (var x = 0; x < OutputWidth; ++x)
      if (!covered[row * canvasWidth + x])
        throw new InvalidDataException("FTC regions do not cover the picture.");

    var level = plan.Level;
    var rects = plan.Rectangles(_regions.Select(r => (r.X, r.Y, r.Width, r.Height)).ToArray());
    var output = new byte[plan.Width * plan.Height * 3];
    for (var index = 0; index < _regions.Length; ++index) {
      var region = _regions[index];
      var records = _codecProfile == 2 ? _ParseArithmetic(_segments[index], region, template) : _ParseBits(_segments[index], region, template);
      var packed = FifReconstruction.Reconstruct(records, region.Width, region.Height, _color, template, level);
      if (deblock)
        FifReconstruction.TransformFilter(records, packed, region.Width, region.Height, _color, level);

      FifReconstruction.Place(output, plan, rects[index], packed, FifReconstruction.AtLevel(region.Width, level),
        FifReconstruction.AtLevel(region.Height, level), _color, _errorUv);
    }

    if (deblock)
      FifReconstruction.RegionBoundaryFilter(output, plan.Width, plan.Height, rects);

    return (plan.Width, plan.Height, output);
  }

  private FifRecord _ReadRecord(FifBitReader bits, int domainWidth, int domainHeight, int kind) {
    int raw, selector;
    if (_version < 32) {
      raw = bits.Read(_quantizers[0].Bits);
      selector = _quantizers.Length == 2 ? bits.Read(1) : 0;
    } else {
      selector = _quantizers.Length == 2 ? bits.Read(1) : 0;
      raw = bits.Read(_quantizers[selector].Bits);
    }

    var quantizer = _quantizers[selector];
    if (raw >= quantizer.Values.Length)
      throw new InvalidDataException("FTC quantizer index is outside its table.");

    var domainCount = domainWidth * domainHeight;
    var domain = bits.Read(FifTransforms.BitLength(domainCount));
    if (domain >= domainCount)
      throw new InvalidDataException("FTC domain index is outside its grid.");

    var symmetry = bits.Read(_symmetryBits);
    return new(kind, raw, quantizer.Values[raw], domain % domainWidth * _domainStep, domain / domainWidth * _domainStep, symmetry, quantizer.Kind, selector);
  }

  private FifRecord _ReadTemplateRecord(FifBitReader bits, FifTemplateRaster template, int kind) {
    var raw = bits.Read(_templateQuantizer.Bits);
    var domainWidth = (template.Width + _templateStep - 1) / _templateStep;
    var domainHeight = (template.Height + _templateStep - 1) / _templateStep;
    var domainCount = domainWidth * domainHeight;
    var domain = bits.Read(FifTransforms.BitLength(domainCount));
    if (domain >= domainCount)
      throw new InvalidDataException("FTT domain index is outside its grid.");

    var symmetry = bits.Read(_templateSymmetryBits);
    return new(kind, raw, _templateQuantizer.Values[raw], domain % domainWidth * _templateStep * 2, domain / domainWidth * _templateStep * 2, symmetry, 8);
  }

  private FifRecord[] _ParseBits(byte[] segment, Region region, FifTemplateRaster? template) {
    var bits = new FifBitReader(segment);
    var records = new List<FifRecord>(_ParseBitPlane(bits, region.Width, region.Height, 0, template));
    if (_color) {
      bits.AlignZero("primary-plane padding");
      var prefix = (region.Height + _domainStep - 1) / _domainStep;
      records.AddRange(_ParseBitPlane(bits, region.Width, region.Height / 2, prefix, template));
    }

    bits.Finish("FTC region stream");
    return records.ToArray();
  }

  private FifRecord[] _ParseBitPlane(FifBitReader bits, int width, int height, int domainHeightPrefix, FifTemplateRaster? template) {
    var rangeWidth = width / _rangeSize;
    var rangeHeight = height / _rangeSize;
    var domainWidth = (width + _domainStep - 1) / _domainStep;
    var domainHeight = domainHeightPrefix + (height + _domainStep - 1) / _domainStep;
    var (order, paddedWidth, paddedHeight) = FifTransforms.TileOrder(rangeWidth, rangeHeight);
    var candidates = new List<int>();
    for (var i = 0; i < order.Length; ++i)
      if (order[i] >= 0)
        candidates.Add(i);

    var labels = new byte[paddedWidth * paddedHeight];
    var survivors = FifTransforms.MarkRuns(bits, candidates, labels, 2, "class-2 mask");
    if (survivors.Count > 0)
      survivors = FifTransforms.MarkRuns(bits, survivors, labels, 6, "class-6 mask");
    if (survivors.Count > 0)
      survivors = FifTransforms.MarkRuns(bits, survivors, labels, 1, "class-1 mask");
    if (survivors.Count > 0)
      survivors = FifTransforms.MarkRuns(bits, survivors, labels, 5, "class-5 mask");
    if (survivors.Count > 0)
      FifTransforms.MarkFinalRuns(bits, survivors, labels, 0, 4, "class-0/4 mask");

    if (template == null && candidates.Any(o => labels[o] is 4 or 5 or 6))
      throw new InvalidDataException("This FTC picture draws on an FTT template that was not supplied.");

    var records = new FifRecord?[rangeWidth * rangeHeight];
    void Groups(int label, int groupSize, Func<int, FifRecord> read) {
      var lastGroup = -1;
      FifRecord baseRecord = default;
      for (var ordinal = 0; ordinal < labels.Length; ++ordinal) {
        if (labels[ordinal] != label)
          continue;

        var group = ordinal / (groupSize * groupSize);
        if (group != lastGroup) {
          baseRecord = read(label);
          lastGroup = group;
        }

        if (order[ordinal] >= 0)
          records[order[ordinal]] = FifTransforms.Grouped(baseRecord, ordinal, groupSize);
      }
    }

    FifRecord Own(int kind) => _ReadRecord(bits, domainWidth, domainHeight, kind);
    Groups(2, 4, Own);
    Groups(1, 2, Own);
    for (var ordinal = 0; ordinal < labels.Length; ++ordinal)
      if (labels[ordinal] == 0 && order[ordinal] >= 0)
        records[order[ordinal]] = Own(0);

    if (template != null) {
      FifRecord FromTemplate(int kind) => _ReadTemplateRecord(bits, template, kind);
      Groups(6, 4, FromTemplate);
      Groups(5, 2, FromTemplate);
      for (var ordinal = 0; ordinal < labels.Length; ++ordinal)
        if (labels[ordinal] == 4 && order[ordinal] >= 0)
          records[order[ordinal]] = FromTemplate(4);
    }

    var result = new FifRecord[records.Length];
    for (var i = 0; i < records.Length; ++i)
      result[i] = records[i] ?? throw new InvalidDataException("An FTC range block received no transform.");

    return result;
  }

  // ============================================================================================
  // Codec profile 2
  // ============================================================================================

  private sealed class Group(int kind, int size, int start, List<int> ordinals) {
    public int Kind { get; } = kind;
    public int Size { get; } = size;
    public int Start { get; } = start;
    public List<int> Ordinals { get; } = ordinals;
    public int Selector;
    public int Raw;
    public int Symmetry;
    public int DomainX;
    public int DomainY;
  }

  private FifRecord[] _ParseArithmetic(byte[] segment, Region region, FifTemplateRaster? template) {
    if (template != null)
      throw new InvalidDataException("FTT transforms in arithmetic-coded FTC streams are not supported.");

    var decoder = new FifArithmeticDecoder(segment);
    var records = new List<FifRecord>(_ParseArithmeticPlane(decoder, region.Width, region.Height, null));
    if (_color) {
      if (decoder.BitPosition != 0) {
        ++decoder.Position;
        decoder.BitPosition = 0;
      }

      if (_deltas.Length != 0)
        decoder.Restart();

      records.AddRange(_ParseArithmeticPlane(decoder, region.Width, region.Height / 2, region.Height + (region.Height + 1) / 2));
    }

    // The coder may keep up to three bytes of its final code register unread.
    if (segment.Length - decoder.Position > 3)
      throw new InvalidDataException("FTC arithmetic stream has unexplained trailing bytes.");

    return records.ToArray();
  }

  private FifRecord[] _ParseArithmeticPlane(FifArithmeticDecoder decoder, int width, int height, int? combinedHeight) {
    var rangeWidth = width / 4;
    var (order, paddedWidth, paddedHeight) = FifTransforms.TileOrder(width / 4, height / 4);
    var count = paddedWidth * paddedHeight;
    var labels = new byte[count];
    for (var i = 0; i < count; ++i)
      labels[i] = order[i] < 0 ? (byte)0xFF : (byte)0;

    var flags = decoder.Uniform(4);
    int[] Symbols(int amount) {
      var model = decoder.Model(16);
      var result = new int[amount];
      for (var i = 0; i < amount; ++i)
        result[i] = decoder.Decode(model);
      return result;
    }

    if ((flags & 1) != 0) {
      var dictionary = Symbols(count / 4);
      for (var group = 0; group < dictionary.Length; ++group)
      for (var bit = 0; bit < 4; ++bit) {
        var ordinal = group * 4 + bit;
        if (labels[ordinal] != 0xFF && (dictionary[group] & (1 << bit)) != 0)
          labels[ordinal] |= 4;
      }
    }

    if ((flags & 2) != 0) {
      var classes = Symbols(count / 4);
      var zeroes = Symbols(count / 4);
      for (var group = 0; group < classes.Length; ++group)
      for (var bit = 0; bit < 4; ++bit) {
        var ordinal = group * 4 + bit;
        if (labels[ordinal] == 0xFF)
          continue;
        if ((classes[group] & (1 << bit)) != 0)
          labels[ordinal] |= 2;
        else if ((zeroes[group] & (1 << bit)) == 0)
          labels[ordinal] |= 1;
      }
    } else {
      var classes = Symbols(count / 16);
      var zeroes = Symbols(count / 4);
      for (var super = 0; super < classes.Length; ++super)
      for (var sub = 0; sub < 4; ++sub) {
        var group = super * 4 + sub;
        var label = (classes[super] & (1 << sub)) != 0 ? 2 : 1;
        for (var bit = 0; bit < 4; ++bit) {
          var ordinal = group * 4 + bit;
          if (labels[ordinal] == 0xFF)
            continue;
          labels[ordinal] |= (byte)label;
          if ((zeroes[group] & (1 << bit)) != 0)
            labels[ordinal] &= 0xFC;
        }
      }
    }

    if (labels.Any(l => l is 4 or 5 or 6))
      throw new InvalidDataException("An arithmetic-coded FTC stream references a transform dictionary.");

    var groups = new List<Group>();
    foreach (var (kind, size) in new[] { (2, 4), (1, 2), (0, 1) }) {
      var area = size * size;
      var byGroup = new Dictionary<int, List<int>>();
      var orderOfGroups = new List<int>();
      for (var ordinal = 0; ordinal < labels.Length; ++ordinal) {
        if (labels[ordinal] != kind)
          continue;
        var key = ordinal / area;
        if (!byGroup.TryGetValue(key, out var list)) {
          byGroup[key] = list = [];
          orderOfGroups.Add(key);
        }
        list.Add(ordinal);
      }

      foreach (var key in orderOfGroups)
        groups.Add(new(kind, size, key * area, byGroup[key]));
    }

    if (groups.Count == 0)
      throw new InvalidDataException("An arithmetic-coded FTC plane contains no transforms.");

    var maximumBits = _quantizers.Max(q => q.Bits);
    var mainBits = Math.Min(maximumBits, 9);
    var extraBits = maximumBits - mainBits;
    void Field(int alphabet, Action<Group, int> set) {
      var model = decoder.Model(alphabet);
      foreach (var group in groups)
        set(group, decoder.Decode(model));
    }

    if (_quantizers.Length > 1)
      Field(_quantizers.Length, (g, v) => g.Selector = v);
    Field(1 << mainBits, (g, v) => g.Raw = v << extraBits);
    if (extraBits != 0)
      Field(1 << extraBits, (g, v) => g.Raw |= v);
    Field(1 << _symmetryBits, (g, v) => g.Symmetry = v);

    var domainWidth = (width + _domainStep - 1) / _domainStep;
    var xPredictive = decoder.Uniform(2) != 0;
    var xModel = decoder.Model(domainWidth);
    foreach (var group in groups) {
      var value = decoder.Decode(xModel);
      if (xPredictive) {
        var logical = order[group.Start];
        if (logical < 0)
          throw new InvalidDataException("Invalid FTC profile-2 group origin.");
        var predictor = logical % rangeWidth * _rangeSize / _domainStep;
        value = _Modulo(predictor - domainWidth / 2 + value, domainWidth);
      }

      group.DomainX = value * _domainStep;
    }

    var domainHeight = ((combinedHeight ?? height) + _domainStep - 1) / _domainStep;
    var yPredictive = decoder.Uniform(2) != 0;
    var yModel = decoder.Model(domainHeight);
    foreach (var group in groups) {
      var value = decoder.Decode(yModel);
      if (yPredictive) {
        var logical = order[group.Start];
        if (logical < 0)
          throw new InvalidDataException("Invalid FTC profile-2 group origin.");
        var predictor = logical / rangeWidth * _rangeSize / _domainStep;
        value = _Modulo(predictor - domainHeight / 2 + value, domainHeight);
      }

      group.DomainY = value * _domainStep;
    }

    var records = new FifRecord?[width / 4 * (height / 4)];
    foreach (var group in groups) {
      if (group.Selector >= _quantizers.Length)
        throw new InvalidDataException("FTC profile-2 quantizer selector is outside its table.");

      var quantizer = _quantizers[group.Selector];
      if (group.Raw >= quantizer.Values.Length)
        throw new InvalidDataException("FTC profile-2 quantization index is outside its table.");

      var baseRecord = new FifRecord(group.Kind, group.Raw, quantizer.Values[group.Raw], group.DomainX, group.DomainY, group.Symmetry, quantizer.Kind, group.Selector);
      foreach (var ordinal in group.Ordinals)
        if (order[ordinal] >= 0)
          records[order[ordinal]] = group.Size == 1 ? baseRecord : FifTransforms.Grouped(baseRecord, ordinal, group.Size);
    }

    var complete = new FifRecord[records.Length];
    for (var i = 0; i < records.Length; ++i)
      complete[i] = records[i] ?? throw new InvalidDataException("An FTC profile-2 block lacks a transform.");

    if (_deltas.Length != 0)
      _ApplyDeltas(decoder, complete, labels, order);

    return complete;
  }

  private static int _Modulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;

  /// <summary>
  /// FTC 3.3 nudges each group's brightness by a side-channel delta. The DLL refines every quantizer
  /// to unit steps for this: the widest quantizer's width plus the bits of its step, increment one,
  /// tables laid end to end; a coded index moves to <c>raw &lt;&lt; log2(step)</c> and the delta adds to that.
  /// </summary>
  private void _ApplyDeltas(FifArithmeticDecoder decoder, FifRecord[] records, byte[] labels, int[] order) {
    var widest = _quantizers[0];
    foreach (var quantizer in _quantizers)
      if (widest.Bits < quantizer.Bits)
        widest = quantizer;

    var bits = FifTransforms.BitLength(widest.Step) + widest.Bits;
    var count = 1 << bits;
    var fine = new int[count * _quantizers.Length];
    var shifts = new int[_quantizers.Length];
    for (var i = 0; i < _quantizers.Length; ++i) {
      FifTransforms.QuantizerValues(_quantizers[i].Kind, bits, 1, _quantizers[i].Base, 33).CopyTo(fine, i * count);
      shifts[i] = FifTransforms.BitLength(_quantizers[i].Step);
    }

    int Index(FifRecord r) => r.QuantizerSelector * count + (r.QuantizationIndex << shifts[r.QuantizerSelector]);
    for (var i = 0; i < records.Length; ++i)
      records[i] = records[i] with { Quantized = fine[Index(records[i])] };

    for (var ordinal = 0; ordinal < labels.Length; ++ordinal) {
      if (labels[ordinal] is not (1 or 2))
        continue;

      var delta = decoder.RawBit() == 0 ? _deltas[0] : _deltas[1 + decoder.RawBit()];
      var logical = order[ordinal];
      if (logical < 0)
        throw new InvalidDataException("The FTC 3.3 side channel marks padding.");

      var index = Index(records[logical]) + delta;
      if ((uint)index >= (uint)fine.Length)
        throw new InvalidDataException("An FTC 3.3 quantizer delta leaves its table.");

      records[logical] = records[logical] with { Quantized = fine[index] };
    }
  }
}
