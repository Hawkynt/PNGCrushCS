using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Neochrome;

/// <summary>The 128-byte header at the start of every NEOchrome file. All words are big-endian.</summary>
public readonly record struct NeochromeHeader {

  public const int StructSize = 128;

  public short Flag { get; init; }
  public short Resolution { get; init; }
  public short Pal0 { get; init; }
  public short Pal1 { get; init; }
  public short Pal2 { get; init; }
  public short Pal3 { get; init; }
  public short Pal4 { get; init; }
  public short Pal5 { get; init; }
  public short Pal6 { get; init; }
  public short Pal7 { get; init; }
  public short Pal8 { get; init; }
  public short Pal9 { get; init; }
  public short Pal10 { get; init; }
  public short Pal11 { get; init; }
  public short Pal12 { get; init; }
  public short Pal13 { get; init; }
  public short Pal14 { get; init; }
  public short Pal15 { get; init; }
  public byte[] FileName { get; init; }
  public short AnimationLimits { get; init; }
  public short AnimationSpeedDirection { get; init; }
  public short AnimSteps { get; init; }
  public short AnimXOffset { get; init; }
  public short AnimYOffset { get; init; }
  public short AnimWidth { get; init; }
  public short AnimHeight { get; init; }
  public short[] Reserved { get; init; }

  /// <summary>High byte of the raw animation speed/direction word, retained for API compatibility.</summary>
  public byte AnimSpeed => (byte)((ushort)this.AnimationSpeedDirection >> 8);

  /// <summary>Low byte of the raw animation speed/direction word, retained for API compatibility.</summary>
  public byte AnimDirection => (byte)this.AnimationSpeedDirection;

  public NeochromeHeader(
    short flag,
    short resolution,
    short pal0, short pal1, short pal2, short pal3,
    short pal4, short pal5, short pal6, short pal7,
    short pal8, short pal9, short pal10, short pal11,
    short pal12, short pal13, short pal14, short pal15,
    byte[] fileName,
    short animationLimits,
    short animationSpeedDirection,
    short animSteps,
    short animXOffset,
    short animYOffset,
    short animWidth,
    short animHeight,
    short[] reserved
  ) {
    this.Flag = flag;
    this.Resolution = resolution;
    this.Pal0 = pal0;
    this.Pal1 = pal1;
    this.Pal2 = pal2;
    this.Pal3 = pal3;
    this.Pal4 = pal4;
    this.Pal5 = pal5;
    this.Pal6 = pal6;
    this.Pal7 = pal7;
    this.Pal8 = pal8;
    this.Pal9 = pal9;
    this.Pal10 = pal10;
    this.Pal11 = pal11;
    this.Pal12 = pal12;
    this.Pal13 = pal13;
    this.Pal14 = pal14;
    this.Pal15 = pal15;
    this.FileName = fileName;
    this.AnimationLimits = animationLimits;
    this.AnimationSpeedDirection = animationSpeedDirection;
    this.AnimSteps = animSteps;
    this.AnimXOffset = animXOffset;
    this.AnimYOffset = animYOffset;
    this.AnimWidth = animWidth;
    this.AnimHeight = animHeight;
    this.Reserved = reserved;
  }

  /// <summary>
  /// Legacy constructor retained for source compatibility. The two animation bytes are stored as the
  /// high/low bytes of the real word at offset 50; filename, limits, and reserved words default to zero.
  /// </summary>
  public NeochromeHeader(
    short flag,
    short resolution,
    short pal0, short pal1, short pal2, short pal3,
    short pal4, short pal5, short pal6, short pal7,
    short pal8, short pal9, short pal10, short pal11,
    short pal12, short pal13, short pal14, short pal15,
    byte animSpeed,
    byte animDirection,
    short animSteps,
    short animXOffset,
    short animYOffset,
    short animWidth,
    short animHeight
  ) : this(
    flag, resolution,
    pal0, pal1, pal2, pal3, pal4, pal5, pal6, pal7,
    pal8, pal9, pal10, pal11, pal12, pal13, pal14, pal15,
    new byte[12],
    0,
    unchecked((short)((animSpeed << 8) | animDirection)),
    animSteps, animXOffset, animYOffset, animWidth, animHeight,
    new short[33]
  ) { }

  /// <summary>Extracts the 16-entry palette from individual fields.</summary>
  public short[] GetPalette() => [
    this.Pal0, this.Pal1, this.Pal2, this.Pal3,
    this.Pal4, this.Pal5, this.Pal6, this.Pal7,
    this.Pal8, this.Pal9, this.Pal10, this.Pal11,
    this.Pal12, this.Pal13, this.Pal14, this.Pal15,
  ];

  public static NeochromeHeader ReadFrom(ReadOnlySpan<byte> data) {
    if (data.Length < StructSize)
      throw new ArgumentException($"NEOchrome header requires {StructSize} bytes.", nameof(data));

    var reserved = new short[33];
    for (var i = 0; i < reserved.Length; ++i)
      reserved[i] = _ReadWord(data, 62 + i * 2);

    return new NeochromeHeader(
      _ReadWord(data, 0), _ReadWord(data, 2),
      _ReadWord(data, 4), _ReadWord(data, 6), _ReadWord(data, 8), _ReadWord(data, 10),
      _ReadWord(data, 12), _ReadWord(data, 14), _ReadWord(data, 16), _ReadWord(data, 18),
      _ReadWord(data, 20), _ReadWord(data, 22), _ReadWord(data, 24), _ReadWord(data, 26),
      _ReadWord(data, 28), _ReadWord(data, 30), _ReadWord(data, 32), _ReadWord(data, 34),
      data.Slice(36, 12).ToArray(),
      _ReadWord(data, 48), _ReadWord(data, 50), _ReadWord(data, 52), _ReadWord(data, 54),
      _ReadWord(data, 56), _ReadWord(data, 58), _ReadWord(data, 60),
      reserved
    );
  }

  public void WriteTo(Span<byte> data) {
    if (data.Length < StructSize)
      throw new ArgumentException($"NEOchrome header requires {StructSize} bytes.", nameof(data));
    if (this.FileName is null || this.FileName.Length != 12)
      throw new InvalidOperationException("NEOchrome filename field must contain exactly 12 bytes.");
    if (this.Reserved is null || this.Reserved.Length != 33)
      throw new InvalidOperationException("NEOchrome reserved field must contain exactly 33 words.");

    data[..StructSize].Clear();
    _WriteWord(data, 0, this.Flag);
    _WriteWord(data, 2, this.Resolution);
    var palette = this.GetPalette();
    for (var i = 0; i < palette.Length; ++i)
      _WriteWord(data, 4 + i * 2, palette[i]);

    this.FileName.AsSpan().CopyTo(data.Slice(36, 12));
    _WriteWord(data, 48, this.AnimationLimits);
    _WriteWord(data, 50, this.AnimationSpeedDirection);
    _WriteWord(data, 52, this.AnimSteps);
    _WriteWord(data, 54, this.AnimXOffset);
    _WriteWord(data, 56, this.AnimYOffset);
    _WriteWord(data, 58, this.AnimWidth);
    _WriteWord(data, 60, this.AnimHeight);
    for (var i = 0; i < this.Reserved.Length; ++i)
      _WriteWord(data, 62 + i * 2, this.Reserved[i]);
  }

  public static HeaderFieldDescriptor[] GetFieldMap() => [
    new(nameof(Flag), 0, 2), new(nameof(Resolution), 2, 2),
    new(nameof(Pal0), 4, 2), new(nameof(Pal1), 6, 2), new(nameof(Pal2), 8, 2), new(nameof(Pal3), 10, 2),
    new(nameof(Pal4), 12, 2), new(nameof(Pal5), 14, 2), new(nameof(Pal6), 16, 2), new(nameof(Pal7), 18, 2),
    new(nameof(Pal8), 20, 2), new(nameof(Pal9), 22, 2), new(nameof(Pal10), 24, 2), new(nameof(Pal11), 26, 2),
    new(nameof(Pal12), 28, 2), new(nameof(Pal13), 30, 2), new(nameof(Pal14), 32, 2), new(nameof(Pal15), 34, 2),
    new(nameof(FileName), 36, 12),
    new(nameof(AnimationLimits), 48, 2), new(nameof(AnimationSpeedDirection), 50, 2), new(nameof(AnimSteps), 52, 2),
    new(nameof(AnimXOffset), 54, 2), new(nameof(AnimYOffset), 56, 2), new(nameof(AnimWidth), 58, 2), new(nameof(AnimHeight), 60, 2),
    new(nameof(Reserved), 62, 66),
  ];

  private static short _ReadWord(ReadOnlySpan<byte> data, int offset)
    => BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));

  private static void _WriteWord(Span<byte> data, int offset, short value)
    => BinaryPrimitives.WriteInt16BigEndian(data.Slice(offset, 2), value);
}
