using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Fif;

/// <summary>An FTT template's raster, the external source fragments kinds 4–6 draw on.</summary>
internal sealed record FifTemplateRaster(int Width, int Height, uint Identifier, byte[] Pixels);

/// <summary>Turns FTC transform records into pixels, as DECO_32.DLL does.</summary>
/// <remarks>
/// The attractor is found at half resolution and then drawn once at full resolution:
/// <list type="number">
/// <item>Each 4×4 range block owns four half-resolution nodes. A node depending on a resolved node is
/// computed at once; a chain of unresolved dependencies is followed up to 513 deep and, when it does
/// not end in a resolved node, primed by 32 iterations from mid-grey.</item>
/// <item>Blocks are then re-evaluated from 2×2 averages of their domains, block by block in place,
/// until no sample of any block moves by more than four.</item>
/// <item>The full-resolution block reads its domain from the converged half-resolution image.</item>
/// </list>
/// Domains are addressed linearly, as the DLL does: a sample past a row's end is the next row's, and
/// past the last row is whatever the buffer holds beyond, which here is a zeroed apron.
/// </remarks>
internal static class FifReconstruction {

  /// <summary>Bytes after a half-resolution image that a domain at the bottom edge may read.</summary>
  private const int _APRON = 4096;

  public static byte[] Reconstruct(IReadOnlyList<FifRecord> records, int width, int height, bool color, FifTemplateRaster? template)
    => Reconstruct(records, width, height, color, template, 1);

  /// <summary>
  /// Draws the packed plane at <paramref name="level"/>: 0 is half resolution, 1 the coded size, and
  /// each further level doubles it.
  /// </summary>
  /// <remarks>
  /// Past the converged half-resolution image the DLL never iterates again: each level applies every
  /// transform once to the level below, its range blocks twice as wide, its domains read sample for
  /// sample from the previous level, its domain origins doubled.
  /// </remarks>
  public static byte[] Reconstruct(IReadOnlyList<FifRecord> records, int width, int height, bool color, FifTemplateRaster? template, int level) {
    var packedHeight = color ? height * 3 / 2 : height;
    var expected = width / 4 * (height / 4 + (color ? height / 8 : 0));
    if (records.Count != expected)
      throw new InvalidDataException($"FTC decoded {records.Count} transforms where the plane needs {expected}.");

    var half = _InitializeHalf(records, width, packedHeight, template, out var nodeCount);
    _ConvergeHalf(records, half, width, nodeCount);
    if (level == 0)
      return half[..nodeCount];

    var image = half;
    var raster = template;
    for (var step = 1; step <= level; ++step) {
      if (step >= 2 && raster != null)
        raster = UpsampleTemplate(raster);
      image = _Expand(records, image, width, packedHeight, raster, step);
    }

    return image;
  }

  /// <summary>
  /// A template block at <paramref name="level"/>, in source orientation: the template drawn at that
  /// level supplies all but the last 2^(level-1) − 1 rows and columns, which continue the last step
  /// of each row and then of each column, as the DLL extrapolates them.
  /// </summary>
  private static int[] _TemplateBlock(FifTemplateRaster raster, int baseX, int baseY, int side, int level, int quantized) {
    var read = side + 1 - (1 << (level - 1));
    var block = new int[side * side];
    for (var row = 0; row < read; ++row) {
      var y = Math.Clamp(baseY + row, 0, raster.Height - 1);
      for (var column = 0; column < read; ++column) {
        var x = Math.Clamp(baseX + column, 0, raster.Width - 1);
        block[row * side + column] = FifTransforms.Clamp10(raster.Pixels[y * raster.Width + x] + quantized);
      }

      var step = block[row * side + read - 1] - block[row * side + read - 2];
      for (var column = read; column < side; ++column)
        block[row * side + column] = FifTransforms.Clamp10(block[row * side + column - 1] + step);
    }

    for (var column = 0; column < side; ++column) {
      var step = block[(read - 1) * side + column] - block[(read - 2) * side + column];
      for (var row = read; row < side; ++row)
        block[row * side + column] = FifTransforms.Clamp10(block[(row - 1) * side + column] + step);
    }

    return block;
  }

