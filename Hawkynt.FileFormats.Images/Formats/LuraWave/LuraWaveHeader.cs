using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.LuraWave;

/// <summary>A rectangle the encoder coded with extra precision (an <c>ER</c> chunk entry).</summary>
/// <param name="X">Left edge, in coded pixels.</param>
/// <param name="Y">Top edge, in coded pixels.</param>
/// <param name="Width">Width; the right edge is inclusive at <c>X + Width</c>.</param>
/// <param name="Height">Height; the bottom edge is inclusive at <c>Y + Height</c>.</param>
public readonly record struct LuraWaveRegion(int X, int Y, int Width, int Height);

/// <summary>Everything the chunked header in front of the coded data of a LuraWave file says.</summary>
public sealed record LuraWaveHeader {

  /// <summary>The two signature bytes, <c>WV</c>.</summary>
  public const ushort Signature = 0x5756;

  /// <summary>The only stream version the decoder accepts.</summary>
  public const byte SupportedVersion = 2;

  /// <summary>The quality value that means "everything that was coded".</summary>
  public const int FullQuality = 1024;

  /// <summary>72 dpi in 16.16 fixed point, the resolution a stream without a <c>DP</c> chunk has.</summary>
  public const int DefaultDpiFixed = 72 << 16;

  /// <summary>Largest number of interest regions the format can carry.</summary>
  public const int MaxRegions = 16;

  internal const ushort TagGeometry = 0x4745;    // "GE"
  internal const ushort TagCoding = 0x4342;      // "CB"
  internal const ushort TagQuality = 0x5155;     // "QU"
  internal const ushort TagStripes = 0x5354;     // "ST"
  internal const ushort TagData = 0x4454;        // "DT"
  internal const ushort TagRegions = 0x4552;     // "ER"
  internal const ushort TagResolution = 0x4450;  // "DP"
  internal const ushort TagMaxValue = 0x4D56;    // "MV"
  internal const ushort TagPassword = 0x5057;    // "PW"

  /// <summary>Visible image width.</summary>
  public int Width { get; init; }

  /// <summary>Visible image height.</summary>
  public int Height { get; init; }

  /// <summary>How the coded channels become pixels.</summary>
  public LuraWaveColourMode Mode { get; init; }

  /// <summary>The byte following the colour mode in the <c>GE</c> chunk; the decoder ignores it.</summary>
  public byte ModeFlags { get; init; }

  /// <summary>Width of the coded coefficient grid (the image padded up to a multiple of <c>2^Levels</c>).</summary>
  public int CodedWidth { get; init; }

  /// <summary>Height of one stripe of the coded grid; a single-stripe ("embedded") stream codes the whole padded height here.</summary>
  public int StripeHeight { get; init; }

  /// <summary>Number of wavelet decomposition levels.</summary>
  public int Levels { get; init; }

  /// <summary>The byte following the level count in the <c>CB</c> chunk; the decoder ignores it.</summary>
  public byte CodingFlags { get; init; }

  /// <summary>The bit plane at which bit-plane coding stops and the remaining bits are sent verbatim.</summary>
  public int LowestBitPlane { get; init; }

  /// <summary>Number of stripes the coded data is split into.</summary>
  public int StripeCount { get; init; }

  /// <summary>Encoder quality, 0..1024 (<see cref="FullQuality"/> = lossless intent).</summary>
  public int Quality { get; init; } = FullQuality;

  /// <summary>Byte length of every stripe's coded data, from the <c>ST</c> chunk (empty when absent).</summary>
  public int[] StripeSizes { get; init; } = [];

  /// <summary>Extra precision shift granted to <see cref="Regions"/>.</summary>
  public int RegionShift { get; init; }

  /// <summary>Interest regions (the <c>ER</c> chunk).</summary>
  public LuraWaveRegion[] Regions { get; init; } = [];

