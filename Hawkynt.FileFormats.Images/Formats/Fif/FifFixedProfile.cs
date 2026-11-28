using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Fif;

/// <summary>The fixed FTC 1.1 profile: a 67-byte header and one bit-packed transform stream.</summary>
/// <remarks>
/// Every header byte but the dimensions, payload length and domain step is fixed; anything else is
/// not this profile. The picture is one region: a Y plane and, below it, a half-height plane of U and
/// V side by side. Ranges are 4×4, quantized by the 128-step type-6 table.
/// </remarks>
internal static class FifFixedProfile {

  public const int HeaderSize = 67;

  /// <summary>The header with its variable fields zeroed; see <see cref="Header"/>.</summary>
  private static readonly byte[] _template = [
    (byte)'F', (byte)'T', (byte)'C', 0, 0x01, 0x01, 0x02, 0x01,
    0, 0, 0, 0, 0x18, 0x00, 0x01, 0x00,
    0x43, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0x01, 0x01, 0x10, 0x00,
    0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0x04, 0x00, 0x04, 0x00,
    0x02, 0x00, 0x03, 0x07, 0x04, 0x00, 0x00, 0x01,
    0x00, 0x06, 0x00, 0x08, 0, 0, 0, 0,
    0, 0, 0,
  ];

  /// <summary>The exact header for a picture of this size, payload and domain step.</summary>
  public static byte[] Header(int width, int height, int payloadSize, int domainStep) {
    var header = _template[..];
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)height);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)payloadSize);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(36), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(38), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(48), (ushort)domainStep);
    header[61] = (byte)(domainStep == 8 ? 1 : 0);
    return header;
  }

  public static bool Matches(ReadOnlySpan<byte> data)
    => data.Length >= 20 && data[..8].SequenceEqual(_template.AsSpan(0, 8)) && BinaryPrimitives.ReadUInt32LittleEndian(data[16..]) == HeaderSize;

  public static (int Width, int Height) Size(byte[] data)
    => (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10)));

  public static (int Width, int Height, byte[] Rgb) Decode(byte[] data, bool deblock) => Decode(data, deblock, null);

  public static (int Width, int Height, byte[] Rgb) Decode(byte[] data, bool deblock, (int Width, int Height)? size) {
    if (data.Length < HeaderSize)
      throw new InvalidDataException("FTC file is shorter than its 67-byte header.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10));
    if (width == 0 || height == 0 || width % 8 != 0 || height % 8 != 0)
      throw new InvalidDataException("FTC 1.1 dimensions must be positive multiples of eight.");

    var payloadSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20));
    if (payloadSize <= 0 || HeaderSize + (long)payloadSize != data.Length)
      throw new InvalidDataException("FTC 1.1 header and payload lengths do not equal the file length.");

    var domainStep = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(48));
    if (domainStep is not (2 or 8))
      throw new InvalidDataException($"Unsupported FTC 1.1 domain step {domainStep}.");

    var expected = Header(width, height, payloadSize, domainStep);
    for (var i = 0; i < HeaderSize; ++i)
      if (data[i] != expected[i])
        throw new InvalidDataException($"Unsupported FTC 1.1 header byte at 0x{i:x2}: 0x{data[i]:x2}, expected 0x{expected[i]:x2}.");

    var bits = new FifBitReader(data, HeaderSize);
    var records = new List<FifRecord>(_ParsePlane(bits, width, height, domainStep, 0));
    bits.AlignZero("primary-plane padding");
    records.AddRange(_ParsePlane(bits, width, height / 2, domainStep, (height + domainStep - 1) / domainStep));
    bits.Finish("FTC 1.1 payload");

    var plan = FifOutputPlan.For(width, height, size?.Width ?? width, size?.Height ?? height);
    var packed = FifReconstruction.Reconstruct(records, width, height, color: true, template: null, plan.Level);
    if (deblock)
      FifReconstruction.TransformFilter(records, packed, width, height, color: true, plan.Level);

    var output = new byte[plan.Width * plan.Height * 3];
    var rect = plan.Rectangles([(0, 0, width, height)])[0];
    FifReconstruction.Place(output, plan, rect, packed, FifReconstruction.AtLevel(width, plan.Level), FifReconstruction.AtLevel(height, plan.Level), color: true, errorUv: false);
    return (plan.Width, plan.Height, output);
  }

  private static FifRecord _ReadRecord(FifBitReader bits, int domainWidth, int domainHeight, int domainStep, int kind) {
    var quantization = bits.Read(7);
    var domainCount = domainWidth * domainHeight;
    var domain = bits.Read(FifTransforms.BitLength(domainCount));
    if (domain >= domainCount)
      throw new InvalidDataException($"FTC domain index {domain} is outside a {domainCount}-cell grid.");

    var symmetry = bits.Read(3);
    return new(kind, quantization, FifTransforms.FixedQuantization[quantization],
      domain % domainWidth * domainStep, domain / domainWidth * domainStep, symmetry);
  }

  private static FifRecord[] _ParsePlane(FifBitReader bits, int width, int height, int domainStep, int domainHeightPrefix) {
    var rangeWidth = width / 4;
    var rangeHeight = height / 4;
    var domainWidth = (width + domainStep - 1) / domainStep;
    var domainHeight = domainHeightPrefix + (height + domainStep - 1) / domainStep;
    var (order, paddedWidth, paddedHeight) = FifTransforms.TileOrder(rangeWidth, rangeHeight);
    var candidates = new List<int>();
    for (var i = 0; i < order.Length; ++i)
      if (order[i] >= 0)
        candidates.Add(i);

    var labels = new byte[paddedWidth * paddedHeight];
    var survivors = FifTransforms.MarkRuns(bits, candidates, labels, 2, "class-2 mask");
    if (survivors.Count > 0)
      survivors = FifTransforms.MarkRuns(bits, survivors, labels, 6, "reserved-class mask");
    if (survivors.Count > 0)
      survivors = FifTransforms.MarkRuns(bits, survivors, labels, 1, "class-1 mask");
    if (survivors.Count > 0)
      FifTransforms.MarkRuns(bits, survivors, labels, 1, "class-0/1 mask");

    foreach (var ordinal in candidates)
      if (labels[ordinal] == 6)
        throw new InvalidDataException("Reserved FTC transform class 6 is not part of the fixed profile.");

    var records = new FifRecord?[rangeWidth * rangeHeight];
    foreach (var (label, groupSize) in new[] { (2, 4), (1, 2) }) {
      var lastGroup = -1;
      FifRecord baseRecord = default;
      for (var ordinal = 0; ordinal < labels.Length; ++ordinal) {
        if (labels[ordinal] != label)
          continue;

        var group = ordinal / (groupSize * groupSize);
        if (group != lastGroup) {
          baseRecord = _ReadRecord(bits, domainWidth, domainHeight, domainStep, label);
          lastGroup = group;
        }

        if (order[ordinal] >= 0)
          records[order[ordinal]] = FifTransforms.Grouped(baseRecord, ordinal, groupSize);
      }
    }

    for (var ordinal = 0; ordinal < labels.Length; ++ordinal)
      if (labels[ordinal] == 0 && order[ordinal] >= 0)
        records[order[ordinal]] = _ReadRecord(bits, domainWidth, domainHeight, domainStep, 0);

    var result = new FifRecord[records.Length];
    for (var i = 0; i < records.Length; ++i)
      result[i] = records[i] ?? throw new InvalidDataException("An FTC range block received no transform.");

    return result;
  }
}
