using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileFormat.Fif;

/// <summary>The progressive FIF1 container: tiles coded coarse to fine over five quadtree levels.</summary>
/// <remarks>
/// A 56-byte header, chunks C0 (option), optional 31 (palette), FF (FTU original size) and 03 (the
/// restart-offset directory), then per level a global model stream and per tile a restart point. A
/// tile's components (Y, or E, U and V at half size) each split their blocks level by level; each
/// level codes model selections, block brightness residuals from fixed prefix books, and then per
/// model, column by column, domains, symmetries and contrasts.
/// </remarks>
internal sealed class FifProgressive {

  private sealed record Region(int X, int Y, int Width, int Height, int[] Offsets);

  private sealed record Model(int DomainKind, int Components, int SymmetryCount, int SymmetryBase, int AffineCount, int AffineBase);

  private sealed class Transform(int rangeX, int rangeY) {
    public int RangeX = rangeX;
    public int RangeY = rangeY;
    public int DomainX;
    public int DomainY;
    public int RangeCode;
    public int Symmetry;
    public int Affine;
    public int LevelValue;
    public bool Constant;
  }

  private static readonly int[] _nibbleValues = [0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 24, 32, 48, 64, 128];

  /// <summary>Contrasts 0.5 to 1.875 in eighths and a quarter, as 16.16 fixed point.</summary>
  private static readonly int[] _contrast = [0x8000, 0xA000, 0xC000, 0xE000, 0x10000, 0x12000, 0x14000, 0x16000, 0x18000, 0x1A000, 0x1C000, 0x1E000, 0x2000];

  private readonly byte[] _data;
  private bool _color;
  private int _levels;
  private int _initialExponent;
  private int _lumaStep;
  private int _chromaStep;
  private int _headerSize;
  private int _primaryLimit;
  private int _aggregateLimit;
  private int _quality;
  private int[] _globalOffsets = [];
  private Region[] _regions = [];
  private SortedSet<int> _boundaries = [];

  public int OutputWidth { get; private set; }
  public int OutputHeight { get; private set; }
  private int _canvasWidth;
  private int _canvasHeight;

  private FifProgressive(byte[] data) => _data = data;

  public static bool Matches(ReadOnlySpan<byte> data)
    => data.Length >= 4 && data[0] == 'F' && data[1] == 'I' && data[2] == 'F' && data[3] == 1;

  public static FifProgressive Parse(byte[] data) {
    var fif = new FifProgressive(data);
    fif._Parse();
    return fif;
  }