  /// <summary>
  /// The template at the next level: every sample kept at even positions, the odd ones the rounded
  /// mean of their neighbours along the row and then the column, the last column and row repeated.
  /// </summary>
  public static FifTemplateRaster UpsampleTemplate(FifTemplateRaster raster) {
    int width = raster.Width, height = raster.Height, outputWidth = width * 2;
    var source = raster.Pixels;
    var output = new byte[outputWidth * height * 2];
    for (var row = 0; row < height; ++row) {
      var from = row * width;
      var to = row * 2 * outputWidth;
      for (var x = 0; x < width - 1; ++x) {
        output[to + 2 * x] = source[from + x];
        output[to + 2 * x + 1] = (byte)((source[from + x] + source[from + x + 1] + 1) >> 1);
      }

      output[to + outputWidth - 2] = output[to + outputWidth - 1] = source[from + width - 1];
    }

    for (var row = 1; row < height * 2; row += 2) {
      var at = row * outputWidth;
      for (var x = 0; x < outputWidth; ++x)
        output[at + x] = row == height * 2 - 1
          ? output[at - outputWidth + x]
          : (byte)((output[at - outputWidth + x] + output[at + outputWidth + x] + 1) >> 1);
    }

    return raster with { Width = outputWidth, Height = height * 2, Pixels = output };
  }

  /// <summary>Width of a plane <paramref name="width"/> wide when drawn at <paramref name="level"/>.</summary>
  public static int AtLevel(int width, int level) => level == 0 ? width >> 1 : width << (level - 1);

  private static int _Linear(int baseX, int baseY, int x, int y, int rowWidth) => (baseY + y) * rowWidth + baseX + x;

  private static byte _Read(byte[] buffer, int index) => (uint)index < (uint)buffer.Length ? buffer[index] : (byte)0;

  private static byte[] _InitializeHalf(IReadOnlyList<FifRecord> records, int width, int packedHeight, FifTemplateRaster? template, out int nodeCount) {
    var halfWidth = width / 2;
    nodeCount = halfWidth * (packedHeight / 2);
    var quantized = new int[nodeCount];
    var slopes = new int[nodeCount];
    var sources = new int[nodeCount];
    var states = new byte[nodeCount];
    var output = new byte[nodeCount + _APRON];
    var blockWidth = width / 4;

    for (var block = 0; block < records.Count; ++block) {
      var record = records[block];
      var target = block / blockWidth * 2 * halfWidth + block % blockWidth * 2;
      var baseX = record.DomainX / 2;
      var baseY = record.DomainY / 2;
      for (var row = 0; row < 2; ++row)
      for (var column = 0; column < 2; ++column) {
        var index = target + row * halfWidth + column;
        if (record.Kind >= 4) {
          // A template block averages 2×2 template samples per node, picked in the square the
          // symmetry turns the node into.
          var raster = template ?? throw new InvalidDataException("An FTC transform needs an FTT template that was not supplied.");
          var (cellY, cellX) = FifTransforms.Transform(record.Symmetry & 7, row, column, 2);
          var at = _Linear(baseX, baseY, cellX * 2, cellY * 2, raster.Width);
          var average = (_Read(raster.Pixels, at) + _Read(raster.Pixels, at + 1) + _Read(raster.Pixels, at + raster.Width)
                         + _Read(raster.Pixels, at + raster.Width + 1) + 2) >> 2;
          output[index] = (byte)FifTransforms.Clamp10(average + record.Quantized);
          states[index] = 1;
          continue;
        }

        var (sourceY, sourceX) = FifTransforms.Transform(record.Symmetry, row * 2, column * 2, 4);
        quantized[index] = record.Quantized;
        slopes[index] = record.Slope;
        sources[index] = _Linear(baseX, baseY, sourceX, sourceY, halfWidth);
      }
    }

    var chain = new List<int>(600);
    for (var root = 0; root < nodeCount; ++root) {
      if (states[root] != 0)
        continue;

      var source = sources[root];
      _CheckNode(source, nodeCount);
      if (states[source] == 1) {
        output[root] = (byte)FifTransforms.Affine(quantized[root], output[source], slopes[root]);
        states[root] = 1;
        continue;
      }

      chain.Clear();
      chain.Add(root);
      states[source] = 2;
      var current = source;
      while (chain.Count < 513) {
        chain.Add(current);
        current = sources[current];
        _CheckNode(current, nodeCount);
        if (states[current] != 0)
          break;
        states[current] = 2;
      }

      int value;
      if (states[current] == 1)
        value = output[current];
      else {
        for (var i = 0; i < 32; ++i) {
          chain.Add(current);
          current = sources[current];
          _CheckNode(current, nodeCount);
        }

        value = 128;
        for (var i = chain.Count - 1; i >= chain.Count - 32; --i)
          value = FifTransforms.Affine(quantized[chain[i]], value, slopes[chain[i]]);
        chain.RemoveRange(chain.Count - 32, 32);
      }

      for (var i = chain.Count - 1; i >= 0; --i) {
        var node = chain[i];
        states[node] = 1;
        value = FifTransforms.Affine(quantized[node], value, slopes[node]);
        output[node] = (byte)value;
      }
    }

    return output;
  }

