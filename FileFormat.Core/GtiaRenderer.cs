using System;

namespace FileFormat.Core;

/// <summary>
/// Draws Atari 8-bit scanlines the way the GTIA does: playfield and sprites resolved together,
/// pixel by pixel, against the priority register.
/// </summary>
/// <remarks>
/// Most Atari picture formats can be read as a layout, because most pictures were drawn within one
/// display mode. A few were not: their authors rewrote the chip's registers between scanlines, or
/// mid-scanline, to get colours and resolutions the mode alone does not offer. Those pictures are
/// not a layout at all — they are a program, and the only way to know what one shows is to run it
/// against the hardware's own rules.
/// <para/>
/// So this is a renderer rather than a decoder. A format drives it: it pokes registers, says what
/// ANTIC is fetching, and asks for a span of pixels. What comes back is what the television showed.
/// </remarks>
public abstract class GtiaRenderer {

  /// <summary>Sprites the chip tracks: four players and four missiles.</summary>
  public const int SpriteCount = 4;

  /// <summary>Colour registers: four for the players, four for the playfield, one for the background.</summary>
  public const int ColorRegisterCount = 9;

  /// <summary>Index of the background register.</summary>
  public const int BackgroundRegister = 8;

  private readonly byte[] _playerHpos = new byte[SpriteCount];
  private readonly byte[] _missileHpos = new byte[SpriteCount];
  private readonly byte[] _playerSize = new byte[SpriteCount];
  private readonly byte[] _missileSize = new byte[SpriteCount];
  private readonly byte[] _playerSizeCounter = new byte[SpriteCount];
  private readonly byte[] _missileSizeCounter = new byte[SpriteCount];
  private readonly byte[] _playerGraphics = new byte[SpriteCount];
  private readonly byte[] _playerShift = new byte[SpriteCount];
  private int _missileShift;

  /// <summary>The nine colour registers.</summary>
  public byte[] Colors { get; } = new byte[ColorRegisterCount];

  /// <summary>The priority register, which decides what wins where objects overlap.</summary>
  public int Priority { get; set; }

  /// <summary>The missiles' shape for the current scanline, two bits each.</summary>
  public int MissileGraphics { get; set; }

  /// <summary>Characters ANTIC fetches per scanline.</summary>
  public int PlayfieldColumns { get; set; }

  /// <summary>
  /// The playfield byte for a column, or a value of 256 or more to mark a character whose high bit
  /// is set — which changes what its topmost pixel value means.
  /// </summary>
  protected abstract int GetPlayfieldByte(int y, int column);

  /// <summary>
  /// The colour a lit high-resolution pixel takes, which by default is the background's hue with
  /// one register's luminance — the reason a Graphics 8 screen is two shades of one colour.
  /// </summary>
  protected virtual int GetHiresColor(int color) => (color & 240) | (this.Colors[5] & 14);

  /// <summary>Sets one player's width, which the chip counts in doublings rather than pixels.</summary>
  public void SetPlayerSize(int player, int size) {
    size &= 3;
    this._playerSize[player] = (byte)(size == 2 ? 1 : size + 1);
  }

  /// <summary>
  /// Sets one player's width as a count of doublings rather than through the register's encoding.
  /// </summary>
  /// <remarks>
  /// The register spends two bits on three widths, so one of its four values is a duplicate. A
  /// format that stores the width directly is storing the count, not the register.
  /// </remarks>
  public void SetPlayerWidth(int player, int width) => this._playerSize[player] = (byte)width;

  /// <summary>Sets one missile's width as a count of doublings.</summary>
  public void SetMissileWidth(int missile, int width) => this._missileSize[missile] = (byte)width;

  /// <summary>Sets all four missiles' widths from one register.</summary>
  public void SetMissileSizes(int value) {
    for (var i = 0; i < SpriteCount; ++i) {
      var size = (value >> (i << 1)) & 3;
      this._missileSize[i] = (byte)(size == 2 ? 1 : size + 1);
    }
  }

  /// <summary>Writes one of the chip's registers by its address within the chip.</summary>
  public void Poke(int address, int value) {
    switch (address) {
      case >= 0 and <= 3: this._playerHpos[address] = (byte)value; break;
      case >= 4 and <= 7: this._missileHpos[address - 4] = (byte)value; break;
      case >= 8 and <= 11: this.SetPlayerSize(address - 8, value); break;
      case 12: this.SetMissileSizes(value); break;
      case >= 13 and <= 16: this._playerGraphics[address - 13] = (byte)value; break;
      case 17: this.MissileGraphics = value; break;
      // The low bit of a colour register does not reach the screen.
      case >= 18 and <= 26: this.Colors[address - 18] = (byte)(value & 254); break;
      case 27: this.Priority = value; break;
    }
  }

  /// <summary>Sets a player's horizontal position.</summary>
  public void SetPlayerHpos(int player, int value) => this._playerHpos[player] = (byte)value;

  /// <summary>Sets a missile's horizontal position.</summary>
  public void SetMissileHpos(int missile, int value) => this._missileHpos[missile] = (byte)value;

