using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.MayaIff;

/// <summary>Reads Maya IFF files from bytes, streams, or file paths.</summary>
/// <remarks>
/// What a tile holds between its corners and its end used to be recorded here as unsettled, and the
/// reader took it as one plane per channel in the order the tag reads forwards. It is settled now,
/// from files written by a converter that is not this one:
/// <list type="bullet">
///   <item>the channels are named backwards — alpha, blue, green, red — for however many of them
///     the header's flags say there are, and the tag on the chunk does not decide that;</item>
///   <item>the corners and the rows both count from the bottom of the picture upwards, so a tile
///     stating rows 0 to 63 is the bottom of the picture and its first stored row is the last row
///     of all;</item>
///   <item>a tile whose data is exactly its own width times height times the channel count is
///     stored interleaved and uncompressed, and any other length is the run-length coding, applied
///     to each channel's plane separately one after another.</item>
/// </list>
/// Reading it the old way drew a picture out of one channel's values with its rows upside down,
/// which is why nothing here had ever agreed with another reader on a file it did not write itself.
/// </remarks>
public static class MayaIffReader {

  /// <summary>FOR4 magic bytes (46 4F 52 34).</summary>
  private static readonly byte[] _FOR4_MAGIC = "FOR4"u8.ToArray();

  /// <summary>CIMG form type bytes (43 49 4D 47).</summary>
  private static readonly byte[] _CIMG_TYPE = "CIMG"u8.ToArray();

  /// <summary>TBHD chunk tag.</summary>
  private static readonly byte[] _TBHD_TAG = "TBHD"u8.ToArray();

  /// <summary>RGBA chunk tag.</summary>
  private static readonly byte[] _RGBA_TAG = "RGBA"u8.ToArray();

  /// <summary>RGB  chunk tag (with trailing space).</summary>
  private static readonly byte[] _RGB_TAG = Encoding.ASCII.GetBytes("RGB ");

  /// <summary>Minimum file size: 12 (FOR4+size+CIMG) + 8 (TBHD tag+size) + 24 (TBHD data) = 44.</summary>
  private const int _MIN_FILE_SIZE = 12 + 8 + MayaIffTbhdHeader.StructSize;

  /// <summary>Bytes a tile spends stating its own corners.</summary>
  private const int _TILE_CORNERS = 8;

  public static MayaIffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Maya IFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MayaIffFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static MayaIffFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid Maya IFF file.");

    if (!data.Slice(0, 4).SequenceEqual(_FOR4_MAGIC))
      throw new InvalidDataException("Invalid Maya IFF magic: expected FOR4.");

    if (!data.Slice(8, 4).SequenceEqual(_CIMG_TYPE))
      throw new InvalidDataException("Invalid Maya IFF form type: expected CIMG.");

    var state = new _Walked();

    // The tiles live in a form of their own inside the outer one, so the walk descends into any
    // nested FOR4 rather than stepping over it.
    _Walk(data, 12, data.Length, state);

    if (!state.HeaderFound)
      throw new InvalidDataException("No TBHD chunk found in Maya IFF file.");

    if (state.Width <= 0 || state.Height <= 0)
      throw new InvalidDataException($"A Maya IFF picture states {state.Width}x{state.Height}.");

    // Zero says a byte a channel and one says two. Nothing here reads the sixteen-bit form, and a
    // reader that took it for the eight-bit one would find half a picture and stretch it.
    if (state.BytesPerChannel != 0)
      throw new InvalidDataException($"A Maya IFF picture states {(state.BytesPerChannel + 1) * 8} bits a channel, which is not read here.");

    var channels = state.Channels;
    if (channels is not (3 or 4))
      throw new InvalidDataException($"A Maya IFF picture states flags {state.Flags}, which name {channels} colour channels.");

    if (state.Tiles.Count < 1)
      throw new InvalidDataException("A Maya IFF picture carries no tiles.");

    var wanted = (long)state.Width * state.Height * channels;
    if (wanted > int.MaxValue)
      throw new InvalidDataException($"A Maya IFF picture of {state.Width}x{state.Height} is larger than can be held.");

    var pixelData = new byte[(int)wanted];

    // Each tile states its own corners, so it goes back where it came from rather than wherever the
    // reading happens to have reached. The corners count rows from the bottom of the picture, and
    // so does the tile's own data: the whole thing is one bottom-up coordinate system.
    foreach (var (left, lower, right, upper, payload) in state.Tiles) {
      var wide = right - left + 1;
      var high = upper - lower + 1;
      if (wide <= 0 || high <= 0 || right >= state.Width || upper >= state.Height)
        throw new InvalidDataException($"A Maya IFF tile states corners {left},{lower}..{right},{upper} in a picture of {state.Width}x{state.Height}.");

      var samples = wide * high * channels;
      var planes = payload.Length == samples
        ? _Deinterleave(payload, wide, high, channels)
        : _Unpack(payload, wide, high, channels);

      // Reversed channel order, and the first row stored is the lowest row of the tile.
      for (var c = 0; c < channels; ++c) {
        var plane = (channels - 1 - c) * wide * high;
        for (var y = 0; y < high; ++y) {
          var row = plane + y * wide;
          var destination = ((state.Height - 1 - lower - y) * state.Width + left) * channels + c;
          for (var x = 0; x < wide; ++x)
            pixelData[destination + x * channels] = planes[row + x];
        }
      }
    }