  private static void _CheckNode(int node, int count) {
    if ((uint)node >= (uint)count)
      throw new InvalidDataException("An FTC domain lies outside its plane.");
  }

  private static void _ConvergeHalf(IReadOnlyList<FifRecord> records, byte[] half, int width, int nodeCount) {
    var halfWidth = width / 2;
    var blockWidth = width / 4;
    var converged = new bool[records.Count];
    bool anyChanged;
    do {
      anyChanged = false;
      for (var block = 0; block < records.Count; ++block) {
        if (converged[block])
          continue;

        var record = records[block];
        if (record.Kind >= 4) {
          converged[block] = true;
          continue;
        }

        var target = block / blockWidth * 2 * halfWidth + block % blockWidth * 2;
        var baseX = record.DomainX / 2;
        var baseY = record.DomainY / 2;
        var significant = false;
        for (var row = 0; row < 2; ++row)
        for (var column = 0; column < 2; ++column) {
          var (sourceY, sourceX) = FifTransforms.Transform(record.Symmetry, row * 2, column * 2, 3);
          var at = _Linear(baseX, baseY, sourceX, sourceY, halfWidth);
          var total = _Read(half, at) + _Read(half, at + 1) + _Read(half, at + halfWidth) + _Read(half, at + halfWidth + 1);
          var value = FifTransforms.Affine(record.Quantized, (total + 2) / 4, record.Slope);
          var targetIndex = target + row * halfWidth + column;
          var difference = (value - half[targetIndex]) & 0xFF;
          if (difference is > 4 and < 252)
            significant = true;

          half[targetIndex] = (byte)value;
        }

        if (significant)
          anyChanged = true;
        else
          converged[block] = true;
      }
    } while (anyChanged);
  }

  private static byte[] _Expand(IReadOnlyList<FifRecord> records, byte[] previous, int width, int packedHeight, FifTemplateRaster? template, int level) {
    var previousWidth = AtLevel(width, level - 1);
    var levelWidth = AtLevel(width, level);
    var side = 4 << (level - 1);
    var blockWidth = width / 4;
    var packed = new byte[levelWidth * AtLevel(packedHeight, level)];
    for (var block = 0; block < records.Count; ++block) {
      var record = records[block];
      var target = block / blockWidth * side * levelWidth + block % blockWidth * side;
      var baseX = (record.DomainX << (level - 1)) / 2;
      var baseY = (record.DomainY << (level - 1)) / 2;
      var templateBlock = record.Kind >= 4 ? _TemplateBlock(template!, baseX, baseY, side, level, record.Quantized) : null;
      for (var row = 0; row < side; ++row)
      for (var column = 0; column < side; ++column) {
        var (sourceY, sourceX) = FifTransforms.Transform(record.Symmetry & 7, row, column, side);
        int value;
        if (record.Kind >= 4)
          value = templateBlock![sourceY * side + sourceX];
        else
          value = FifTransforms.Affine(record.Quantized, _Read(previous, _Linear(baseX, baseY, sourceX, sourceY, previousWidth)), record.Slope);

        packed[target + row * levelWidth + column] = (byte)value;
      }
    }

    return packed;
  }

  // ============================================================================================
  // Deblocking
  // ============================================================================================