  /// <summary>
  /// Loads the colour registers from a table with one entry per scanline, in the order Graph2Font
  /// and its relatives store them.
  /// </summary>
  /// <remarks>
  /// Those formats store the background first and the playfield before the players, which is the
  /// order a person would list them rather than the order the chip numbers them — except in the
  /// nine-colour GTIA mode, where the registers are used as one flat set and the two orders
  /// coincide.
  /// </remarks>
  public void SetTabulatedColors(ReadOnlySpan<byte> data, int offset, int stride, int count, int gtiaMode) {
    ReadOnlySpan<byte> order = [8, 4, 5, 6, 7, 0, 1, 2, 3];

    for (var i = 0; i < count; ++i) {
      var at = offset + i * stride;
      var value = at >= 0 && at < data.Length ? data[at] : 0;
      this.Colors[(gtiaMode & 192) == 128 ? i : order[i]] = (byte)(value & 254);
    }
  }

  /// <summary>Sets all four players' widths from one register.</summary>
  public void SetPlayerSizes(int value) {
    for (var i = 0; i < SpriteCount; ++i)
      this.SetPlayerSize(i, value >> (i << 1));
  }

  /// <summary>Sets one player's shape for the current scanline.</summary>
  public void SetPlayerGraphics(int player, int value) => this._playerGraphics[player] = (byte)value;

  /// <summary>A player's width in doublings.</summary>
  public int PlayerSize(int player) => this._playerSize[player];

  /// <summary>A missile's width in doublings.</summary>
  public int MissileSize(int missile) => this._missileSize[missile];

  /// <summary>Loads the four players' shapes for a scanline, as the chip's own fetch would.</summary>
  public void ProcessPlayerDma(ReadOnlySpan<byte> data, int offset) {
    for (var i = 0; i < SpriteCount; ++i) {
      var at = offset + (i << 8);
      this._playerGraphics[i] = at >= 0 && at < data.Length ? data[at] : (byte)0;
    }
  }

  /// <summary>Loads the missiles' and then the players' shapes for a scanline.</summary>
  public void ProcessSpriteDma(ReadOnlySpan<byte> data, int offset) {
    this.MissileGraphics = offset >= 0 && offset < data.Length ? data[offset] : 0;
    this.ProcessPlayerDma(data, offset + 256);
  }

  /// <summary>
  /// Prepares for a scanline by clearing the shift registers and running the sprites through the
  /// positions before the picture starts.
  /// </summary>
  /// <remarks>
  /// The run-up matters: a sprite positioned left of the visible area is already part-way through
  /// its shape by the time the picture begins, and starting it fresh at the first visible column
  /// would show the wrong part of it.
  /// </remarks>
  public void StartLine(int startHpos) {
    Array.Clear(this._playerShift);
    this._missileShift = 0;

    for (var hpos = startHpos - 31; hpos < startHpos; ++hpos)
      this._Advance(hpos, 0);
  }

  /// <summary>
  /// Draws one span of a scanline and returns where it stopped.
  /// </summary>
  /// <param name="anticMode">What ANTIC is fetching, which decides how the playfield byte is read.</param>
  /// <param name="frame">Receives one GTIA colour byte per pixel.</param>
  public int DrawSpan(
    int y, int hpos, int untilHpos, AnticMode anticMode, Span<byte> frame, int width, int yOffset) {
    var gtiaMode = this.Priority >> 6;

    for (; hpos < untilHpos; ++hpos) {
      var x = hpos;
      var objects = 0;
      var playfield = 0;

      // The ninth GTIA mode reads its pixels a position early and always claims an object.
      if (gtiaMode == 2) {
        --x;
        objects = 1;
      }

      if (anticMode != AnticMode.Blank) {
        var column = (x >> 2) + (this.PlayfieldColumns >> 1) - 32;
        if (column >= 0 && column < this.PlayfieldColumns) {
          playfield = this.GetPlayfieldByte(y, column);

          var inverse = playfield >= 256;
          if (inverse && anticMode == AnticMode.HiRes)
            playfield = 511 - playfield;

          if (gtiaMode == 0) {
            playfield = (playfield >> ((~x & 3) << 1)) & 3;
            objects = anticMode == AnticMode.HiRes
              ? 64
              : anticMode == AnticMode.FiveColor && playfield == 3 && inverse ? 128 : (8 << playfield) & 112;
          } else {
            if ((x & 2) == 0)
              playfield >>= 4;

            playfield &= 15;
            if (gtiaMode == 2) {
              ReadOnlySpan<byte> objectsFor = [1, 2, 4, 8, 16, 32, 64, 128, 0, 0, 0, 0, 16, 32, 64, 128];
              objects = objectsFor[playfield];
            }
          }
        }
      }

      objects = this._Advance(hpos, objects);
      var color = this._Resolve(objects);
      var offset = (yOffset + y) * width + ((hpos + (width >> 2) - 128) << 1);

      switch (gtiaMode) {
        case 0:
          if (anticMode != AnticMode.HiRes)
            break;

          // A high-resolution pixel does not get its own colour, only its own luminance.
          frame[offset] = (byte)((playfield & 2) == 0 ? color : this.GetHiresColor(color));
          frame[offset + 1] = (byte)((playfield & 1) == 0 ? color : this.GetHiresColor(color));
          continue;

        case 2:
          break;

        default:
          if ((objects & 15) != 0)
            break;

          if (gtiaMode == 1)
            color |= playfield;
          else if (playfield == 0)
            color &= 240;
          else
            color |= playfield << 4;

          break;
      }

      frame[offset + 1] = frame[offset] = (byte)color;
    }

    return hpos;
  }

