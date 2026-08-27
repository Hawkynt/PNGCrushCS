using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>JPEG 2000 Tier-2 packet parser (ITU-T T.800 B.9-B.12).</summary>
internal static class Tier2Decoder {

  private sealed class BlockState {
    internal required int SubbandIndex { get; init; }
    internal required int X { get; init; }
    internal required int Y { get; init; }
    internal bool Included { get; set; }
    internal int ZeroBitPlanes { get; set; }
    internal int Lblock { get; set; } = 3;
    internal int Passes { get; set; }
    internal MemoryStream Data { get; } = new();
  }

  private readonly record struct Contribution(BlockState State, int Passes, int Length);

  public static List<CodeBlockData> ParsePackets(byte[] data, int offset, int length, TileInfo tile) {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(tile);
    if ((uint)offset > (uint)data.Length || length < 0 || offset + length > data.Length)
      throw new ArgumentOutOfRangeException(nameof(length));

    var subbands = SubbandInfo.ComputeSubbands(tile.Width, tile.Height, tile.DecompLevels);
    var inclusionTrees = new Dictionary<(int Component, int Subband), TagTree>();
    var zeroPlaneTrees = new Dictionary<(int Component, int Subband), TagTree>();
    var states = new Dictionary<(int Subband, int X, int Y), BlockState>();

    for (var component = 0; component < tile.ComponentCount; ++component)
      foreach (var subband in subbands) {
        subband.GetCodeBlockGrid(tile.CodeBlockWidth, tile.CodeBlockHeight, out var countX, out var countY);
        if (countX == 0 || countY == 0)
          continue;
        inclusionTrees[(component, subband.Index)] = new(countX, countY);
        zeroPlaneTrees[(component, subband.Index)] = new(countX, countY);
      }

    var cursor = offset;
    var end = offset + length;

    for (var layer = 0; layer < tile.Layers; ++layer)
      for (var resolution = 0; resolution <= tile.DecompLevels; ++resolution)
        for (var component = 0; component < tile.ComponentCount; ++component)
          cursor = _ParsePacket(
            data, cursor, end, tile, subbands, component, resolution, layer,
            inclusionTrees, zeroPlaneTrees, states);

    var result = new List<CodeBlockData>(states.Count);
    foreach (var state in states.Values) {
      if (!state.Included || state.Passes == 0)
        continue;

      result.Add(new() {
        SubbandIndex = state.SubbandIndex,
        CodeBlockX = state.X,
        CodeBlockY = state.Y,
        NumCodingPasses = state.Passes,
        ZeroBitPlanes = state.ZeroBitPlanes,
        CompressedData = state.Data.ToArray(),
      });
    }

    return result;
  }

  private static int _ParsePacket(
    byte[] data,
    int cursor,
    int end,
    TileInfo tile,
    SubbandInfo[] subbands,
    int component,
    int resolution,
    int layer,
    IReadOnlyDictionary<(int Component, int Subband), TagTree> inclusionTrees,
    IReadOnlyDictionary<(int Component, int Subband), TagTree> zeroPlaneTrees,
    IDictionary<(int Subband, int X, int Y), BlockState> states
  ) {
    if (cursor >= end)
      throw new InvalidDataException("JPEG 2000 tile data ended before all declared LRCP packets were present.");

    var reader = new BitReader(data, cursor, end - cursor);
    if (reader.ReadBit() == 0) {
      reader.AlignToByte();
      return reader.Position;
    }

    var contributions = new List<Contribution>();
    var componentOffset = component * subbands.Length;

    foreach (var subband in _SubbandsForResolution(subbands, tile.DecompLevels, resolution)) {
      subband.GetCodeBlockGrid(tile.CodeBlockWidth, tile.CodeBlockHeight, out var countX, out var countY);
      if (countX == 0 || countY == 0)
        continue;

      var inclusion = inclusionTrees[(component, subband.Index)];
      var zeroPlanes = zeroPlaneTrees[(component, subband.Index)];

      for (var y = 0; y < countY; ++y)
        for (var x = 0; x < countX; ++x) {
          var key = (subband.Index + componentOffset, x, y);
          if (!states.TryGetValue(key, out var state)) {
            state = new() { SubbandIndex = key.Item1, X = x, Y = y };
            states.Add(key, state);
          }

          if (!state.Included) {
            if (!inclusion.Decode(x, y, layer + 1, reader))
              continue;

            state.Included = true;
            var threshold = 1;
            while (!zeroPlanes.Decode(x, y, threshold, reader)) {
              ++threshold;
              if (threshold > tile.BitsPerComponent + 38)
                throw new InvalidDataException("JPEG 2000 zero-bit-plane tag tree exceeds the codestream precision bound.");
            }
            state.ZeroBitPlanes = threshold - 1;
          } else if (reader.ReadBit() == 0) {
            continue;
          }

          var passes = _ReadNumCodingPasses(reader);
          while (reader.ReadBit() != 0) {
            ++state.Lblock;
            if (state.Lblock > 30)
              throw new InvalidDataException("JPEG 2000 Lblock grew beyond a representable packet contribution length.");
          }

          var lengthBits = state.Lblock + FloorLog2(passes);
          if (lengthBits > 31)
            throw new InvalidDataException($"JPEG 2000 packet contribution length uses {lengthBits} bits; this decoder indexes byte arrays with Int32.");

          var contributionLength = reader.ReadBits(lengthBits);
          contributions.Add(new(state, passes, contributionLength));
        }
    }

    reader.AlignToByte();
    var body = reader.Position;

    foreach (var contribution in contributions) {
      if (contribution.Length < 0 || body + contribution.Length > end)
        throw new InvalidDataException(
          $"JPEG 2000 code-block contribution states {contribution.Length} bytes with only {end - body} left in the tile-part.");

      contribution.State.Data.Write(data, body, contribution.Length);
      contribution.State.Passes += contribution.Passes;
      body += contribution.Length;
    }

    return body;
  }

  private static SubbandInfo[] _SubbandsForResolution(SubbandInfo[] subbands, int levels, int resolution) {
    var result = new List<SubbandInfo>(resolution == 0 ? 1 : 3);
    foreach (var subband in subbands) {
      if (resolution == 0) {
        if (subband.Type == 0)
          result.Add(subband);
        continue;
      }

      var level = levels - resolution + 1;
      if (subband.Type != 0 && subband.ResolutionLevel == level)
        result.Add(subband);
    }
    return result.ToArray();
  }

  private static int _ReadNumCodingPasses(BitReader reader) {
    if (reader.ReadBit() == 0)
      return 1;
    if (reader.ReadBit() == 0)
      return 2;

    var third = reader.ReadBit();
    var fourth = reader.ReadBit();
    if (third == 0)
      return fourth == 0 ? 3 : 4;
    if (fourth == 0)
      return 5;

    var extension = reader.ReadBits(5);
    if (extension < 31)
      return 6 + extension;

    return 37 + reader.ReadBits(7);
  }

  internal static int FloorLog2(int value) {
    if (value <= 0)
      throw new ArgumentOutOfRangeException(nameof(value));

    var bits = 0;
    while (value > 1) {
      value >>= 1;
      ++bits;
    }
    return bits;
  }
}
