using System;
using System.IO;

namespace FileFormat.AtariIce;

/// <summary>Reads Interlace Character Editor pictures from bytes, streams, or file paths.</summary>
/// <remarks>
/// Almost all of this is the table of thirty-three mode pairings. Each says how big the picture is,
/// which colour registers the header's bytes fill, and how each of the two fields is to be read —
/// and the order matters, because a register the first field uses may be overwritten before the
/// second field reads it. That is not a quirk of the file but of the machine: the two fields are
/// separate television frames, and the program reloaded the registers between them.
/// </remarks>
public static class AtariIceReader {

  /// <summary>GTIA colour registers the machine has: four for sprites, four playfield and one background.</summary>
  private const int _REGISTER_COUNT = 9;

  /// <summary>Index of the background register.</summary>
  private const int _BACKGROUND = 8;

  /// <summary>
  /// Which registers a version 2.0 GTIA 11 picture fills, in the order its header lists them.
  /// </summary>
  private static ReadOnlySpan<int> _Ice20ColorOrder => [0, 1, 2, 3, 5, 7, 8];

  public static AtariIceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariIceFile FromStream(Stream stream) {
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

  public static AtariIceFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= 1024)
      throw new InvalidDataException("Not an Interlace Character Editor picture: too short for a character set.");

    var state = new _State(data);
    state.Read(data[0]);

