using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FileFormat.PostScript;

/// <summary>What a PostScript object is, out of the type table in the PostScript Language Reference.</summary>
public enum PsType {

  /// <summary>The object <c>null</c> leaves behind, and what an empty slot of an array holds.</summary>
  Null,

  /// <summary>Boolean.</summary>
  Boolean,

  /// <summary>An integer, held exactly.</summary>
  Integer,

  /// <summary>A real.</summary>
  Real,

  /// <summary>A name, literal when quoted with a slash and executable when written bare.</summary>
  Name,

  /// <summary>A string, which is a shared and writable run of bytes.</summary>
  String,

  /// <summary>An array; executable, it is a procedure.</summary>
  Array,

  /// <summary>A dictionary.</summary>
  Dictionary,

  /// <summary>A built-in operator.</summary>
  Operator,

  /// <summary>The mark <c>[</c> and <c>mark</c> push.</summary>
  Mark,

  /// <summary>A file, which here is only ever the program's own source.</summary>
  File,

  /// <summary>What <c>save</c> hands back for <c>restore</c> to take.</summary>
  Save,

  /// <summary>A font, which this carries as an identity rather than as glyphs.</summary>
  Font
}

/// <summary>
/// One PostScript object: a type, whether it is executable, and either a number or a reference.
/// </summary>
/// <remarks>
/// PostScript objects are values with a type tag and an executable flag, and the composite ones —
/// strings, arrays, dictionaries — are references to shared storage rather than copies of it. That
/// is exactly a tagged struct holding either a number or a reference, so it is written as one: the
/// simple types cost no allocation, and two copies of a composite object share the thing they name,
/// which is what makes <c>put</c> visible through every other reference to the same array.
/// </remarks>
public readonly struct PsObject : IEquatable<PsObject> {

  private readonly double _number;
  private readonly object? _reference;

  /// <summary>Which of the language's types this is.</summary>
  public PsType Type { get; }

  /// <summary>Whether executing this object runs it rather than pushing it.</summary>
  public bool IsExecutable { get; }

  private PsObject(PsType type, bool executable, double number, object? reference) {
    this.Type = type;
    this.IsExecutable = executable;
    this._number = number;
    this._reference = reference;
  }

  /// <summary>The object <c>null</c> pushes.</summary>
  public static PsObject Null => new(PsType.Null, false, 0, null);

  /// <summary>The mark <c>[</c> and <c>mark</c> push.</summary>
  public static PsObject Mark => new(PsType.Mark, false, 0, null);

  /// <summary>A boolean.</summary>
  public static PsObject FromBoolean(bool value) => new(PsType.Boolean, false, value ? 1 : 0, null);

  /// <summary>An integer.</summary>
  public static PsObject FromInteger(long value) => new(PsType.Integer, false, value, null);

  /// <summary>A real.</summary>
  public static PsObject FromReal(double value) => new(PsType.Real, false, value, null);

  /// <summary>A literal name, as a slash writes one.</summary>
  public static PsObject FromName(string name) => new(PsType.Name, false, 0, name);

  /// <summary>An executable name, as a bare word in the program is.</summary>
  public static PsObject FromExecutableName(string name) => new(PsType.Name, true, 0, name);

  /// <summary>A string over the given bytes, which it shares rather than copies.</summary>
  public static PsObject FromString(PsString value) => new(PsType.String, false, 0, value);

  /// <summary>An array, literal.</summary>
  public static PsObject FromArray(PsArray value) => new(PsType.Array, false, 0, value);

  /// <summary>An array marked executable, which is what a procedure is.</summary>
  public static PsObject FromProcedure(PsArray value) => new(PsType.Array, true, 0, value);

  /// <summary>A dictionary.</summary>
  public static PsObject FromDictionary(PsDictionary value) => new(PsType.Dictionary, false, 0, value);

  /// <summary>A built-in operator.</summary>
  public static PsObject FromOperator(PsOperator value) => new(PsType.Operator, true, 0, value);

  /// <summary>A file.</summary>
  public static PsObject FromFile(PsFile value) => new(PsType.File, false, 0, value);

  /// <summary>A save object, which carries the depth the graphics stack was at.</summary>
  public static PsObject FromSave(int depth) => new(PsType.Save, false, depth, null);

  /// <summary>A font, which is a dictionary this only ever has to hand back again.</summary>
  public static PsObject FromFont(PsDictionary value) => new(PsType.Font, false, 0, value);

  /// <summary>The same object with its executable flag set as asked, which is what <c>cvx</c> and <c>cvlit</c> do.</summary>
  public PsObject WithExecutable(bool executable) => new(this.Type, executable, this._number, this._reference);

  /// <summary>Whether this is an integer or a real.</summary>
  public bool IsNumber => this.Type is PsType.Integer or PsType.Real;

  /// <summary>The value as a number, whichever of the two numeric types it is.</summary>
  public double Number => this._number;

  /// <summary>The value as a boolean.</summary>
  public bool Boolean => this._number != 0;

  /// <summary>The value as an integer.</summary>
  public long Integer => (long)this._number;

  /// <summary>The name, for a name object.</summary>
  public string Name => (string)this._reference!;

  /// <summary>The string, for a string object.</summary>
  public PsString String => (PsString)this._reference!;

  /// <summary>The array, for an array object.</summary>
  public PsArray Array => (PsArray)this._reference!;

  /// <summary>The dictionary, for a dictionary or a font.</summary>
  public PsDictionary Dictionary => (PsDictionary)this._reference!;

  /// <summary>The operator, for an operator object.</summary>
  public PsOperator Operator => (PsOperator)this._reference!;

  /// <summary>The file, for a file object.</summary>
  public PsFile File => (PsFile)this._reference!;

  /// <summary>The depth a save was taken at.</summary>
  public int SaveDepth => (int)this._number;

  /// <summary>
  /// Whether two objects are the same one, which for a composite means the same storage.
  /// </summary>
  /// <remarks>
  /// <c>eq</c> compares strings by their characters and everything else by identity, so this is
  /// identity and the string case is handled where <c>eq</c> is.
  /// </remarks>
  public bool Equals(PsObject other) {
    if (this.Type != other.Type)
      return false;

    return this._reference == null && other._reference == null
      ? this._number.Equals(other._number)
      : this.Type == PsType.Name
        ? (string?)this._reference == (string?)other._reference
        : ReferenceEquals(this._reference, other._reference);
  }

  public override bool Equals(object? obj) => obj is PsObject other && this.Equals(other);

  public override int GetHashCode() => this.Type switch {
    PsType.Name => HashCode.Combine(this.Type, (string)this._reference!),
    PsType.Integer or PsType.Real or PsType.Boolean or PsType.Save => HashCode.Combine(this.Type, this._number),
    PsType.Null or PsType.Mark => HashCode.Combine(this.Type),
    _ => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this._reference)
  };

  /// <summary>The name of the type, as <c>type</c> reports it.</summary>
  public string TypeName => this.Type switch {
    PsType.Null => "nulltype",
    PsType.Boolean => "booleantype",
    PsType.Integer => "integertype",
    PsType.Real => "realtype",
    PsType.Name => "nametype",
    PsType.String => "stringtype",
    PsType.Array => "arraytype",
    PsType.Dictionary => "dicttype",
    PsType.Operator => "operatortype",
    PsType.Mark => "marktype",
    PsType.File => "filetype",
    PsType.Save => "savetype",
    _ => "fonttype"
  };

  public override string ToString() => this.Type switch {
    PsType.Null => "null",
    PsType.Boolean => this.Boolean ? "true" : "false",
    PsType.Integer => this.Integer.ToString(CultureInfo.InvariantCulture),
    PsType.Real => this._number.ToString("R", CultureInfo.InvariantCulture),
    PsType.Name => this.IsExecutable ? this.Name : "/" + this.Name,
    PsType.String => "(" + this.String.AsText() + ")",
    PsType.Array => this.IsExecutable ? "{...}" : "[...]",
    PsType.Dictionary => "-dict-",
    PsType.Operator => "--" + this.Operator.Name + "--",
    PsType.Mark => "-mark-",
    PsType.File => "-file-",
    PsType.Save => "-save-",
    _ => "-font-"
  };
}