  /// <summary>Horizontal resolution as a 16.16 fixed-point dots-per-inch value (72 dpi when absent).</summary>
  public int HorizontalDpiFixed { get; init; } = DefaultDpiFixed;

  /// <summary>Vertical resolution as a 16.16 fixed-point dots-per-inch value (72 dpi when absent).</summary>
  public int VerticalDpiFixed { get; init; } = DefaultDpiFixed;

  /// <summary>Largest sample value for the wide modes (the <c>MV</c> chunk), or 0 when absent.</summary>
  public int MaxValue { get; init; }

  /// <summary>Check value of the access password (the <c>PW</c> chunk); 0 when no password is set.</summary>
  public int PasswordCheck { get; init; }

  /// <summary>The quality the stream decodes to without the password (<see cref="FullQuality"/> when no password is set).</summary>
  public int PublicQuality { get; init; } = FullQuality;

  /// <summary>Offset of the first coded word in the file.</summary>
  public int DataOffset { get; init; }

  /// <summary>Number of colour channels the mode codes.</summary>
  public int Channels => ChannelsOf(this.Mode);

  /// <summary>Whether the stream is a single stripe, which the vendor called "embedded" (its other layout being "baseline").</summary>
  public bool IsEmbedded => this.StripeCount == 1;

  /// <summary>Number of coded channels for a colour mode, or 0 for a value the decoder does not know.</summary>
  public static int ChannelsOf(LuraWaveColourMode mode) => mode switch {
    LuraWaveColourMode.Grey or LuraWaveColourMode.WideGrey or LuraWaveColourMode.WideSignedGrey => 1,
    LuraWaveColourMode.ColourDifference or LuraWaveColourMode.Rgb or LuraWaveColourMode.ReversibleColour
      or LuraWaveColourMode.Polynomial or LuraWaveColourMode.WideColourDifference or LuraWaveColourMode.WideReversibleColour => 3,
    LuraWaveColourMode.FourChannel => 4,
    _ => 0,
  };

  /// <summary>Whether the bytes start with the LuraWave signature and the version this reader knows.</summary>
  public static bool HasSignature(ReadOnlySpan<byte> data)
    => data.Length >= 4 && BinaryPrimitives.ReadUInt16BigEndian(data) == Signature && data[2] == SupportedVersion;