    return new MayaIffFile {
      Width = state.Width,
      Height = state.Height,
      HasAlpha = channels == 4,
      PixelData = pixelData,
    };
  }

  public static MayaIffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>An uncompressed tile, which is interleaved, taken apart into one plane per channel.</summary>
  private static byte[] _Deinterleave(byte[] payload, int wide, int high, int channels) {
    var pixels = wide * high;
    var planes = new byte[pixels * channels];
    for (var i = 0; i < pixels; ++i)
    for (var c = 0; c < channels; ++c)
      planes[c * pixels + i] = payload[i * channels + c];

    return planes;
  }

  /// <summary>
  /// A compressed tile: each channel's plane run-length coded, one plane after another, and the
  /// whole of the chunk accounted for.
  /// </summary>
  private static byte[] _Unpack(byte[] payload, int wide, int high, int channels) {
    var pixels = wide * high;
    var planes = new byte[pixels * channels];
    var at = 0;

    for (var plane = 0; plane < channels; ++plane) {
      var written = 0;
      var start = plane * pixels;
      while (written < pixels) {
        if (at >= payload.Length)
          throw new InvalidDataException($"A Maya IFF tile runs out {written} bytes into plane {plane} of {pixels}.");

        var control = payload[at++];
        if (control >= 128) {
          var count = control - 128 + 1;
          if (at >= payload.Length || written + count > pixels)
            throw new InvalidDataException($"A Maya IFF tile states a run of {count} with {pixels - written} left in the plane.");

          planes.AsSpan(start + written, count).Fill(payload[at++]);
          written += count;
        } else {
          var count = control + 1;
          if (at + count > payload.Length || written + count > pixels)
            throw new InvalidDataException($"A Maya IFF tile states {count} literal bytes with {pixels - written} left in the plane.");

          payload.AsSpan(at, count).CopyTo(planes.AsSpan(start + written));
          at += count;
          written += count;
        }
      }
    }

    // The tile has to end where its planes do. A chunk longer than what it codes is not being read
    // the way it was written, and drawing it anyway is how a wrong reader passes.
    if (at != payload.Length)
      throw new InvalidDataException($"A Maya IFF tile codes {channels} planes in {at} bytes and the chunk holds {payload.Length}.");

    return planes;
  }

  /// <summary>What the walk over one file collects.</summary>
  private sealed class _Walked {
    public int Width;
    public int Height;
    public uint Flags;
    public int BytesPerChannel;
    public bool HeaderFound;
    public readonly List<(int Left, int Top, int Right, int Bottom, byte[] Payload)> Tiles = [];

    /// <summary>How many planes a tile carries, which the header's flags say and the tag does not.</summary>
    public int Channels =>
      ((this.Flags & MayaIffTbhdHeader.RgbFlag) != 0 ? 3 : 0)
      + ((this.Flags & MayaIffTbhdHeader.AlphaFlag) != 0 ? 1 : 0);
  }

  /// <summary>Walks the chunks of one form, descending into any form nested inside it.</summary>
  private static void _Walk(ReadOnlySpan<byte> data, int offset, int end, _Walked state) {
    while (offset + 8 <= end) {
      var tag = data.Slice(offset, 4);
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
      var body = offset + 8;
      if (size < 0 || body + size > end)
        return;

      if (tag.SequenceEqual(_FOR4_MAGIC)) {
        // A nested form names its own type in the first four bytes of its body.
        _Walk(data, body + 4, body + size, state);
      } else if (tag.SequenceEqual(_TBHD_TAG) && size >= MayaIffTbhdHeader.StructSize) {
        var tbhd = MayaIffTbhdHeader.ReadFrom(data.Slice(body, MayaIffTbhdHeader.StructSize));
        state.Width = (int)tbhd.Width;
        state.Height = (int)tbhd.Height;
        state.Flags = tbhd.Flags;
        state.BytesPerChannel = tbhd.Bytes;
        state.HeaderFound = true;
      } else if ((tag.SequenceEqual(_RGBA_TAG) || tag.SequenceEqual(_RGB_TAG)) && size >= _TILE_CORNERS) {
        var left = BinaryPrimitives.ReadUInt16BigEndian(data[body..]);
        var top = BinaryPrimitives.ReadUInt16BigEndian(data[(body + 2)..]);
        var right = BinaryPrimitives.ReadUInt16BigEndian(data[(body + 4)..]);
        var bottom = BinaryPrimitives.ReadUInt16BigEndian(data[(body + 6)..]);
        state.Tiles.Add((left, top, right, bottom, data.Slice(body + _TILE_CORNERS, size - _TILE_CORNERS).ToArray()));
      }

      offset = body + size + (size & 1);
    }
  }
}