/// <summary>A PostScript string: a window onto bytes that other strings may share.</summary>
/// <remarks>
/// <c>getinterval</c> hands back a string that is part of another one and writing through either is
/// visible in both, so a string is an offset and a length into a shared array rather than an array
/// of its own.
/// </remarks>
public sealed class PsString {

  /// <summary>The bytes, which may be longer than this string and shared with others.</summary>
  public byte[] Bytes { get; }

  /// <summary>Where this string starts in them.</summary>
  public int Offset { get; }

  /// <summary>How many bytes long it is.</summary>
  public int Length { get; }

  /// <summary>Wraps a run of bytes.</summary>
  public PsString(byte[] bytes, int offset, int length) {
    this.Bytes = bytes;
    this.Offset = offset;
    this.Length = length;
  }

  /// <summary>A string of the given length, all zero, as <c>string</c> makes one.</summary>
  public PsString(int length) : this(new byte[length], 0, length) { }

  /// <summary>The bytes of some text, as a string.</summary>
  public static PsString Of(string text) {
    var bytes = Encoding.Latin1.GetBytes(text);
    return new(bytes, 0, bytes.Length);
  }

  /// <summary>One byte.</summary>
  public byte this[int index] {
    get => this.Bytes[this.Offset + index];
    set => this.Bytes[this.Offset + index] = value;
  }