  private static ushort _U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
  private static uint _U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));

  private void _Parse() {
    var data = _data;
    if (data.Length < 56)
      throw new InvalidDataException("File is shorter than the 56-byte FIF1 header.");
    if (data[4] != (byte)'u' || data[5] != 0)
      throw new InvalidDataException("Unsupported FIF1 container version.");

    OutputWidth = (int)_U32(data, 6);
    OutputHeight = (int)_U32(data, 10);
    _headerSize = _U16(data, 14);
    if (OutputWidth <= 0 || OutputHeight <= 0)
      throw new InvalidDataException("FIF1 output dimensions are zero.");
    if (_U32(data, 16) != data.Length)
      throw new InvalidDataException("The FIF1 declared length differs from the file length.");

    var colorTag = data.AsSpan(20, 4);
    var outputTag = data.AsSpan(24, 4);
    if (colorTag.SequenceEqual("EUV\0"u8) && outputTag.SequenceEqual("BGR\0"u8))
      _color = true;
    else if (colorTag.SequenceEqual("Y\0\0\0"u8) && outputTag.SequenceEqual("Y\0\0\0"u8))
      _color = false;
    else
      throw new InvalidDataException("Unsupported FIF1 colour representation.");

    var scale = _U16(data, 28);
    _canvasWidth = _U16(data, 30);
    _canvasHeight = _U16(data, 32);
    _lumaStep = data[34];
    _chromaStep = data[35];
    _initialExponent = data[36];
    _levels = data[37];
    int tileWidth = _U16(data, 38), tileHeight = _U16(data, 40);
    _primaryLimit = (int)_U32(data, 42);
    _aggregateLimit = (int)_U32(data, 46);
    var codec = data[50];
    var chunkCount = data[51];
    if (scale != 1 || _lumaStep != 2 || _chromaStep != 4 || _initialExponent != 4 || _levels != 5 || codec != 0x10)
      throw new InvalidDataException("Unsupported FIF1 transform geometry or codec.");
    if (_canvasWidth == 0 || _canvasHeight == 0 || tileWidth == 0 || tileHeight == 0
        || _primaryLimit <= 0 || _aggregateLimit <= 0 || _primaryLimit > _aggregateLimit)
      throw new InvalidDataException("Invalid FIF1 canvas or tiling fields.");
    if (data[52] != 0 || data[53] != 0 || data[54] != 0)
      throw new InvalidDataException("Nonzero FIF1 reserved header bytes.");

    _quality = data[55];

    var position = 56;
    int? option = null;
    (int Width, int Height)? ftu = null;
    byte[]? directory = null;
    var palette = false;
    var chunkOrder = new List<int>();
    for (var i = 0; i < chunkCount; ++i) {
      if (position + 3 > data.Length)
        throw new InvalidDataException("Truncated FIF1 chunk header.");

      int kind = data[position];
      int length = _U16(data, position + 1);
      position += 3;
      if (position + length > data.Length)
        throw new InvalidDataException("Truncated FIF1 chunk payload.");

      var payload = data.AsSpan(position, length);
      position += length;
      chunkOrder.Add(kind);
      switch (kind) {
        case 0xC0:
          if (option != null || length != 2)
            throw new InvalidDataException("Invalid FIF1 C0 chunk.");
          option = BinaryPrimitives.ReadUInt16LittleEndian(payload);
          if (option is not (2 or 4))
            throw new InvalidDataException("Unsupported FIF1 C0 transform option.");
          break;
        case 0x31:
          if (palette || !_color || length < 5)
            throw new InvalidDataException("Invalid FIF1 palette chunk.");
          palette = true;
          break;
        case 0xFF:
          if (ftu != null || length != 16 || !payload[..8].SequenceEqual("FTU\0\u0001\u0001\b\0"u8))
            throw new InvalidDataException("Invalid FIF1 FTU original-size chunk.");
          ftu = ((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]));
          break;
        case 0x03:
          if (directory != null)
            throw new InvalidDataException("Duplicate FIF1 offset directory.");
          directory = payload.ToArray();
          break;
        default:
          throw new InvalidDataException($"Unsupported FIF1 chunk type 0x{kind:x2}.");
      }
    }

    if (position != _headerSize)
      throw new InvalidDataException("The FIF1 chunk sequence does not end at the header size.");
    if (option == null || ftu == null || directory == null)
      throw new InvalidDataException("A required FIF1 chunk is missing.");
    if (ftu.Value != (OutputWidth, OutputHeight))
      throw new InvalidDataException("The FIF1 header and FTU dimensions disagree.");

    int[] canonical = palette ? [0xC0, 0x31, 0xFF, 0x03] : [0xC0, 0xFF, 0x03];
    if (!chunkOrder.SequenceEqual(canonical))
      throw new InvalidDataException("FIF1 chunks are not in canonical order.");

    var columns = (_canvasWidth + tileWidth - 1) / tileWidth;
    var rows = (_canvasHeight + tileHeight - 1) / tileHeight;
    var regionCount = columns * rows;
    if (directory.Length != (1 + regionCount) * _levels * 4)
      throw new InvalidDataException("The FIF1 offset-directory length is inconsistent.");

    var values = new int[directory.Length / 4];
    for (var i = 0; i < values.Length; ++i)
      values[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(i * 4));

    _globalOffsets = values[.._levels];
    _regions = new Region[regionCount];
    for (var index = 0; index < regionCount; ++index) {
      int x = index % columns * tileWidth, y = index / columns * tileHeight;
      var offsets = new int[_levels];
      for (var level = 0; level < _levels; ++level)
        offsets[level] = values[_levels + level * regionCount + index];
      for (var level = 1; level < _levels; ++level)
        if (offsets[level - 1] > offsets[level])
          throw new InvalidDataException("FIF1 region offsets are not progressive.");

      _regions[index] = new(x, y, Math.Min(tileWidth, _canvasWidth - x), Math.Min(tileHeight, _canvasHeight - y), offsets);
    }

    var all = _globalOffsets.Concat(_regions.SelectMany(r => r.Offsets)).ToArray();
    if (all.Any(o => o < _headerSize || o > data.Length))
      throw new InvalidDataException("A FIF1 stream offset lies outside the data.");
    for (var level = 1; level < _levels; ++level)
      if (_globalOffsets[level - 1] > _globalOffsets[level])
        throw new InvalidDataException("FIF1 global offsets are not progressive.");
    if (_globalOffsets[0] != _regions[0].Offsets[0])
      throw new InvalidDataException("The FIF1 level-zero anchor is inconsistent.");

    _boundaries = [.. all, data.Length];
  }

  private int _StreamEnd(int start) {
    var view = _boundaries.GetViewBetween(start + 1, int.MaxValue);
    return view.Count > 0 ? view.Min : start;
  }

  // ============================================================================================
  // Entropy decoding
  // ============================================================================================

  private static int _Huffman(FifBitReader reader, int symbolTable, int lengthTable, int extensionMetadata = 0, int extensionSymbols = 0) {
    var index = reader.Peek(24) & 0xFF;
    var length = FifProgressiveTables.U8(lengthTable + index);
    if (length is < 1 or > 16)
      throw new InvalidDataException("Invalid FIF1 prefix-code length.");

    int symbol = FifProgressiveTables.S16(symbolTable + index * 2);
    reader.Advance(length);
    if (symbol < 500)
      return symbol;
    if (extensionMetadata == 0)
      throw new InvalidDataException("Unexpected extended FIF1 prefix symbol.");

    var @base = FifProgressiveTables.U16(0x11033310 + symbol * 2);
    var extra = FifProgressiveTables.U8(extensionMetadata + @base * 2);
    if (extra is < 1 or > 8)
      throw new InvalidDataException("Invalid FIF1 extended-prefix width.");

    var suffix = reader.Peek(extra) & ((1 << extra) - 1);
    symbol = FifProgressiveTables.S16(extensionSymbols + (@base + suffix) * 2);
    reader.Advance(extra);
    return symbol;
  }

  private Model[] _Models(int level) {
    if (level == 0)
      return [];

    var start = _globalOffsets[level];
    var reader = new FifBitReader(_data, start, _StreamEnd(start));
    var count = reader.Read(8);
    if (count is < 1 or > 3)
      throw new InvalidDataException($"FIF1 level {level} has an invalid model count {count}.");

    var models = new Model[count];
    for (var i = 0; i < count; ++i) {
      var domainKind = reader.Read(6);
      var components = reader.Read(2);
      if (domainKind is not (0x11 or 0x21 or 0x22) || components is < 1 or > 3)
        throw new InvalidDataException("Invalid FIF1 progressive model descriptor.");

      var v = new int[4];
      for (var j = 0; j < 4; ++j)
        v[j] = _nibbleValues[reader.Read(4)];
      if (v[0] == 0 || v[2] == 0)
        throw new InvalidDataException("A FIF1 model has a zero-sized alphabet.");

      models[i] = new(domainKind, components, v[0], v[1], v[2], v[3]);
    }

    reader.Finish($"FIF1 global model stream {level}");
    return models;
  }

  private static int _SelectionSymbol(FifBitReader reader, int doubledRange) => doubledRange switch {
    2 => _Huffman(reader, 0x110310D0, 0x110312D0) & 0xFF,
    4 => _Huffman(reader, 0x110313D0, 0x110315D0, 0x110316D0, 0x110316D2) & 0xFF,
    8 => _Huffman(reader, 0x11031858, 0x11031A58, 0x11031B58, 0x11031B5A) & 0xFF,
    16 => _Huffman(reader, 0x11031CE0, 0x11031EE0) & 0xFF,
    _ => throw new InvalidDataException($"Unsupported FIF1 progressive range {doubledRange}."),
  };

  private static int _IntensitySymbol(FifBitReader reader, int tableKind) => tableKind switch {
    0 => _Huffman(reader, 0x11031FE0, 0x110321E0, 0x110322E0, 0x110322E2),
    1 => _Huffman(reader, 0x110324E8, 0x110326E8, 0x110327E8, 0x110327EA),
    _ => _Huffman(reader, 0x110329F0, 0x11032BF0, 0x11032CF0, 0x11032CF2),
  };

  private static void _DomainPosition(FifBitReader reader, Transform transform, Model model, int rangeSize, int doubledRange, int domainStep, int domainColumns, int domainBits) {
    switch (model.DomainKind) {
      case 0x21: {
        var symbol = _Huffman(reader, 0x11032DF8, 0x11032FF8) & 0xFF;
        var radius = rangeSize >> 1;
        if (radius < domainStep)
          radius = 0;
        transform.DomainX = transform.RangeX + FifProgressiveTables.S8(0x11033770 + symbol) * domainStep - radius;
        transform.DomainY = transform.RangeY + FifProgressiveTables.S8(0x110337B0 + symbol) * domainStep - radius;
        transform.Symmetry = 0;
        break;
      }
      case 0x11: {
        var symbol = doubledRange == 8
          ? _Huffman(reader, 0x110333F8, 0x110335F8) & 0xFF
          : _Huffman(reader, 0x110330F8, 0x110332F8) & 0xFF;
        transform.DomainX = transform.RangeX + FifProgressiveTables.S8(0x110337F0 + symbol) * domainStep;
        transform.DomainY = transform.RangeY + FifProgressiveTables.S8(0x11033810 + symbol) * domainStep;
        transform.Symmetry = 8;
        break;
      }
      default: {
        var index = reader.Read(domainBits);
        transform.DomainX = index % domainColumns * domainStep;
        transform.DomainY = index / domainColumns * domainStep;
        transform.Symmetry = 0;
        break;
      }
    }
  }

  private List<Transform> _DecodeComponent(Region region, int component, FifBitReader[] readers, Model[][] models) {
    var subsampled = component != 0;
    // Half-size chroma reaches unit ranges one level before luminance and has nothing at the last.
    var componentLevels = _levels - (subsampled ? 1 : 0);
    var encodedStep = component == 0 ? _lumaStep : _chromaStep;
    var domainStep = encodedStep / (subsampled ? 4 : 2);
    if (domainStep == 0)
      throw new InvalidDataException("The FIF1 domain step collapses to zero.");

    var fullScale = 1 << _initialExponent;
    var domainColumns = (region.Width - 1) / encodedStep + 1;
    var domainRows = (region.Height - 1) / encodedStep + 1;
    var domainBits = FifTransforms.BitLength(domainColumns * domainRows);
    var workingWidth = subsampled ? region.Width / 2 : region.Width;
    var workingHeight = subsampled ? region.Height / 2 : region.Height;
    var coordinateStep = subsampled ? fullScale / 4 : fullScale / 2;
    var expected = region.Width / fullScale * (region.Height / fullScale);
    var parents = new List<Transform>();
    for (var y = 0; y < workingHeight / 2; y += coordinateStep)
    for (var x = 0; x < workingWidth / 2; x += coordinateStep)
      parents.Add(new(x, y));

    if (parents.Count != expected || expected == 0 || expected > _aggregateLimit || (expected & 3) != 0)
      throw new InvalidDataException("A FIF1 region has an inconsistent transform capacity.");

    var output = new List<Transform>();
    for (var level = 0; level < componentLevels; ++level) {
      var reader = readers[level];
      var rangeSize = 1 << (_initialExponent - level);
      var doubledRange = rangeSize * 2;
      var rangeCode = rangeSize * 2;
      if (subsampled) {
        rangeCode /= 2;
        rangeSize /= 2;
      }

      var grouped = new SortedDictionary<int, List<Transform>>();
      var applicable = models[level].Select((m, i) => (Index: i, Model: m))
        .Where(p => component == 0 ? p.Model.Components != 2 : p.Model.Components != 1).ToArray();

      List<Transform> split;
      if (level == 0 || applicable.Length == 0)
        split = parents;
      else {
        var mapping = new List<int?> { null };
        mapping.AddRange(applicable.Select(p => (int?)p.Index));
        var assignments = new List<int>(parents.Count);
        for (var i = 0; i < parents.Count / 4; ++i) {
          var packed = _SelectionSymbol(reader, doubledRange);
          for (var shift = 0; shift < 8; shift += 2)
            assignments.Add((packed >> shift) & 3);
        }

        split = [];
        for (var i = 0; i < parents.Count; ++i) {
          var assignment = assignments[i];
          if (assignment >= mapping.Count)
            throw new InvalidDataException("A FIF1 model-selection symbol is out of range.");

          var modelIndex = mapping[assignment];
          if (modelIndex == null)
            split.Add(parents[i]);
          else {
            if (!grouped.TryGetValue(modelIndex.Value, out var list))
              grouped[modelIndex.Value] = list = [];
            list.Add(new(parents[i].RangeX, parents[i].RangeY) { RangeCode = rangeCode, LevelValue = parents[i].LevelValue });
          }
        }
      }

      if (level == 0) {
        var columns = region.Width / fullScale;
        if (columns == 0 || split.Count % columns != 0)
          throw new InvalidDataException("The FIF1 level-zero prediction grid is invalid.");

        var tableKind = component == 0 ? 0 : 1;
        for (var i = 0; i < split.Count; ++i) {
          var predictor = i == 0 ? 0x80 : i < columns ? split[i - 1].LevelValue : split[i - columns].LevelValue;
          split[i].LevelValue = FifTransforms.Clamp10(predictor - _IntensitySymbol(reader, tableKind));
        }

        parents = split;
      } else {
        var fullRange = 1 << (_initialExponent - level);
        var tableKind = component == 0
          ? fullRange < 3 ? 2 : 0
          : fullRange is 8 or 16 ? 1 : fullRange == 4 ? 0 : 2;
        var modifiedRange = subsampled ? fullRange / 2 : fullRange;
        if (modifiedRange == 1)
          foreach (var parent in split) {
            parent.RangeX *= 2;
            parent.RangeY *= 2;
          }

        var childHalf = modifiedRange == 1 ? 1 : modifiedRange / 2;
        var children = new List<Transform>(split.Count * 4);
        foreach (var parent in split) {
          children.Add(new(parent.RangeX, parent.RangeY) { LevelValue = parent.LevelValue });
          foreach (var (dx, dy) in new[] { (childHalf, 0), (0, childHalf), (childHalf, childHalf) })
            children.Add(new(parent.RangeX + dx, parent.RangeY + dy) {
              LevelValue = FifTransforms.Clamp10(parent.LevelValue - _IntensitySymbol(reader, tableKind)),
            });
        }

        parents = children;
        if (level == componentLevels - 1)
          foreach (var transform in parents)
            transform.Constant = true;
      }

      // Columnar across the level: every model's domains, then symmetries, then contrasts.
      foreach (var (modelIndex, transforms) in grouped) {
        var model = models[level][modelIndex];
        foreach (var transform in transforms)
          _DomainPosition(reader, transform, model, rangeSize, doubledRange, domainStep, domainColumns, domainBits);
      }

      foreach (var (modelIndex, transforms) in grouped) {
        var model = models[level][modelIndex];
        var bits = FifTransforms.BitLength(model.SymmetryCount);
        foreach (var transform in transforms) {
          var delta = model.SymmetryCount == 1 ? 0 : reader.Read(bits);
          if (delta >= model.SymmetryCount)
            throw new InvalidDataException("A FIF1 symmetry symbol exceeds its alphabet.");
          transform.Symmetry += model.SymmetryBase + delta;
        }
      }

      foreach (var (modelIndex, transforms) in grouped) {
        var model = models[level][modelIndex];
        var bits = FifTransforms.BitLength(model.AffineCount);
        foreach (var transform in transforms) {
          var delta = model.AffineCount == 1 ? 0 : reader.Read(bits);
          if (delta >= model.AffineCount)
            throw new InvalidDataException("A FIF1 contrast symbol exceeds its alphabet.");
          transform.Affine = model.AffineBase + delta;
          if (transform.Symmetry is < 0 or > 15 || transform.Affine is < 0 or > 255)
            throw new InvalidDataException("A FIF1 model produces an invalid transform.");
        }
      }

      foreach (var (_, transforms) in grouped)
        output.AddRange(transforms);
      if (level == componentLevels - 1)
        output.AddRange(parents);
    }

    return output;
  }

  // ============================================================================================
  // Rendering
  // ============================================================================================

  private static int _Contrast(int affine, int levelValue, int sample, int mean, bool fourSampleSum) {
    if ((uint)affine >= (uint)_contrast.Length || (uint)levelValue > 255)
      throw new InvalidDataException("A FIF1 contrast or brightness is outside its table.");

    var delta = fourSampleSum ? (sample - mean + 2) >> 2 : sample - mean;
    var scaled = (int)(((long)delta * _contrast[affine] + 0x8000) >> 16);
    return FifTransforms.Clamp10(levelValue + scaled);
  }

  /// <summary>Reproduces the DLL's dependency ordering of the same-scale (local-domain) transforms.</summary>
  private static List<Transform> _SpecialOrder(List<Transform> transforms, int width, int height) {
    var ordered = transforms.Where(t => (t.Symmetry & 8) != 0).ToList();
    var active = ordered.Count;
    while (active > 8) {
      var mask = new bool[width * height];
      for (var index = active - 1; index >= 0; --index) {
        var t = ordered[index];
        var side = t.RangeCode / 2;
        for (var row = 0; row < side; ++row) {
          var y = Math.Clamp(t.DomainY + row, 0, height - 1);
          for (var column = 0; column < side; ++column)
            mask[y * width + Math.Clamp(t.DomainX + column, 0, width - 1)] = true;
        }
      }

      var dependent = new bool[active];
      var dependentCount = 0;
      for (var index = active - 1; index >= 0; --index) {
        var t = ordered[index];
        var side = t.RangeCode / 2;
        var found = false;
        for (var row = 0; row < side && !found; ++row) {
          var y = Math.Clamp(t.RangeY + row, 0, height - 1);
          for (var column = 0; column < side; ++column)
            if (mask[y * width + Math.Clamp(t.RangeX + column, 0, width - 1)]) {
              found = true;
              break;
            }
        }

        dependent[index] = found;
        if (found)
          ++dependentCount;
      }

      // An in-place two-ended partition, not a stable one: the order is what the DLL produces.
      int first = 0, last = active - 1;
      while (first < last) {
        if (!dependent[first]) {
          while (first < last && !dependent[last])
            --last;
          if (first == last)
            break;

          (ordered[first], ordered[last]) = (ordered[last], ordered[first]);
          (dependent[first], dependent[last]) = (dependent[last], dependent[first]);
          --last;
        }

        ++first;
      }

      if (dependentCount == active)
        break;
      active = dependentCount;
    }

    for (var end = active - 1; end > 0; --end)
    for (var index = end - 1; index >= 0; --index) {
      var later = ordered[end];
      var earlier = ordered[index];
      var side = earlier.RangeCode / 2;
      if (later.RangeX < earlier.RangeX + side && later.RangeY < earlier.RangeY + side)
        (ordered[index], ordered[end]) = (later, earlier);
    }

    return ordered;
  }

  private static int _Sample(byte[] source, int width, int height, int x, int y)
    => source[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];

  private static void _TransformBlock(byte[] source, byte[] output, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight,
    Transform transform, int side, int targetScale, int domainScale, bool boxReduce) {
    var samples = new int[side * side];
    var sum = 0;
    for (var row = 0; row < side; ++row)
    for (var column = 0; column < side; ++column) {
      var (sy, sx) = FifTransforms.Transform(transform.Symmetry & 7, row, column, side);
      var x = transform.DomainX * domainScale + sx * (boxReduce ? 2 : 1);
      var y = transform.DomainY * domainScale + sy * (boxReduce ? 2 : 1);
      var sample = boxReduce
        ? _Sample(source, sourceWidth, sourceHeight, x, y) + _Sample(source, sourceWidth, sourceHeight, x + 1, y)
          + _Sample(source, sourceWidth, sourceHeight, x, y + 1) + _Sample(source, sourceWidth, sourceHeight, x + 1, y + 1)
        : _Sample(source, sourceWidth, sourceHeight, x, y);
      samples[row * side + column] = sample;
      sum += sample;
    }

    var mean = (sum + samples.Length / 2) / samples.Length;
    var rangeX = transform.RangeX * targetScale;
    var rangeY = transform.RangeY * targetScale;
    if (rangeX < 0 || rangeY < 0 || rangeX + side > targetWidth || rangeY + side > targetHeight)
      throw new InvalidDataException("A FIF1 range block leaves its tile.");

    for (var i = 0; i < samples.Length; ++i)
      output[(rangeY + i / side) * targetWidth + rangeX + i % side] = (byte)_Contrast(transform.Affine, transform.LevelValue, samples[i], mean, boxReduce);
  }

  private static byte[] _RenderRegion(Region region, int component, List<Transform> transforms) {
    var divisor = component == 0 ? 1 : 2;
    var width = region.Width / divisor;
    var height = region.Height / divisor;
    var halfWidth = width / 2;
    var halfHeight = height / 2;
    if (transforms.Count == 0 || halfWidth == 0 || halfHeight == 0)
      throw new InvalidDataException("A FIF1 reconstruction tile is empty.");

    var half = new byte[halfWidth * halfHeight];
    var halfCovered = new bool[half.Length];
    var constants = new List<Transform>();
    var ordinary = new List<Transform>();
    foreach (var t in transforms) {
      if (t.Constant) {
        constants.Add(t);
        continue;
      }

      if (t.RangeCode is not (2 or 4 or 8 or 16))
        throw new InvalidDataException("A FIF1 transform has an invalid range code.");

      var side = t.RangeCode / 2;
      if (t.RangeX < 0 || t.RangeY < 0 || t.RangeX + side > halfWidth || t.RangeY + side > halfHeight)
        throw new InvalidDataException("A FIF1 half-resolution range leaves its tile.");

      for (var row = 0; row < side; ++row)
      for (var column = 0; column < side; ++column) {
        var at = (t.RangeY + row) * halfWidth + t.RangeX + column;
        half[at] = (byte)t.LevelValue;
        halfCovered[at] = true;
      }

      ordinary.Add(t);
    }

    if ((constants.Count & 3) != 0)
      throw new InvalidDataException("The finest FIF1 constants are not grouped in fours.");

    for (var i = 0; i < constants.Count; i += 4) {
      int x = constants[i].RangeX / 2, y = constants[i].RangeY / 2;
      if ((uint)x >= (uint)halfWidth || (uint)y >= (uint)halfHeight)
        throw new InvalidDataException("A finest FIF1 constant leaves its tile.");

      half[y * halfWidth + x] = (byte)((constants[i].LevelValue + constants[i + 1].LevelValue + constants[i + 2].LevelValue + constants[i + 3].LevelValue) >> 2);
      halfCovered[y * halfWidth + x] = true;
    }

    if (halfCovered.Contains(false))
      throw new InvalidDataException("FIF1 transforms do not cover the half-resolution tile.");

    var special = _SpecialOrder(ordinary, halfWidth, halfHeight);
    var normal = ordinary.Where(t => (t.Symmetry & 8) == 0).ToList();
    for (var pass = 0; pass < 4; ++pass) {
      var next = half[..];
      foreach (var t in normal)
        if (t.RangeCode / 2 > 1)
          _TransformBlock(half, next, halfWidth, halfHeight, halfWidth, halfHeight, t, t.RangeCode / 2, 1, 1, true);
      foreach (var t in special)
        if (t.RangeCode / 2 > 1)
          _TransformBlock(next, next, halfWidth, halfHeight, halfWidth, halfHeight, t, t.RangeCode / 2, 1, 1, false);
      half = next;
    }

    var full = new byte[width * height];
    var covered = new bool[full.Length];
    void Cover(Transform t) {
      for (var row = 0; row < t.RangeCode; ++row)
      for (var column = 0; column < t.RangeCode; ++column)
        covered[(t.RangeY * 2 + row) * width + t.RangeX * 2 + column] = true;
    }

    foreach (var t in normal) {
      _TransformBlock(half, full, halfWidth, halfHeight, width, height, t, t.RangeCode, 2, 1, false);
      Cover(t);
    }

    foreach (var t in constants) {
      if ((uint)t.RangeX >= (uint)width || (uint)t.RangeY >= (uint)height)
        throw new InvalidDataException("A full-resolution FIF1 constant leaves its tile.");
      full[t.RangeY * width + t.RangeX] = (byte)t.LevelValue;
      covered[t.RangeY * width + t.RangeX] = true;
    }

    foreach (var t in special) {
      _TransformBlock(full, full, width, height, width, height, t, t.RangeCode, 2, 2, false);
      Cover(t);
    }

    if (covered.Contains(false))
      throw new InvalidDataException("FIF1 transforms do not cover the full-resolution tile.");

    return full;
  }

  public (int Width, int Height, byte[] Rgb) Decode(bool deblock) {
    var componentCount = _color ? 3 : 1;
    var planes = new byte[componentCount][];
    for (var c = 0; c < componentCount; ++c) {
      var divisor = c == 0 ? 1 : 2;
      planes[c] = new byte[_canvasWidth / divisor * (_canvasHeight / divisor)];
    }

    var models = new Model[_levels][];
    for (var level = 0; level < _levels; ++level)
      models[level] = _Models(level);

    // The final restart has no physical end: the DLL reads zeros past its buffer, bounded here by
    // the transform limit.
    var virtualEnd = _data.Length + (long)_aggregateLimit * 16 + 32;
    foreach (var region in _regions) {
      var readers = region.Offsets.Select(start => new FifBitReader(_data, start, virtualEnd)).ToArray();
      var decoded = new List<Transform>[componentCount];
      for (var c = 0; c < componentCount; ++c)
        decoded[c] = _DecodeComponent(region, c, readers, models);

      if (decoded[0].Count > _primaryLimit || decoded.Sum(d => d.Count) > _aggregateLimit)
        throw new InvalidDataException("A FIF1 transform limit is exceeded.");

      for (var c = 0; c < componentCount; ++c) {
        var tile = _RenderRegion(region, c, decoded[c]);
        var divisor = c == 0 ? 1 : 2;
        int tileWidth = region.Width / divisor, tileHeight = region.Height / divisor;
        if (deblock)
          FifReconstruction.ProgressiveTileFilter(tile, tileWidth, tileHeight,
            decoded[c].Select(t => t.Constant ? (t.RangeX, t.RangeY, 1) : (t.RangeX * 2, t.RangeY * 2, t.RangeCode)).ToList(), _quality);

        var planeWidth = _canvasWidth / divisor;
        for (var row = 0; row < tileHeight; ++row)
          Buffer.BlockCopy(tile, row * tileWidth, planes[c], (region.Y / divisor + row) * planeWidth + region.X / divisor, tileWidth);
      }
    }

    var canvas = new byte[_canvasWidth * _canvasHeight * 3];
    if (!_color)
      canvas = FifReconstruction.GrayToRgb(planes[0], _canvasWidth * _canvasHeight);
    else
      foreach (var region in _regions) {
        // Each tile converts on its own, so chroma never interpolates across a tile seam.
        int rw = region.Width, rh = region.Height, halfWidth = _canvasWidth / 2;
        var packed = new byte[rw * rh * 3 / 2];
        for (var row = 0; row < rh; ++row)
          Buffer.BlockCopy(planes[0], (region.Y + row) * _canvasWidth + region.X, packed, row * rw, rw);
        for (var row = 0; row < rh / 2; ++row) {
          var source = (region.Y / 2 + row) * halfWidth + region.X / 2;
          var target = rw * rh + row * rw;
          Buffer.BlockCopy(planes[1], source, packed, target, rw / 2);
          Buffer.BlockCopy(planes[2], source, packed, target + rw / 2, rw / 2);
        }

        var block = FifReconstruction.ToRgb(packed, rw, rh, errorUv: true, OutputWidth - region.X, OutputHeight - region.Y);
        for (var row = 0; row < rh; ++row)
          Buffer.BlockCopy(block, row * rw * 3, canvas, ((region.Y + row) * _canvasWidth + region.X) * 3, rw * 3);
      }

    var output = new byte[OutputWidth * OutputHeight * 3];
    for (var row = 0; row < OutputHeight; ++row)
      Buffer.BlockCopy(canvas, row * _canvasWidth * 3, output, row * OutputWidth * 3, OutputWidth * 3);

    if (deblock)
      FifReconstruction.RegionBoundaryFilter(output, OutputWidth, OutputHeight, _regions.Select(r => (r.X, r.Y, r.Width, r.Height)));

    return (OutputWidth, OutputHeight, output);
  }
}