  /// <summary>The near-side correction table, shared by every filter.</summary>
  public static readonly sbyte[] FilterNear = _BuildNear();

  private static sbyte[] _BuildNear() {
    var near = new sbyte[512];
    for (var i = 0; i < 256; ++i)
      near[i] = (sbyte)Math.Min((i * 2 + 2) / 5, 16);
    for (var i = 256; i < 512; ++i) {
      var value = FifTransforms.CDiv(i * 2 - 0x402, 5) + 0x100;
      value = value < 0xF0 ? 0xF0 : value > 0xFF ? 0 : value;
      near[i] = (sbyte)(byte)value;
    }

    return near;
  }

  /// <summary>The far-side correction table; its positive half divides by a strength-dependent divisor.</summary>
  public static sbyte[] FilterFar(int divisor) {
    var far = new sbyte[512];
    for (var i = 0; i < 256; ++i)
      far[i] = (sbyte)Math.Min((i + 1) / divisor, 16);
    for (var i = 256; i < 512; ++i) {
      var value = FifTransforms.CDiv(i - 0x201, 10) + 0x100;
      value = value < 0xF0 ? 0xF0 : value > 0xFF ? 0 : value;
      far[i] = (sbyte)(byte)value;
    }

    return far;
  }

  private static readonly sbyte[] _farDefault = FilterFar(6);

  private static readonly (int Category, int GroupSize)[] _colorGroups = [(1, 1), (2, 2), (4, 4), (16, 1), (32, 2), (64, 4)];
  private static readonly (int Category, int GroupSize)[] _grayGroups = [(2, 2), (4, 4), (32, 2), (64, 4)];

  /// <summary>The group-width threshold of an FTC picture, whose quality byte is zero.</summary>
  private const int _TRANSFORM_FILTER_THRESHOLD = 4;

  /// <summary>
  /// Smooths one boundary: <paramref name="flags"/> bit 0 adjusts the samples at and after it, bit 1
  /// those before it.
  /// </summary>
  private static void _FilterSide(byte[] pixels, sbyte[] far, int minusTwo, int minusOne, int plusZero, int plusOne, int flags) {
    int near = FilterNear[(pixels[plusZero] - pixels[minusOne]) & 0x1FF];
    int farValue = far[(pixels[plusOne] - pixels[minusTwo]) & 0x1FF];
    var halfNear = FifTransforms.CDiv(near, 2);
    if ((flags & 1) != 0) {
      pixels[plusZero] = (byte)FifTransforms.Clamp10(pixels[plusZero] - near + farValue);
      pixels[plusOne] = (byte)FifTransforms.Clamp10(pixels[plusOne] - halfNear);
    }

    if ((flags & 2) != 0) {
      pixels[minusOne] = (byte)FifTransforms.Clamp10(pixels[minusOne] + near - farValue);
      pixels[minusTwo] = (byte)FifTransforms.Clamp10(pixels[minusTwo] + halfNear);
    }
  }

  /// <summary>The transform-group boundary filter over one packed FTC region.</summary>
  public static void TransformFilter(IReadOnlyList<FifRecord> records, byte[] packed, int width, int height, bool color)
    => TransformFilter(records, packed, width, height, color, 1);