  /// <summary>The bytes this string covers.</summary>
  public ReadOnlySpan<byte> Span => this.Bytes.AsSpan(this.Offset, this.Length);

  /// <summary>Part of this string, sharing the same bytes.</summary>
  public PsString Interval(int index, int count) => new(this.Bytes, this.Offset + index, count);

  /// <summary>The bytes as text, one byte to a character.</summary>
  public string AsText() => Encoding.Latin1.GetString(this.Span);
}

/// <summary>A PostScript array: a window onto objects that other arrays may share.</summary>
public sealed class PsArray {

  /// <summary>The objects, which may be more than this array covers.</summary>
  public PsObject[] Items { get; }

  /// <summary>Where this array starts in them.</summary>
  public int Offset { get; }

  /// <summary>How many objects long it is.</summary>
  public int Length { get; }

  /// <summary>Wraps a run of objects.</summary>
  public PsArray(PsObject[] items, int offset, int length) {
    this.Items = items;
    this.Offset = offset;
    this.Length = length;
  }

  /// <summary>An array of the given length, all null, as <c>array</c> makes one.</summary>
  public PsArray(int length) : this(_Filled(length), 0, length) { }

  /// <summary>An array holding exactly these objects.</summary>
  public PsArray(IReadOnlyList<PsObject> items) : this(_Copy(items), 0, items.Count) { }

  private static PsObject[] _Filled(int length) {
    var items = new PsObject[length];
    System.Array.Fill(items, PsObject.Null);
    return items;
  }

  private static PsObject[] _Copy(IReadOnlyList<PsObject> items) {
    var copy = new PsObject[items.Count];
    for (var i = 0; i < copy.Length; ++i)
      copy[i] = items[i];

    return copy;
  }

  /// <summary>One element.</summary>
  public PsObject this[int index] {
    get => this.Items[this.Offset + index];
    set => this.Items[this.Offset + index] = value;
  }

  /// <summary>Part of this array, sharing the same storage.</summary>
  public PsArray Interval(int index, int count) => new(this.Items, this.Offset + index, count);
}

/// <summary>A PostScript dictionary.</summary>
/// <remarks>
/// The capacity a file states is not enforced: a Level 2 dictionary grows, and a file that asked
/// for a hundred slots and used a hundred and one is not a file that is wrong about anything worth
/// refusing it over.
/// </remarks>
public sealed class PsDictionary {

  private readonly Dictionary<PsObject, PsObject> _entries;
  private readonly List<PsObject> _order = [];

  /// <summary>How many slots the file asked for.</summary>
  public int Capacity { get; }

  /// <summary>Whether the dictionary refuses to be written to.</summary>
  public bool IsReadOnly { get; set; }

  /// <summary>Builds a dictionary of the stated capacity.</summary>
  public PsDictionary(int capacity) {
    this.Capacity = capacity;
    this._entries = new(Math.Clamp(capacity, 1, 4096));
  }

  /// <summary>How many entries there are.</summary>
  public int Count => this._entries.Count;

  /// <summary>The keys, in the order they were first defined.</summary>
  public IReadOnlyList<PsObject> Keys => this._order;

  /// <summary>Looks a key up.</summary>
  public bool TryGet(PsObject key, out PsObject value) => this._entries.TryGetValue(key, out value);