    return new() { Data = data.ToArray(), Width = state.Width, Height = state.Height, Fields = state.Fields };
  }

  /// <summary>
  /// The registers as the header fills them, together with the fields already settled.
  /// </summary>
  /// <remarks>
  /// A mutable helper rather than a chain of expressions because the order of assignment is part of
  /// the format: several modes overwrite a register between the two fields, and a few settle the
  /// second field before the first for that reason.
  /// </remarks>
  private ref struct _State {

    private readonly ReadOnlySpan<byte> _data;
    private readonly byte[] _registers = new byte[_REGISTER_COUNT];

    public _State(ReadOnlySpan<byte> data) {
      this._data = data;
      this.Fields = new IceField[2];
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int LeftSkip { get; private set; }
    public IceField[] Fields { get; }

    /// <summary>Whether the file is a character set rather than a screen with one.</summary>
    public bool IsFont { get; private set; }

    private byte _At(int offset) => offset >= 0 && offset < this._data.Length ? this._data[offset] : (byte)0;

    /// <summary>Sets one register, masking off the bit that never reaches the screen.</summary>
    public void Set(int register, int value) => this._registers[register] = (byte)(value & 254);

    /// <summary>Reads five bytes as the background and then PF0 to PF3.</summary>
    public void SetBakPf0123(int offset) {
      this.Set(_BACKGROUND, this._At(offset));
      for (var i = 0; i < 4; ++i)
        this.Set(4 + i, this._At(offset + 1 + i));
    }

    /// <summary>Reads PF0 to PF3 from every other byte, the ones between belonging to the other field.</summary>
    public void SetPf0123Even(int offset) {
      for (var i = 0; i < 4; ++i)
        this.Set(4 + i, this._At(offset + i * 2));
    }

    /// <summary>Reads PF2 and then PF1, which is the order this header writes them.</summary>
    public void SetPf21(int offset) {
      this.Set(6, this._At(offset));
      this.Set(5, this._At(offset + 1));
    }

    /// <summary>Reads PF0 to PF3 and then the background.</summary>
    public void SetPf0123Bak(int offset) {
      for (var i = 0; i < 5; ++i)
        this.Set(4 + i, this._At(offset + i));
    }

    /// <summary>Reads the three sprite registers, the four playfield ones and the background.</summary>
    public void SetPm123Pf0123Bak(int offset) {
      for (var i = 0; i < 8; ++i)
        this.Set(1 + i, this._At(offset + i));
    }

    /// <summary>Reads all nine registers, the first sprite one included.</summary>
    public void SetAll(int offset) {
      this.Set(0, this._At(offset));
      this.SetPm123Pf0123Bak(offset + 1);
    }

    public void SetSize(int width, int height) {
      this.Width = width;
      this.Height = height;
      this.LeftSkip = 0;
    }

    public void Skip(int leftSkip) => this.LeftSkip = leftSkip;

    /// <summary>Settles one field with the registers as they stand.</summary>
    public void Field(int index, int charactersOffset, int fontOffset, IceFrameMode mode)
      => this.Fields[index] = new() {
        CharactersOffset = charactersOffset,
        FontOffset = fontOffset,
        Mode = mode,
        Registers = this._registers[..],
        LeftSkip = this.LeftSkip,
      };

    /// <summary>Settles one version 2.0 field with the registers as they stand.</summary>
    public void Ice20Field(int index, bool second, int fontOffset, int gtiaMode)
      => this.Fields[index] = new() {
        FontOffset = fontOffset,
        Ice20Mode = gtiaMode,
        Ice20Second = second,
        Registers = this._registers[..],
        LeftSkip = this.LeftSkip,
      };

    /// <summary>Requires the file to be exactly this long, and sets the size a character set has.</summary>
    private void Font(int length, int width = 256, int height = 128) {
      if (this._data.Length != length)
        throw new InvalidDataException($"Mode {this._data[0]} is {length} bytes, not {this._data.Length}.");

      this.IsFont = true;
      this.SetSize(width, height);
    }

    /// <summary>
    /// Accepts either a character set or a screen with one, since the same pairing serves both.
    /// </summary>
    private void FontOrScreen(int fontLength, int screenLength) {
      if (this._data.Length == fontLength) {
        this.Font(fontLength);
        return;
      }

      if (this._data.Length != screenLength || this._data[0] != 1)
        throw new InvalidDataException($"Mode {this._data[0]} is neither {fontLength} nor {screenLength} bytes.");

      this.IsFont = false;
      this.SetSize(320, 192);
    }

    /// <summary>Where a field's characters come from: the fixed sheet, or the screen in the file.</summary>
    private int Characters(bool second, int screenOffset)
      => this.IsFont ? second ? IceRenderer.SecondFontSheet : IceRenderer.FirstFontSheet : screenOffset;

    public void Read(int mode) {
      switch (mode) {
        case 0:
          this.Font(2053);
          this.Set(5, this._At(1));
          this.Set(6, this._At(3));
          this.Field(0, IceRenderer.FirstFontSheet, 5, IceFrameMode.Gr0);
          this.Set(5, this._At(2));
          this.Set(6, this._At(4));
          this.Field(1, IceRenderer.SecondFontSheet, 1029, IceFrameMode.Gr0);
          break;

        case 1:
          this.FontOrScreen(2054, 18310);
          this.SetBakPf0123(1);
          this.Field(0, this.Characters(false, 16390), 6, IceFrameMode.Gr12);
          this.Field(1, this.Characters(true, 17350), 1030, IceFrameMode.Gr12);
          break;

        case 2:
          this.FontOrScreen(2058, 18314);
          this.Set(_BACKGROUND, this._At(1));
          this.SetPf0123Even(2);
          this.Field(0, this.Characters(false, 16394), 10, IceFrameMode.Gr12);
          this.SetPf0123Even(3);
          this.Field(1, this.Characters(true, 17354), 1034, IceFrameMode.Gr12);
          break;

        case 3:
          // The one pairing whose screen form is a different length from its font form's double.
          if (this._data.Length == 2055)
            this.Font(2055);
          else {
            if (this._data.Length != 17351 || this._data[0] != 3)
              throw new InvalidDataException($"Mode 3 is neither 2055 nor 17351 bytes.");

            this.IsFont = false;
            this.SetSize(320, 192);
          }

          this.SetPf21(1);
          this.Field(0, this.Characters(false, 16391), 7, IceFrameMode.Gr0);
          this.SetBakPf0123(2);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(1, this.Characters(true, 16391), 1031, IceFrameMode.Gr12);
          break;

        case 4:
          this.Font(2058);
          this.Skip(2);
          this.SetAll(1);
          this.Field(0, IceRenderer.FirstFontSheet, 10, IceFrameMode.Gr0Gtia10);
          this.Field(1, IceRenderer.SecondFontSheet, 1034, IceFrameMode.Gr0Gtia10);
          break;

        case 5: {
          // Two lengths, differing by whether the second field's header repeats the eighth colour.
          if (this._data.Length is not (2065 or 2066))
            throw new InvalidDataException($"Mode 5 is neither 2065 nor 2066 bytes.");

          this.IsFont = true;
          this.SetSize(256, 128);
          this.Skip(2);
          this.Set(0, this._At(1));
          for (var i = 0; i < 8; ++i)
            this.Set(1 + i, this._At(2 + i * 2));

          var short5 = this._data.Length == 2065;
          this.Field(0, IceRenderer.FirstFontSheet, short5 ? 17 : 18, IceFrameMode.Gr0Gtia10);
          for (var i = 0; i < (short5 ? 7 : 8); ++i)
            this.Set(1 + i, this._At(3 + i * 2));

          this.Field(1, IceRenderer.SecondFontSheet, short5 ? 1041 : 1042, IceFrameMode.Gr0Gtia10);
          break;
        }

        case 6: this._Paired(2051, IceFrameMode.Gr0Gtia9, IceFrameMode.Gr0Gtia9); break;
        case 7: this._Paired(2051, IceFrameMode.Gr0Gtia11, IceFrameMode.Gr0Gtia11); break;

        case 8:
          this.Font(2058);
          this.Skip(1);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(0, IceRenderer.FirstFontSheet, 10, IceFrameMode.Gr0Gtia9);
          this.SetAll(1);
          this.Field(1, IceRenderer.SecondFontSheet, 1034, IceFrameMode.Gr0Gtia10);
          break;

        case 9:
          this.Font(2058);
          this.Skip(1);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(0, IceRenderer.FirstFontSheet, 10, IceFrameMode.Gr0Gtia11);
          this.Set(0, 0);
          this.SetPm123Pf0123Bak(2);
          this.Field(1, IceRenderer.SecondFontSheet, 1034, IceFrameMode.Gr0Gtia10);
          break;

        case 10: this._Paired(2051, IceFrameMode.Gr0Gtia9, IceFrameMode.Gr0Gtia11); break;

        case 11:
          this.Font(2051);
          this.Set(6, 0);
          this.Set(5, this._At(2));
          this.Field(0, IceRenderer.FirstFontSheet, 3, IceFrameMode.Gr0);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(1, IceRenderer.SecondFontSheet, 1027, IceFrameMode.Gr0Gtia11);
          break;

        case 12:
          this.Font(2051);
          this.SetPf21(1);
          this.Field(0, IceRenderer.FirstFontSheet, 3, IceFrameMode.Gr0);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(1, IceRenderer.SecondFontSheet, 1027, IceFrameMode.Gr0Gtia9);
          break;

        case 13:
          this.Font(2059);
          this.SetPf21(1);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(0, IceRenderer.FirstFontSheet, 11, IceFrameMode.Gr0);
          this.Skip(2);
          this.Set(0, this._At(1));
          this.SetPm123Pf0123Bak(3);
          this.Field(1, IceRenderer.SecondFontSheet, 1035, IceFrameMode.Gr0Gtia10);
          break;

        case 14:
          this.Font(2054);
          this.SetBakPf0123(1);

          // The second field is settled first because the first blanks the background it uses.
          this.Field(1, IceRenderer.SecondFontSheet, 1030, IceFrameMode.Gr12Gtia11);
          this.Set(_BACKGROUND, 0);
          this.Field(0, IceRenderer.FirstFontSheet, 6, IceFrameMode.Gr12);
          break;

        case 15:
          this.Font(2054);
          this.SetBakPf0123(1);
          this.Field(0, IceRenderer.FirstFontSheet, 6, IceFrameMode.Gr12);
          this.Field(1, IceRenderer.SecondFontSheet, 1030, IceFrameMode.Gr12Gtia9);
          break;

        case 16:
          this.Font(2058);
          this.Skip(2);
          this.SetAll(1);
          this.Field(1, IceRenderer.SecondFontSheet, 1034, IceFrameMode.Gr12Gtia10);
          this.Skip(0);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(0, IceRenderer.FirstFontSheet, 10, IceFrameMode.Gr12);
          break;

        case 17:
          this.FontOrScreen(2054, 17350);
          this.SetBakPf0123(1);
          this.Field(1, this.Characters(true, 16390), 1030, IceFrameMode.Gr0Gtia11);
          this.Set(_BACKGROUND, 0);
          this.Field(0, this.Characters(false, 16390), 6, IceFrameMode.Gr12);
          break;

        case 18:
          this.FontOrScreen(2054, 17350);
          this.SetBakPf0123(1);
          this.Field(0, this.Characters(false, 16390), 6, IceFrameMode.Gr12);
          this.Field(1, this.Characters(true, 16390), 1030, IceFrameMode.Gr0Gtia9);
          break;

        case 19:
          this.FontOrScreen(2058, 17354);
          this.SetPf0123Bak(5);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(0, this.Characters(false, 16394), 10, IceFrameMode.Gr12);
          this.Skip(2);
          this.SetAll(1);
          this.Field(1, this.Characters(true, 16394), 1034, IceFrameMode.Gr0Gtia10);
          break;

        case 22:
          this.Font(2058, 256, 256);
          this.Skip(2);
          this.SetAll(1);
          this.Field(0, IceRenderer.FirstFontSheet, 10, IceFrameMode.Gr13Gtia10);
          this.Field(1, IceRenderer.SecondFontSheet, 1034, IceFrameMode.Gr13Gtia10);
          break;

        case 23:
          this.Font(2065, 256, 256);
          this.Skip(2);
          this.Set(0, this._At(1));
          for (var i = 0; i < 8; ++i)
            this.Set(1 + i, this._At(2 + i * 2));

          this.Field(0, IceRenderer.FirstFontSheet, 17, IceFrameMode.Gr13Gtia10);
          for (var i = 0; i < 7; ++i)
            this.Set(1 + i, this._At(3 + i * 2));

          this.Field(1, IceRenderer.SecondFontSheet, 1041, IceFrameMode.Gr13Gtia10);
          break;

        case 24: this._Paired(2051, IceFrameMode.Gr13Gtia9, IceFrameMode.Gr13Gtia9, 256); break;
        case 25: this._Paired(2051, IceFrameMode.Gr13Gtia11, IceFrameMode.Gr13Gtia11, 256); break;

        case 26:
          this.Font(2058, 256, 256);
          this.Skip(1);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(0, IceRenderer.FirstFontSheet, 10, IceFrameMode.Gr13Gtia9);
          this.SetAll(1);
          this.Field(1, IceRenderer.SecondFontSheet, 1034, IceFrameMode.Gr13Gtia10);
          break;

        case 27:
          this.Font(2058, 256, 256);
          this.Skip(1);
          this.Set(_BACKGROUND, this._At(1));
          this.Field(0, IceRenderer.FirstFontSheet, 10, IceFrameMode.Gr13Gtia11);
          this.Set(0, 0);
          this.SetPm123Pf0123Bak(2);
          this.Field(1, IceRenderer.SecondFontSheet, 1034, IceFrameMode.Gr13Gtia10);
          break;

        case 28: this._Paired(2051, IceFrameMode.Gr13Gtia9, IceFrameMode.Gr13Gtia11, 256); break;

        case 31:
          this.Font(1032, 256, 288);
          this.Skip(2);
          for (var i = 0; i < 7; ++i)
            this.Set(_Ice20ColorOrder[i], this._At(1 + i));

          this.Ice20Field(0, false, 8, 10);
          this.Ice20Field(1, true, 520, 10);
          break;

        case 32:
          this.Font(1038, 256, 288);
          this.Skip(2);
          this.Set(0, this._At(1));
          for (var i = 1; i < 7; ++i)
            this.Set(_Ice20ColorOrder[i], this._At(i * 2));

          this.Ice20Field(0, false, 14, 10);
          for (var i = 1; i < 7; ++i)
            this.Set(_Ice20ColorOrder[i], this._At(1 + i * 2));

          this.Ice20Field(1, true, 526, 10);
          break;

        case 33: this._Ice20Paired(1027, 9, 9); break;
        case 34: this._Ice20Paired(1027, 11, 11); break;

        case 35:
          this.Font(1032, 256, 288);
          this.Skip(1);
          this.Set(_BACKGROUND, this._At(1));
          this.Ice20Field(0, false, 8, 9);
          for (var i = 0; i < 7; ++i)
            this.Set(_Ice20ColorOrder[i], this._At(1 + i));

          this.Ice20Field(1, true, 520, 10);
          break;

        case 36:
          this.Font(1032, 256, 288);
          this.Skip(1);
          this.Set(_BACKGROUND, this._At(1));
          this.Ice20Field(0, false, 8, 11);
          this.Set(0, 0);
          for (var i = 1; i < 7; ++i)
            this.Set(_Ice20ColorOrder[i], this._At(1 + i));

          this.Ice20Field(1, true, 520, 10);
          break;

        case 37: this._Ice20Paired(1027, 9, 11); break;

        default:
          throw new InvalidDataException($"Mode {mode} is not one the editor writes.");
      }
    }

    /// <summary>
    /// The commonest shape: a background byte for each field and nothing else, the two fields
    /// differing only in the GTIA mode they are read in.
    /// </summary>
    private void _Paired(int length, IceFrameMode first, IceFrameMode second, int height = 128) {
      this.Font(length, 256, height);
      this.Set(_BACKGROUND, this._At(1));
      this.Field(0, IceRenderer.FirstFontSheet, 3, first);
      this.Set(_BACKGROUND, this._At(2));
      this.Field(1, IceRenderer.SecondFontSheet, 1027, second);
    }

    /// <summary>The same shape among the version 2.0 pictures.</summary>
    private void _Ice20Paired(int length, int first, int second) {
      this.Font(length, 256, 288);
      this.Set(_BACKGROUND, this._At(1));
      this.Ice20Field(0, false, 3, first);
      this.Set(_BACKGROUND, this._At(2));
      this.Ice20Field(1, true, 515, second);
    }
  }

  public static AtariIceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