  /// <summary>The transform-group filter on a plane drawn at <paramref name="level"/>.</summary>
  /// <remarks>
  /// The mask is the same at every level; each block edge is smoothed along its whole drawn length.
  /// A group only marks its edges while the drawn group is wider than four pixels, which at half
  /// resolution drops the 2×2 groups.
  /// </remarks>
  public static void TransformFilter(IReadOnlyList<FifRecord> records, byte[] packed, int width, int height, bool color, int level) {
    var cell = level == 0 ? 2 : 4 << (level - 1);
    var levelWidth = AtLevel(width, level);
    var blockColumns = width / 4;
    var blockRows = height / 4 + (color ? height / 8 : 0);
    var stride = blockColumns + 1;
    var markers = new int[stride * (blockRows + 1)];
    var mask = new byte[stride * (blockRows + 1)];
    for (var i = 0; i < records.Count; ++i)
      markers[i / blockColumns * stride + i % blockColumns] = 1 << records[i].Kind;

    foreach (var (category, groupSize) in color ? _colorGroups : _grayGroups)
      if (cell * groupSize > _TRANSFORM_FILTER_THRESHOLD)
      for (var groupY = 0; groupY < blockRows / groupSize * groupSize; groupY += groupSize)
      for (var groupX = 0; groupX < blockColumns / groupSize * groupSize; groupX += groupSize)
      for (var offset = 0; offset < groupSize; ++offset) {
        if (markers[groupY * stride + groupX + offset] == category)
          mask[groupY * stride + groupX + offset] |= 4;
        if (markers[(groupY + groupSize - 1) * stride + groupX + offset] == category)
          mask[(groupY + groupSize) * stride + groupX + offset] |= 8;
        if (markers[(groupY + offset) * stride + groupX] == category)
          mask[(groupY + offset) * stride + groupX] |= 1;
        if (markers[(groupY + offset) * stride + groupX + groupSize - 1] == category)
          mask[(groupY + offset) * stride + groupX + groupSize] |= 2;
      }

    var primaryRows = color ? blockRows * 2 / 3 : blockRows;
    if (color) {
      for (var row = primaryRows; row < blockRows; ++row)
        mask[row * stride + blockColumns / 2] &= 0xFC;
      for (var column = 0; column < blockColumns; ++column) {
        mask[primaryRows * stride + column] &= 0xF3;
        mask[column] &= 0xF3;
      }
    } else
      for (var column = 0; column < blockColumns; ++column)
        mask[column] &= 0xF3;

    for (var row = 0; row < blockRows; ++row)
      mask[row * stride] &= 0xFC;

    for (var blockY = 0; blockY < blockRows; ++blockY)
    for (var blockX = 0; blockX < blockColumns; ++blockX) {
      var flags = mask[blockY * stride + blockX];
      var origin = blockY * cell * levelWidth + blockX * cell;
      var vertical = flags & 3;
      if (vertical != 0)
        for (var offset = 0; offset < cell; ++offset) {
          var position = origin + offset * levelWidth;
          _FilterSide(packed, _farDefault, position - 2, position - 1, position, position + 1, vertical);
        }

      var horizontal = (flags >> 2) & 3;
      if (horizontal != 0)
        for (var offset = 0; offset < cell; ++offset) {
          var position = origin + offset;
          _FilterSide(packed, _farDefault, position - 2 * levelWidth, position - levelWidth, position, position + levelWidth, horizontal);
        }
    }
  }

  /// <summary>Smooths the seams between regions or tiles of the finished RGB picture.</summary>
  /// <remarks>Each region's right edge and then its bottom edge, both sides, unless the seam lies within two pixels of the border.</remarks>
  public static void RegionBoundaryFilter(byte[] rgb, int width, int height, IEnumerable<(int X, int Y, int Width, int Height)> regions) {
    var stride = width * 3;
    foreach (var region in regions) {
      var right = region.X + region.Width;
      var bottom = region.Y + region.Height;
      if (region.X >= width || region.Y >= height || right <= 0 || bottom <= 0)
        continue;

      var top = Math.Max(region.Y, 0);
      var left = Math.Max(region.X, 0);
      var clippedBottom = Math.Min(bottom, height);
      var clippedRight = Math.Min(right, width);
      if (right >= 2 && right <= width - 2)
        for (var y = top; y < clippedBottom; ++y)
        for (var channel = 0; channel < 3; ++channel) {
          var q = y * stride + (right - 1) * 3 + channel;
          _FilterSide(rgb, _farDefault, q - 3, q, q + 3, q + 6, 3);
        }

      if (bottom >= 2 && bottom <= height - 2)
        for (var x = left; x < clippedRight; ++x)
        for (var channel = 0; channel < 3; ++channel) {
          var q = (bottom - 1) * stride + x * 3 + channel;
          _FilterSide(rgb, _farDefault, q - stride, q, q + stride, q + 2 * stride, 3);
        }
    }
  }