  /// <summary>Parses the header chunks up to and including <c>DT</c>, validating everything the decoder relies on.</summary>
  /// <exception cref="InvalidDataException">The bytes are not a LuraWave stream this decoder can handle.</exception>
  public static LuraWaveHeader Parse(ReadOnlySpan<byte> data) {
    if (data.Length < 4 || BinaryPrimitives.ReadUInt16BigEndian(data) != Signature)
      throw new InvalidDataException("Not a LuraWave stream: the 'WV' signature is missing.");
    if (data[2] != SupportedVersion)
      throw new InvalidDataException($"Unsupported LuraWave stream version {data[2]} (only version {SupportedVersion} is known).");

    var header = new LuraWaveHeader();
    var position = 4;
    while (true) {
      if (position + 4 > data.Length)
        throw new InvalidDataException("LuraWave header is truncated before its 'DT' chunk.");

      var tag = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
      var length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
      if (length < 4 || position + length > data.Length)
        throw new InvalidDataException($"LuraWave chunk '{TagName(tag)}' at offset {position} declares {length} bytes, which do not fit.");

      var body = data.Slice(position + 4, length - 4);
      position += length;
      switch (tag) {
        case TagGeometry:
          Require(body, 10, tag);
          header = header with {
            Width = ReadPositive32(body, tag),
            Height = ReadPositive32(body[4..], tag),
            Mode = (LuraWaveColourMode)body[8],
            ModeFlags = body[9],
          };
          break;

        case TagCoding: {
          var wide = body.Length > 9;
          Require(body, wide ? 13 : 9, tag);
          var cursor = wide ? 8 : 4;
          var lowest = (sbyte)body[cursor + 2];
          header = header with {
            CodedWidth = wide ? ReadPositive32(body, tag) : BinaryPrimitives.ReadUInt16BigEndian(body),
            StripeHeight = wide ? ReadPositive32(body[4..], tag) : BinaryPrimitives.ReadUInt16BigEndian(body[2..]),
            Levels = body[cursor],
            CodingFlags = body[cursor + 1],
            LowestBitPlane = lowest == -1 ? 2 : lowest,
            StripeCount = BinaryPrimitives.ReadUInt16BigEndian(body[(cursor + 3)..]),
          };
          break;
        }

        case TagQuality:
          Require(body, 2, tag);
          header = header with { Quality = BinaryPrimitives.ReadUInt16BigEndian(body) };
          break;

        case TagStripes: {
          var sizes = new int[body.Length / 4];
          for (var i = 0; i < sizes.Length; ++i)
            sizes[i] = BinaryPrimitives.ReadInt32BigEndian(body[(i * 4)..]);
          header = header with { StripeSizes = sizes };
          break;
        }

        case TagRegions: {
          Require(body, 2, tag);
          var count = body[0];
          if (count > MaxRegions)
            throw new InvalidDataException($"LuraWave 'ER' chunk lists {count} regions; at most {MaxRegions} are possible.");
          Require(body, 2 + count * 12, tag);
          var regions = new LuraWaveRegion[count];
          for (var i = 0; i < count; ++i) {
            var entry = body[(2 + i * 12)..];
            regions[i] = new(
              BinaryPrimitives.ReadInt32BigEndian(entry),
              BinaryPrimitives.ReadInt32BigEndian(entry[4..]),
              BinaryPrimitives.ReadUInt16BigEndian(entry[8..]),
              BinaryPrimitives.ReadUInt16BigEndian(entry[10..]));
          }
          header = header with { RegionShift = body[1], Regions = regions };
          break;
        }

        case TagResolution:
          Require(body, 8, tag);
          header = header with {
            HorizontalDpiFixed = BinaryPrimitives.ReadInt32BigEndian(body),
            VerticalDpiFixed = BinaryPrimitives.ReadInt32BigEndian(body[4..]),
          };
          break;

        case TagMaxValue:
          Require(body, 4, tag);
          header = header with { MaxValue = BinaryPrimitives.ReadInt32BigEndian(body) };
          break;

        case TagPassword:
          Require(body, 4, tag);
          header = header with {
            PasswordCheck = BinaryPrimitives.ReadUInt16BigEndian(body),
            PublicQuality = BinaryPrimitives.ReadUInt16BigEndian(body[2..]),
          };
          break;

        case TagData:
          header = header with { DataOffset = position };
          header.Validate();
          return header;
      }
    }
  }