  /// <summary>
  /// Advances the display to where the processor will have finished a given number of cycles.
  /// </summary>
  /// <remarks>
  /// This is what makes a mid-scanline register change land where it does: the processor and the
  /// display run together, and cycles ANTIC steals to fetch the playfield are not available to the
  /// program. A picture built on rewriting registers mid-line was timed against exactly this.
  /// </remarks>
  public int AdvanceCpuCycles(int hpos, int cpuCycles, bool nonBlank) {
    for (;;) {
      hpos += 2;
      var x = (hpos - 118) >> 1;

      var stolen = (x & 1) != 0
        ? nonBlank && x >= -this.PlayfieldColumns && x < this.PlayfieldColumns
        : x >= -36 && x < 0 && (x & 2) != 0;

      if (!stolen && --cpuCycles == 0)
        return hpos;
    }
  }

  /// <summary>
  /// Clocks the sprites one position and returns which objects cover it.
  /// </summary>
  /// <remarks>
  /// Each sprite is a shift register clocked at its own width, so a doubled sprite advances half as
  /// often — which is how one register of eight bits becomes a shape sixteen or thirty-two pixels
  /// wide without any extra storage.
  /// </remarks>
  private int _Advance(int hpos, int objects) {
    for (var i = 0; i < SpriteCount; ++i) {
      if (this._playerHpos[i] == hpos) {
        this._playerShift[i] |= this._playerGraphics[i];
        this._playerSizeCounter[i] = this._playerSize[i];
      }

      if (this._missileHpos[i] == hpos) {
        this._missileShift |= this.MissileGraphics & (3 << (i << 1));
        this._missileSizeCounter[i] = this._missileSize[i];
      }
    }

    // One priority bit turns the four missiles into a fifth playfield colour instead of objects.
    if ((this.Priority & 16) != 0 && (this._missileShift & 170) != 0)
      objects |= 128;

    for (var i = 0; i < SpriteCount; ++i) {
      if ((this._playerShift[i] & 128) != 0
          || ((this.Priority & 16) == 0 && (this._missileShift & (2 << (i << 1))) != 0))
        objects |= 1 << i;

      if (--this._playerSizeCounter[i] == 0) {
        this._playerShift[i] <<= 1;
        this._playerSizeCounter[i] = this._playerSize[i];
      }

      if (--this._missileSizeCounter[i] == 0) {
        var mask = 1 << (i << 1);
        this._missileShift = (this._missileShift & ~(mask * 3)) | ((this._missileShift & mask) << 1);
        this._missileSizeCounter[i] = this._missileSize[i];
      }
    }

    return objects;
  }

  /// <summary>
  /// Resolves which of the overlapping objects at a position decides its colour.
  /// </summary>
  /// <remarks>
  /// The priority register does not rank the objects; it chooses among four fixed rankings, and
  /// where two objects are equal under the chosen one neither wins and the pixel goes black. A
  /// further bit makes overlapping players combine their colour bits instead. Both behaviours were
  /// used deliberately by artists to reach colours the registers do not hold, so neither can be
  /// simplified away.
  /// </remarks>
  private int _Resolve(int objects) {
    if (objects == 0)
      return this.Colors[BackgroundRegister];

    var priority = this.Priority;
    var color = 0;

    if ((objects & 3) != 0) {
      if (((objects & 48) == 0 || (priority & 12) == 0) && ((objects & 192) == 0 || (priority & 4) == 0))
        if ((objects & 1) != 0) {
          color = this.Colors[0];
          if ((objects & 2) != 0 && (priority & 32) != 0)
            color |= this.Colors[1];
        } else
          color = this.Colors[1];
    } else if ((objects & 12) != 0) {
      if (((objects & 192) == 0 || (priority & 6) == 0) && ((objects & 48) == 0 || (priority & 1) != 0))
        if ((objects & 4) != 0) {
          color = this.Colors[2];
          if ((objects & 8) != 0 && (priority & 32) != 0)
            color |= this.Colors[3];
        } else
          color = this.Colors[3];
    }

    if ((objects & 192) != 0 && ((objects & 12) == 0 || (priority & 9) == 0) && ((objects & 3) == 0 || (priority & 4) != 0))
      return color | this.Colors[(objects & 128) != 0 ? 7 : 6];

    if ((objects & 48) != 0 && ((objects & 12) == 0 || (priority & 1) == 0) && ((objects & 3) == 0 || (priority & 3) == 0))
      return color | this.Colors[(objects & 16) != 0 ? 4 : 5];

    return color;
  }
}