  /// <summary>One FIF1 tile's deblocking, by the transform sizes and the file's quality byte.</summary>
  /// <remarks>
  /// The tile is cut into 2×2 cells. Each transform at least a cell wide marks its four edges for
  /// smoothing when larger than the quality's threshold, and clears them otherwise; finer transforms
  /// clear their cell. Only edges on an 8-pixel grid are then smoothed, both sides.
  /// </remarks>
  public static void ProgressiveTileFilter(byte[] tile, int width, int height, IReadOnlyList<(int X, int Y, int Size)> blocks, int quality)
    => ProgressiveTileFilter(tile, width, height, blocks, quality, 1);

  /// <summary>The FIF1 tile filter on a tile drawn at <paramref name="level"/>, positions and sizes in that level's pixels.</summary>
  /// <remarks>Past the second level the cells grow with the level and every marked cell edge is smoothed.</remarks>
  /// <summary>The transform width a FIF1 tile filter marks edges above, by the file's quality byte.</summary>
  public static int ProgressiveThreshold(int quality) => quality == 0 ? 4 : quality > 0x5A ? 8 : quality > 0x19 ? 4 : 2;

  public static void ProgressiveTileFilter(byte[] tile, int width, int height, IReadOnlyList<(int X, int Y, int Size)> blocks, int quality, int level) {
    var threshold = ProgressiveThreshold(quality);
    var divisor = quality == 0 || quality >= 0x47 ? 6 : quality > 0x1E ? 8 : 10;
    var far = divisor == 6 ? _farDefault : FilterFar(divisor);
    var cell = level <= 2 ? 2 : 1 << (level - 1);
    var everyFourth = cell == 2;
    var columns = width / cell;
    var rows = height / cell;
    var stride = columns + 1;
    var grid = new byte[stride * (rows + 1)];

    var index = 0;
    for (; index < blocks.Count; ++index) {
      var (x, y, size) = blocks[index];
      if (size < cell)
        break;

      byte horizontalOr, horizontalAnd, verticalOr, verticalAnd;
      if (size > threshold)
        (horizontalOr, horizontalAnd, verticalOr, verticalAnd) = (0x0C, 0xFF, 0x03, 0xFF);
      else
        (horizontalOr, horizontalAnd, verticalOr, verticalAnd) = (0, 0xF3, 0, 0xFC);

      var span = size / cell;
      var cellX = x / cell;
      var cellY = y / cell;
      var spanX = cellX + span <= columns ? span : columns - cellX;
      var spanY = cellY + span <= rows ? span : rows - cellY;
      for (var i = 0; i < spanX; ++i) {
        var q = cellY * stride + cellX + i;
        grid[q] = (byte)((grid[q] | horizontalOr) & horizontalAnd);
        if (cellY + span <= rows) {
          q = (cellY + span) * stride + cellX + i;
          grid[q] = (byte)((grid[q] | horizontalOr) & horizontalAnd);
        }
      }

      for (var j = 0; j < spanY; ++j) {
        var q = (cellY + j) * stride + cellX;
        grid[q] = (byte)((grid[q] | verticalOr) & verticalAnd);
        if (cellX + span <= columns) {
          q = (cellY + j) * stride + cellX + span;
          grid[q] = (byte)((grid[q] | verticalOr) & verticalAnd);
        }
      }
    }

    for (; index < blocks.Count; ++index) {
      var (x, y, _) = blocks[index];
      var q = y / cell * stride + x / cell;
      if ((uint)q < (uint)grid.Length)
        grid[q] &= 0xF0;
    }

    for (var i = 0; i < columns; ++i)
      grid[i] &= 0xF3;
    for (var j = 0; j < rows; ++j)
      grid[j * stride] &= 0xFC;

    for (var cy = 0; cy < rows; ++cy)
    for (var cx = 0; cx < columns; ++cx) {
      var flags = grid[cy * stride + cx];
      var origin = cy * cell * width + cx * cell;
      var vertical = flags & 3;
      if (vertical != 0 && (!everyFourth || cx % 4 == 0))
        for (var r = 0; r < cell; ++r) {
          var q = origin + r * width;
          _FilterSide(tile, far, q - 2, q - 1, q, q + 1, vertical);
        }

      var horizontal = (flags >> 2) & 3;
      if (horizontal != 0 && (!everyFourth || cy % 4 == 0))
        for (var c = 0; c < cell; ++c) {
          var q = origin + c;
          _FilterSide(tile, far, q - 2 * width, q - width, q, q + width, horizontal);
        }
    }
  }