  private void Validate() {
    if (this.Width <= 0 || this.Height <= 0)
      throw new InvalidDataException("LuraWave stream has no 'GE' chunk or zero dimensions.");
    if ((byte)this.Mode is 5 or 20 or 21)
      throw new InvalidDataException($"LuraWave colour mode {(byte)this.Mode} ({((byte)this.Mode == 5 ? "four 8-bit channels without a colour model" : (byte)this.Mode == 20 ? "3x16 bit LAB" : "4x16 bit YUVK")}) is not supported: LuraTech's encoder writes it, but neither of its decoders renders it as RGB, so there is no reference for what its pixels look like.");
    if (this.Channels == 0)
      throw new InvalidDataException($"Unknown LuraWave colour mode {(byte)this.Mode}.");
    if (this.CodedWidth <= 0 || this.StripeHeight <= 0 || this.StripeCount <= 0)
      throw new InvalidDataException("LuraWave stream has no 'CB' chunk or an empty coded grid.");
    if (this.Levels is < 2 or > 15)
      throw new InvalidDataException($"LuraWave stream declares {this.Levels} wavelet levels; this decoder handles 2 to 15.");
    if (this.LowestBitPlane < 0)
      throw new InvalidDataException($"LuraWave stream declares a negative lowest bit plane ({this.LowestBitPlane}).");
    var block = 1 << this.Levels;
    if (this.CodedWidth % block != 0 || this.StripeHeight % block != 0)
      throw new InvalidDataException($"LuraWave coded grid {this.CodedWidth}x{this.StripeHeight} is not a multiple of 2^{this.Levels}.");
    if (this.StripeCount > 1 && this.StripeHeight != block)
      throw new InvalidDataException($"LuraWave stream splits into stripes of {this.StripeHeight} rows with {this.Levels} levels; the vendor decoder addresses its stripe buffer as if every stripe were exactly 2^levels = {block} rows high, so no other stripe height decodes consistently.");
    if (this.CodedWidth < this.Width || (long)this.StripeHeight * this.StripeCount < this.Height)
      throw new InvalidDataException($"LuraWave coded grid {this.CodedWidth}x{(long)this.StripeHeight * this.StripeCount} does not cover the {this.Width}x{this.Height} image.");
    if ((long)this.CodedWidth * this.StripeHeight * this.Channels > MaxStripeSamples)
      throw new InvalidDataException($"LuraWave stripe of {this.CodedWidth}x{this.StripeHeight}x{this.Channels} samples is larger than this decoder accepts.");
    if ((long)this.CodedWidth * this.StripeHeight * this.StripeCount * this.Channels > MaxStripeSamples)
      throw new InvalidDataException($"LuraWave coded grid of {this.CodedWidth}x{(long)this.StripeHeight * this.StripeCount}x{this.Channels} samples is larger than this decoder accepts.");
    if ((long)this.Width * this.Height > MaxPixels)
      throw new InvalidDataException($"LuraWave image of {this.Width}x{this.Height} pixels is larger than this decoder accepts.");
    if (this.DataOffset % 4 != 0)
      throw new InvalidDataException("LuraWave coded data does not start on a 4-byte boundary.");
    if (this.Mode is LuraWaveColourMode.WideGrey or LuraWaveColourMode.WideColourDifference or LuraWaveColourMode.WideReversibleColour or LuraWaveColourMode.WideSignedGrey && this.MaxValue < 0)
      throw new InvalidDataException("LuraWave 'MV' chunk holds a negative maximum.");
  }

