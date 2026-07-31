using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PhotoChromePcs;

/// <summary>Reads PhotoChrome compressed pictures from bytes, streams, or file paths.</summary>
public static class PhotoChromePcsReader {

  /// <summary>Colours the palette area holds, which the STE test scans in one go.</summary>
  private const int _PALETTE_COLORS = 9616;

  public static PhotoChromePcsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PhotoChromePcsFile FromStream(Stream stream) {
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

  public static PhotoChromePcsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 18 || data[0] != 1 || data[1] != 64 || data[2] != 0 || data[3] != 200)
      throw new InvalidDataException("Not a PhotoChrome picture.");

    var stream = new _Stream(data, 6);
    var first = stream.UnpackField();

    var flags = data[4];
    if (flags == 0)
      return new() {
        Fields = [first],
        IsSte = AtariStGraphics.IsStePalette(first, PhotoChromePcsFile.BitmapSize, _PALETTE_COLORS),
      };

    var second = stream.UnpackField();

    // Each half of the second field is either a difference from the first or stored outright, and
    // the flag byte says which for each — a picture whose two fields share a bitmap but not a
    // palette pays for only one of them.
    if ((flags & 1) == 0) {
      for (var i = 0; i < PhotoChromePcsFile.BitmapSize; ++i)
        second[i] ^= first[i];
    }

    if ((flags & 2) == 0) {
      for (var i = PhotoChromePcsFile.BitmapSize; i < PhotoChromePcsFile.FieldSize; ++i)
        second[i] ^= first[i];
    }

    return new() {
      Fields = [first, second],
      IsSte = AtariStGraphics.IsStePalette(first, PhotoChromePcsFile.BitmapSize, _PALETTE_COLORS)
              || AtariStGraphics.IsStePalette(second, PhotoChromePcsFile.BitmapSize, _PALETTE_COLORS),
    };
  }

  /// <summary>
  /// The run-length stream both halves of a field are packed with, divided into blocks that each
  /// declare how many commands they contain.
  /// </summary>
  private ref struct _Stream {

    private readonly ReadOnlySpan<byte> _data;
    private int _at;
    private int _commands;
    private int _count;
    private int _value;
    private bool _words;

    public _Stream(ReadOnlySpan<byte> data, int at) {
      this._data = data;
      this._at = at;
      this._value = -1;
    }

    private int _Byte() => this._at < this._data.Length ? this._data[this._at++] : -1;

    private int _Word() {
      if (this._at + 1 >= this._data.Length)
        return -1;

      var value = (this._data[this._at] << 8) | this._data[this._at + 1];
      this._at += 2;

      return value;
    }

    /// <summary>Reads one value, which is a byte in the bitmap and a colour word in the palette.</summary>
    private int _Value() => this._words ? this._Word() : this._Byte();

    private void _StartBlock() {
      this._commands = this._Word();
      if (this._commands < 0)
        throw new InvalidDataException("A PhotoChrome block has no command count.");
    }

    /// <summary>
    /// Runs out whatever the block still has to say. The two halves are one stream, so a block that
    /// declared more commands than the picture needed has to be drained rather than abandoned.
    /// </summary>
    private void _EndBlock() {
      while (this._count > 0 || this._commands > 0)
        this._Read();
    }

    private int _Read() {
      while (this._count == 0) {
        if (this._commands <= 0)
          throw new InvalidDataException("A PhotoChrome block runs out of commands.");

        --this._commands;

        var command = this._Byte();
        if (command < 0)
          throw new InvalidDataException("A PhotoChrome stream ends inside a command.");

        if (command >= 128) {
          // A short run of literals, counted downwards from 256.
          this._count = 256 - command;
          this._value = -1;
          continue;
        }

        // Zero and one mean the count is too large for a byte and follows as a word; one also
        // means literals, which is how a long literal run is written.
        this._count = command is 0 or 1 ? this._Word() : command;
        if (this._count < 0)
          throw new InvalidDataException("A PhotoChrome run has no length.");

        this._value = command == 1 ? -1 : this._Value();
      }

      --this._count;

      return this._value >= 0 ? this._value : this._Value();
    }

    /// <summary>Unpacks one field: a block of bitmap and then a block of palette words.</summary>
    public byte[] UnpackField() {
      var field = new byte[PhotoChromePcsFile.FieldSize];

      this._words = false;
      this._StartBlock();
      for (var i = 0; i < PhotoChromePcsFile.BitmapSize; ++i) {
        var value = this._Read();
        if (value < 0)
          throw new InvalidDataException("A PhotoChrome bitmap ends early.");

        field[i] = (byte)value;
      }

      this._EndBlock();

      this._words = true;
      this._StartBlock();
      for (var i = PhotoChromePcsFile.BitmapSize; i < PhotoChromePcsFile.FieldSize; i += 2) {
        var value = this._Read();
        if (value < 0)
          throw new InvalidDataException("A PhotoChrome palette ends early.");

        field[i] = (byte)(value >> 8);
        field[i + 1] = (byte)value;
      }

      this._EndBlock();

      return field;
    }
  }

  public static PhotoChromePcsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