  // ============================================================================================
  // Colour
  // ============================================================================================

  private static readonly int[] _redFromV = new int[256];
  private static readonly int[] _greenFromV = new int[256];
  private static readonly int[] _greenFromU = new int[256];

  static FifReconstruction() {
    int value = -0x804, accumulatorV = -0x34187, accumulatorU = -0x31A8F;
    for (var i = 0; i < 128; ++i) {
      _redFromV[i] = FifTransforms.CDiv(value, 10);
      _greenFromV[i] = FifTransforms.CDiv(accumulatorV, 16);
      _greenFromU[i] = FifTransforms.CDiv(accumulatorU, 32);
      value += 0x10;
      accumulatorV += 0x683;
      accumulatorU += 0x635;
    }

    value = 5;
    accumulatorV = 8;
    accumulatorU = 0x10;
    for (var i = 128; i < 256; ++i) {
      _redFromV[i] = FifTransforms.CDiv(value, 10);
      _greenFromV[i] = FifTransforms.CDiv(accumulatorV, 16);
      _greenFromU[i] = FifTransforms.CDiv(accumulatorU, 32);
      value += 0x10;
      accumulatorV += 0x683;
      accumulatorU += 0x635;
    }
  }

  /// <summary>
  /// Converts one packed plane (Y, then rows of U|V halves) to RGB. Chroma is interpolated, never
  /// reaching past <paramref name="limitWidth"/>×<paramref name="limitHeight"/> — the visible part of
  /// the region — as the DLL converts every region on its own.
  /// </summary>
  public static byte[] ToRgb(byte[] packed, int width, int height, bool errorUv, int limitWidth, int limitHeight) {
    var chroma = width * height;
    return ToRgb(packed, 0, width, packed, chroma, packed, chroma + width / 2, width, width, height, errorUv,
      (Math.Min(limitWidth, width) + 1) / 2, (Math.Min(limitHeight, height) + 1) / 2);
  }