  /// <summary>Looks a name up.</summary>
  public bool TryGet(string name, out PsObject value) => this._entries.TryGetValue(PsObject.FromName(name), out value);

  /// <summary>Defines a key.</summary>
  public void Put(PsObject key, PsObject value) {
    if (!this._entries.ContainsKey(key))
      this._order.Add(key);

    this._entries[key] = value;
  }

  /// <summary>Defines a name.</summary>
  public void Put(string name, PsObject value) => this.Put(PsObject.FromName(name), value);

  /// <summary>Removes a key, which <c>undef</c> does.</summary>
  public void Remove(PsObject key) {
    if (!this._entries.Remove(key))
      return;

    this._order.Remove(key);
  }

  /// <summary>Whether the key is there.</summary>
  public bool Contains(PsObject key) => this._entries.ContainsKey(key);
}

/// <summary>A built-in operator: its name and what it does.</summary>
/// <param name="Name">The name it is defined under, which is what an error message names.</param>
/// <param name="Action">What running it does to the interpreter.</param>
public sealed record PsOperator(string Name, Action<PostScriptInterpreter> Action);

/// <summary>
/// A file object, which for this interpreter is only ever the program being run or a filter over it.
/// </summary>
/// <remarks>
/// <c>currentfile</c> hands the program back to itself so that image data written straight into the
/// program text can be read out of it, and that is the whole reason a file object exists here. There
/// is no file system: a program that tries to open one is refused rather than let near the disk.
/// </remarks>
public sealed class PsFile {

  private readonly byte[] _data;
  private readonly Func<int>? _decode;
  private int _peeked = -1;

  /// <summary>Where reading has got to, for a file that is a run of bytes.</summary>
  public int Position { get; set; }

  /// <summary>Where reading may not go past, for a file that is a run of bytes.</summary>
  public int End { get; }

  /// <summary>Whether the file has been closed.</summary>
  public bool IsClosed { get; set; }

  /// <summary>Opens a view onto a run of bytes.</summary>
  public PsFile(byte[] data, int position, int end) {
    this._data = data;
    this.Position = position;
    this.End = end;
  }

  /// <summary>
  /// Opens a file whose bytes are worked out one at a time.
  /// </summary>
  /// <param name="decode">Hands back the next byte, or -1 when there are no more.</param>
  /// <remarks>
  /// This is what a filter is. Decoding as the bytes are asked for rather than all at once is not an
  /// optimisation: a filter stops taking from the file underneath it the moment its reader stops
  /// asking, and several encodings — hexadecimal among them — have no end marker, so a filter that
  /// decoded eagerly would swallow the program that follows the data.
  /// </remarks>
  public PsFile(Func<int> decode) {
    this._data = [];
    this._decode = decode;
  }

  /// <summary>The bytes the file reads from, for a file that is a run of bytes.</summary>
  public byte[] Data => this._data;

  /// <summary>Whether the file works its bytes out rather than holding them.</summary>
  public bool IsDecoded => this._decode != null;

  /// <summary>
  /// How far reading has really got, which is one short of the position when a byte has been looked
  /// at and not taken.
  /// </summary>
  /// <remarks>
  /// The scanner looks one character ahead to find where a token ends, and a caller asking how much
  /// of a string a token used wants the answer without that lookahead in it. Otherwise the character
  /// that ended the token — a slash, a bracket — would be dropped from what is handed back.
  /// </remarks>
  public int ReadPosition => this.Position - (this._peeked >= 0 ? 1 : 0);

  /// <summary>Whether there is anything left.</summary>
  public bool AtEnd => this.PeekByte() < 0;

  /// <summary>The next byte, or -1 at the end.</summary>
  public int ReadByte() {
    if (this._peeked < 0)
      return this._Next();

    var value = this._peeked;
    this._peeked = -1;
    return value;
  }

  /// <summary>The next byte without taking it, or -1 at the end.</summary>
  public int PeekByte() => this._peeked >= 0 ? this._peeked : this._peeked = this._Next();

  /// <summary>Everything left in the file.</summary>
  public byte[] Drain() {
    var bytes = new List<byte>();
    for (var value = this.ReadByte(); value >= 0; value = this.ReadByte())
      bytes.Add((byte)value);

    return bytes.ToArray();
  }

  private int _Next() {
    if (this._decode != null)
      return this._decode();

    return this.Position < this.End ? this._data[this.Position++] : -1;
  }
}