  /// <summary>
  /// Writes the chunked header in front of <paramref name="codedData"/>: <c>GE</c>, <c>CB</c>, <c>QU</c>, <c>ST</c>,
  /// then <c>DP</c>, <c>MV</c> and <c>ER</c> where they apply, and <c>DT</c> padded so the data starts on a 4-byte boundary.
  /// </summary>
  internal byte[] Serialize(byte[] codedData) {
    using var output = new MemoryStream();

    void Chunk(ushort tag, ReadOnlySpan<byte> body) {
      Span<byte> prefix = stackalloc byte[4];
      BinaryPrimitives.WriteUInt16BigEndian(prefix, tag);
      BinaryPrimitives.WriteUInt16BigEndian(prefix[2..], (ushort)(body.Length + 4));
      output.Write(prefix);
      output.Write(body);
    }

    output.Write([(byte)'W', (byte)'V', SupportedVersion, 0]);

    Span<byte> geometry = stackalloc byte[10];
    BinaryPrimitives.WriteInt32BigEndian(geometry, this.Width);
    BinaryPrimitives.WriteInt32BigEndian(geometry[4..], this.Height);
    geometry[8] = (byte)this.Mode;
    geometry[9] = this.ModeFlags;
    Chunk(TagGeometry, geometry);

    var narrow = this.CodedWidth <= ushort.MaxValue && this.StripeHeight <= ushort.MaxValue;
    Span<byte> coding = stackalloc byte[narrow ? 9 : 13];
    var cursor = 0;
    if (narrow) {
      BinaryPrimitives.WriteUInt16BigEndian(coding, (ushort)this.CodedWidth);
      BinaryPrimitives.WriteUInt16BigEndian(coding[2..], (ushort)this.StripeHeight);
      cursor = 4;
    } else {
      BinaryPrimitives.WriteInt32BigEndian(coding, this.CodedWidth);
      BinaryPrimitives.WriteInt32BigEndian(coding[4..], this.StripeHeight);
      cursor = 8;
    }

    coding[cursor] = (byte)this.Levels;
    coding[cursor + 1] = this.CodingFlags;
    coding[cursor + 2] = (byte)this.LowestBitPlane;
    BinaryPrimitives.WriteUInt16BigEndian(coding[(cursor + 3)..], (ushort)this.StripeCount);
    Chunk(TagCoding, coding);

    Span<byte> word = stackalloc byte[4];
    BinaryPrimitives.WriteUInt16BigEndian(word, (ushort)this.Quality);
    Chunk(TagQuality, word[..2]);

    int[] sizes = this.StripeSizes.Length > 0 ? this.StripeSizes : [codedData.Length];
    var stripes = new byte[sizes.Length * 4];
    for (var i = 0; i < sizes.Length; ++i)
      BinaryPrimitives.WriteInt32BigEndian(stripes.AsSpan(i * 4), sizes[i]);
    Chunk(TagStripes, stripes);

    if (this.HorizontalDpiFixed != DefaultDpiFixed || this.VerticalDpiFixed != DefaultDpiFixed) {
      Span<byte> resolution = stackalloc byte[8];
      BinaryPrimitives.WriteInt32BigEndian(resolution, this.HorizontalDpiFixed);
      BinaryPrimitives.WriteInt32BigEndian(resolution[4..], this.VerticalDpiFixed);
      Chunk(TagResolution, resolution);
    }

    if (this.MaxValue != 0) {
      BinaryPrimitives.WriteInt32BigEndian(word, this.MaxValue);
      Chunk(TagMaxValue, word);
    }

    if (this.Regions.Length > 0) {
      var regions = new byte[2 + this.Regions.Length * 12];
      regions[0] = (byte)this.Regions.Length;
      regions[1] = (byte)this.RegionShift;
      for (var i = 0; i < this.Regions.Length; ++i) {
        var entry = regions.AsSpan(2 + i * 12);
        BinaryPrimitives.WriteInt32BigEndian(entry, this.Regions[i].X);
        BinaryPrimitives.WriteInt32BigEndian(entry[4..], this.Regions[i].Y);
        BinaryPrimitives.WriteUInt16BigEndian(entry[8..], (ushort)this.Regions[i].Width);
        BinaryPrimitives.WriteUInt16BigEndian(entry[10..], (ushort)this.Regions[i].Height);
      }

      Chunk(TagRegions, regions);
    }

    var padding = (int)((4 - (output.Length + 4) % 4) % 4);
    Chunk(TagData, new byte[padding]);
    output.Write(codedData);
    return output.ToArray();
  }

  /// <summary>Upper bound on the coefficients of one stripe — keeps a forged header from forcing a huge allocation.</summary>
  internal const long MaxStripeSamples = 1L << 28;

  /// <summary>Upper bound on visible pixels.</summary>
  internal const long MaxPixels = 1L << 28;

  private static void Require(ReadOnlySpan<byte> body, int needed, ushort tag) {
    if (body.Length < needed)
      throw new InvalidDataException($"LuraWave chunk '{TagName(tag)}' holds {body.Length} bytes; at least {needed} are needed.");
  }

  private static int ReadPositive32(ReadOnlySpan<byte> body, ushort tag) {
    var value = BinaryPrimitives.ReadInt32BigEndian(body);
    if (value < 0)
      throw new InvalidDataException($"LuraWave chunk '{TagName(tag)}' holds a negative size.");
    return value;
  }

  internal static string TagName(ushort tag) => new([(char)(tag >> 8), (char)(tag & 0xFF)]);
}