  /// <summary>
  /// Converts a luma plane and two half-resolution chroma planes to RGB; chroma interpolation stops
  /// at chroma column <paramref name="clampWidth"/> − 1 and row <paramref name="clampHeight"/> − 1.
  /// </summary>
  public static byte[] ToRgb(byte[] luma, int lumaOffset, int lumaStride, byte[] first, int firstOffset, byte[] second, int secondOffset,
    int chromaStride, int width, int height, bool errorUv, int clampWidth, int clampHeight) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y) {
      var chromaY = y / 2;
      var nextY = Math.Min(chromaY + 1, clampHeight - 1);
      var oddY = (y & 1) != 0;
      var a = chromaY * chromaStride;
      var b = nextY * chromaStride;
      for (var x = 0; x < width; ++x) {
        var chromaX = x / 2;
        var nextX = Math.Min(chromaX + 1, clampWidth - 1);
        var oddX = (x & 1) != 0;
        int first00 = first[firstOffset + a + chromaX], second00 = second[secondOffset + a + chromaX];
        int first01 = first[firstOffset + a + nextX], second01 = second[secondOffset + a + nextX];
        int first10 = first[firstOffset + b + chromaX], second10 = second[secondOffset + b + chromaX];
        int first11 = first[firstOffset + b + nextX], second11 = second[secondOffset + b + nextX];
        var luminance = luma[lumaOffset + y * lumaStride + x];
        var output = (y * width + x) * 3;
        if (errorUv) {
          // The DLL keeps the interpolated E and S at twice their scale.
          int e, s;
          if (oddX && oddY) {
            e = (2 * (first00 + first01 + first10 + first11) + 2) >> 2;
            s = (2 * (second00 + second01 + second10 + second11) + 2) >> 2;
          } else if (oddX) {
            e = first00 + first01;
            s = second00 + second01;
          } else if (oddY) {
            e = first00 + first10;
            s = second00 + second10;
          } else {
            e = first00 * 2;
            s = second00 * 2;
          }

          var shared = ((e + s + 1) / 3 + luminance + 0x56) & 0x3FF;
          rgb[output] = (byte)FifTransforms.Clamp10(shared - e);
          rgb[output + 1] = (byte)FifTransforms.Clamp10(shared - 0x100);
          rgb[output + 2] = (byte)FifTransforms.Clamp10(shared - s);
        } else {
          int u, v;
          if (oddX && oddY) {
            u = (first00 + first01 + first10 + first11 + 2) / 4;
            v = (second00 + second01 + second10 + second11 + 2) / 4;
          } else if (oddX) {
            u = (first00 + first01 + 1) / 2;
            v = (second00 + second01 + 1) / 2;
          } else if (oddY) {
            u = (first00 + first10 + 1) / 2;
            v = (second00 + second10 + 1) / 2;
          } else
            (u, v) = (first00, second00);

          rgb[output] = (byte)FifTransforms.Clamp10(luminance + _redFromV[v]);
          rgb[output + 1] = (byte)FifTransforms.Clamp10(luminance - ((_greenFromU[u] + _greenFromV[v]) >> 7));
          rgb[output + 2] = (byte)FifTransforms.Clamp10(luminance - 0x100 + u * 2);
        }
      }
    }

    return rgb;
  }

  /// <summary>
  /// Draws one region's packed plane, as drawn at the plan's level, into the output at its
  /// rectangle, area-averaging each plane down first when the plan resamples.
  /// </summary>
  public static void Place(byte[] output, in FifOutputPlan plan, (int X, int Y, int Width, int Height) rect, byte[] packed,
    int levelWidth, int levelHeight, bool color, bool errorUv) {
    var (width, height) = (rect.Width, rect.Height);
    if (width <= 0 || height <= 0)
      return;

    byte[] block;
    var limitWidth = Math.Min(plan.Width - rect.X, width);
    var limitHeight = Math.Min(plan.Height - rect.Y, height);
    if (limitWidth <= 0 || limitHeight <= 0)
      return;

    if (!plan.Resamples) {
      if (width != levelWidth || height != levelHeight)
        throw new InvalidOperationException("An unscaled region must keep its drawn size.");

      block = color
        ? ToRgb(packed, width, height, errorUv, limitWidth, limitHeight)
        : GrayToRgb(packed, width * height);
    } else {
      var luma = FifOutputPlan.AreaResample(_Plane(packed, 0, levelWidth, levelWidth, levelHeight), levelWidth, levelHeight, width, height);
      if (!color)
        block = GrayToRgb(luma, width * height);
      else {
        var chromaWidth = (levelWidth + 1) / 2;
        var chromaHeight = (levelHeight + 1) / 2;
        var outputChromaWidth = (width + 1) / 2;
        var outputChromaHeight = (height + 1) / 2;
        var chroma = levelWidth * levelHeight;
        var first = FifOutputPlan.AreaResample(_Plane(packed, chroma, levelWidth, chromaWidth, chromaHeight), chromaWidth, chromaHeight, outputChromaWidth, outputChromaHeight);
        var second = FifOutputPlan.AreaResample(_Plane(packed, chroma + levelWidth / 2, levelWidth, chromaWidth, chromaHeight), chromaWidth, chromaHeight, outputChromaWidth, outputChromaHeight);
        block = ToRgb(luma, 0, width, first, 0, second, 0, outputChromaWidth, width, height, errorUv,
          (limitWidth + 1) / 2, (limitHeight + 1) / 2);
      }
    }

    var stride = plan.Width * 3;
    for (var row = 0; row < limitHeight; ++row)
      Buffer.BlockCopy(block, row * width * 3, output, (rect.Y + row) * stride + rect.X * 3, limitWidth * 3);
  }

  private static byte[] _Plane(byte[] packed, int offset, int stride, int width, int height) {
    var plane = new byte[width * height];
    for (var row = 0; row < height; ++row)
      Buffer.BlockCopy(packed, offset + row * stride, plane, row * width, width);
    return plane;
  }

  /// <summary>Grey replicated into R, G and B.</summary>
  public static byte[] GrayToRgb(byte[] plane, int count) {
    var rgb = new byte[count * 3];
    for (var i = 0; i < count; ++i)
      rgb[3 * i] = rgb[3 * i + 1] = rgb[3 * i + 2] = plane[i];

    return rgb;
  }
}
