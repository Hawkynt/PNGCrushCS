using System;
using System.Collections.Generic;
using System.Globalization;

namespace FileFormat.PostScript;

/// <summary>The operators this interpreter knows, and the ones it refuses by name.</summary>
/// <remarks>
/// The set is the part of the language a drawing uses: the stack, arithmetic and comparison, the
/// dictionary and the array and the string, the control constructs, and the graphics. What is not
/// here is not passed over — a name that is not defined stops the program, which is the whole point.
/// <para/>
/// Text is the one deliberate gap, and it is a gap rather than an approximation on purpose. A
/// PostScript file names a font; the glyphs are in the font, not in the file, and this reader does
/// not carry a font library. Drawing a box where the words are would put geometry on the page the
/// file never stated, so the text operators consume their operands, advance nothing, and draw
/// nothing — the same decision the SVG, HP-GL and DXF readers here make, for the same reason.
/// </remarks>
public static class PostScriptOperators {

  /// <summary>Defines every operator in the given dictionary.</summary>
  public static void Install(PsDictionary system) {
    ArgumentNullException.ThrowIfNull(system);

    _Stack(system);
    _Arithmetic(system);
    _Relational(system);
    _Control(system);
    _Types(system);
    _Dictionaries(system);
    _Arrays(system);
    _Strings(system);
    _Files(system);
    _Miscellaneous(system);

    PostScriptGraphicsOperators.Install(system);
  }

  /// <summary>Defines one operator.</summary>
  internal static void Define(PsDictionary system, string name, Action<PostScriptInterpreter> action)
    => system.Put(name, PsObject.FromOperator(new(name, action)));

  /// <summary>
  /// Defines an operator that is accepted and changes nothing that can be seen.
  /// </summary>
  /// <param name="operands">How many operands it consumes.</param>
  /// <remarks>
  /// These are the ones that tune a marking engine: which dot pattern a grey is screened with, what
  /// the transfer curve is, how the page is fed. A surface with eight bits a channel and no screen
  /// has nothing for them to change, so honouring them exactly would come to the same picture. Each
  /// one is listed here rather than left undefined so that the list of what is ignored is a list
  /// rather than a silence.
  /// </remarks>
  internal static void Ignore(PsDictionary system, string name, int operands)
    => Define(system, name, interpreter => interpreter.Drop(operands));

  #region stack

  private static void _Stack(PsDictionary system) {
    Define(system, "pop", static i => i.Pop());
    Define(system, "exch", static i => {
      var b = i.Pop();
      var a = i.Pop();
      i.Push(b);
      i.Push(a);
    });

    Define(system, "dup", static i => i.Push(i.Peek()));
    Define(system, "clear", static i => i.ClearOperands());
    Define(system, "count", static i => i.Push(PsObject.FromInteger(i.Count)));
    Define(system, "mark", static i => i.Push(PsObject.Mark));
    Define(system, "cleartomark", static i => {
      while (i.Count > 0)
        if (i.Pop().Type == PsType.Mark)
          return;

      throw new PsErrorException("unmatchedmark", "A PostScript program cleared to a mark that was not on the stack.");
    });

    Define(system, "counttomark", static i => {
      for (var depth = 0; depth < i.Count; ++depth)
        if (i.Peek(depth).Type == PsType.Mark) {
          i.Push(PsObject.FromInteger(depth));
          return;
        }

      throw new PsErrorException("unmatchedmark", "A PostScript program counted to a mark that was not on the stack.");
    });

    Define(system, "index", static i => {
      var depth = i.PopInteger();
      if (depth < 0)
        throw new PsErrorException("rangecheck", $"A PostScript program indexed {depth} places into the stack.");

      i.Push(i.Peek((int)depth));
    });

    // copy is two operators wearing one name: a count copies that many of the topmost operands, and
    // a composite object copies its contents into another one.
    Define(system, "copy", static i => {
      var top = i.Peek();
      if (top.Type != PsType.Integer) {
        _CopyComposite(i);
        return;
      }

      var count = (int)i.PopInteger();
      if (count < 0 || count > i.Count)
        throw new PsErrorException("rangecheck", $"A PostScript program copied {count} of {i.Count} operands.");

      for (var index = 0; index < count; ++index)
        i.Push(i.Peek(count - 1));
    });

    Define(system, "roll", static i => {
      var shift = i.PopInteger();
      var count = (int)i.PopInteger();
      if (count < 0 || count > i.Count)
        throw new PsErrorException("rangecheck", $"A PostScript program rolled {count} of {i.Count} operands.");

      if (count == 0)
        return;

      var items = new PsObject[count];
      for (var index = count - 1; index >= 0; --index)
        items[index] = i.Pop();

      var offset = (int)(((-shift % count) + count) % count);
      for (var index = 0; index < count; ++index)
        i.Push(items[(offset + index) % count]);
    });
  }

  private static void _CopyComposite(PostScriptInterpreter i) {
    var target = i.Pop();
    var source = i.Pop();

    switch (source.Type) {
      case PsType.Array when target.Type == PsType.Array: {
        var from = source.Array;
        var to = target.Array;
        if (to.Length < from.Length)
          throw new PsErrorException("rangecheck", $"A PostScript program copied {from.Length} elements into room for {to.Length}.");

        for (var index = 0; index < from.Length; ++index)
          to[index] = from[index];

        i.Push(PsObject.FromArray(to.Interval(0, from.Length)).WithExecutable(target.IsExecutable));
        return;
      }

      case PsType.String when target.Type == PsType.String: {
        var from = source.String;
        var to = target.String;
        if (to.Length < from.Length)
          throw new PsErrorException("rangecheck", $"A PostScript program copied {from.Length} bytes into room for {to.Length}.");

        for (var index = 0; index < from.Length; ++index)
          to[index] = from[index];

        i.Push(PsObject.FromString(to.Interval(0, from.Length)));
        return;
      }

      case PsType.Dictionary when target.Type == PsType.Dictionary: {
        var from = source.Dictionary;
        var to = target.Dictionary;
        foreach (var key in from.Keys) {
          from.TryGet(key, out var value);
          to.Put(key, value);
        }

        i.Push(target);
        return;
      }

      default:
        throw new PsErrorException("typecheck", $"A PostScript program copied {source.TypeName} into {target.TypeName}.");
    }
  }

  #endregion

  #region arithmetic

  private static void _Arithmetic(PsDictionary system) {
    Define(system, "add", static i => _Binary(i, static (a, b) => a + b));
    Define(system, "sub", static i => _Binary(i, static (a, b) => a - b));
    Define(system, "mul", static i => _Binary(i, static (a, b) => a * b));

    Define(system, "div", static i => {
      var b = i.PopNumber();
      var a = i.PopNumber();
      if (b == 0)
        throw new PsErrorException("undefinedresult", "A PostScript program divided by zero.");

      i.Push(PsObject.FromReal(a / b));
    });

    Define(system, "idiv", static i => {
      var b = i.PopInteger();
      var a = i.PopInteger();
      if (b == 0)
        throw new PsErrorException("undefinedresult", "A PostScript program divided by zero.");

      i.Push(PsObject.FromInteger(a / b));
    });

    Define(system, "mod", static i => {
      var b = i.PopInteger();
      var a = i.PopInteger();
      if (b == 0)
        throw new PsErrorException("undefinedresult", "A PostScript program took a remainder modulo zero.");

      i.Push(PsObject.FromInteger(a % b));
    });

    Define(system, "neg", static i => _Unary(i, static a => -a));
    Define(system, "abs", static i => _Unary(i, Math.Abs));
    Define(system, "sqrt", static i => {
      var a = i.PopNumber();
      if (a < 0)
        throw new PsErrorException("rangecheck", $"A PostScript program took the square root of {a}.");

      i.Push(PsObject.FromReal(Math.Sqrt(a)));
    });

    Define(system, "ceiling", static i => _Unary(i, Math.Ceiling));
    Define(system, "floor", static i => _Unary(i, Math.Floor));
    Define(system, "round", static i => _Unary(i, static a => Math.Round(a, MidpointRounding.AwayFromZero)));
    Define(system, "truncate", static i => _Unary(i, Math.Truncate));

    Define(system, "atan", static i => {
      var den = i.PopNumber();
      var num = i.PopNumber();
      var degrees = Math.Atan2(num, den) * 180 / Math.PI;
      i.Push(PsObject.FromReal(degrees < 0 ? degrees + 360 : degrees));
    });

    Define(system, "sin", static i => i.Push(PsObject.FromReal(Math.Sin(i.PopNumber() * Math.PI / 180))));
    Define(system, "cos", static i => i.Push(PsObject.FromReal(Math.Cos(i.PopNumber() * Math.PI / 180))));

    Define(system, "exp", static i => {
      var exponent = i.PopNumber();
      var value = i.PopNumber();
      i.Push(PsObject.FromReal(Math.Pow(value, exponent)));
    });

    Define(system, "ln", static i => {
      var a = i.PopNumber();
      if (a <= 0)
        throw new PsErrorException("rangecheck", $"A PostScript program took the logarithm of {a}.");

      i.Push(PsObject.FromReal(Math.Log(a)));
    });

    Define(system, "log", static i => {
      var a = i.PopNumber();
      if (a <= 0)
        throw new PsErrorException("rangecheck", $"A PostScript program took the logarithm of {a}.");

      i.Push(PsObject.FromReal(Math.Log10(a)));
    });

    // The generator is fixed rather than seeded from the clock: a page that draws differently every
    // time it is opened cannot be compared with anything, including itself.
    var random = new Random(1);
    var seed = 1L;
    Define(system, "rand", i => i.Push(PsObject.FromInteger(random.Next())));
    Define(system, "srand", i => {
      seed = i.PopInteger();
      random = new((int)seed);
    });

    Define(system, "rrand", i => i.Push(PsObject.FromInteger(seed)));
  }

  private static void _Unary(PostScriptInterpreter i, Func<double, double> f) {
    var value = i.Pop();
    if (!value.IsNumber)
      throw new PsErrorException("typecheck", $"A PostScript arithmetic operator was given {value.TypeName}.");

    var result = f(value.Number);
    i.Push(value.Type == PsType.Integer && result == Math.Floor(result) && Math.Abs(result) < long.MaxValue
      ? PsObject.FromInteger((long)result)
      : PsObject.FromReal(result));
  }

  private static void _Binary(PostScriptInterpreter i, Func<double, double, double> f) {
    var b = i.Pop();
    var a = i.Pop();
    if (!a.IsNumber || !b.IsNumber)
      throw new PsErrorException("typecheck", $"A PostScript arithmetic operator was given {a.TypeName} and {b.TypeName}.");

    var result = f(a.Number, b.Number);

    // Two integers give an integer, which matters because array indices come out of this arithmetic
    // and an index has to be an integer to be used as one.
    i.Push(a.Type == PsType.Integer && b.Type == PsType.Integer && result == Math.Floor(result) && Math.Abs(result) < long.MaxValue
      ? PsObject.FromInteger((long)result)
      : PsObject.FromReal(result));
  }

  #endregion

  #region comparison and boolean

  private static void _Relational(PsDictionary system) {
    system.Put("true", PsObject.FromBoolean(true));
    system.Put("false", PsObject.FromBoolean(false));
    system.Put("null", PsObject.Null);

    Define(system, "eq", static i => i.Push(PsObject.FromBoolean(_Same(i.Pop(), i.Pop()))));
    Define(system, "ne", static i => i.Push(PsObject.FromBoolean(!_Same(i.Pop(), i.Pop()))));
    Define(system, "gt", static i => _Compare(i, static c => c > 0));
    Define(system, "ge", static i => _Compare(i, static c => c >= 0));
    Define(system, "lt", static i => _Compare(i, static c => c < 0));
    Define(system, "le", static i => _Compare(i, static c => c <= 0));

    Define(system, "and", static i => _Logical(i, static (a, b) => a & b, static (a, b) => a && b));
    Define(system, "or", static i => _Logical(i, static (a, b) => a | b, static (a, b) => a || b));
    Define(system, "xor", static i => _Logical(i, static (a, b) => a ^ b, static (a, b) => a ^ b));

    Define(system, "not", static i => {
      var value = i.Pop();
      i.Push(value.Type == PsType.Boolean ? PsObject.FromBoolean(!value.Boolean) : PsObject.FromInteger(~value.Integer));
    });

    Define(system, "bitshift", static i => {
      var shift = i.PopInteger();
      var value = i.PopInteger();
      i.Push(PsObject.FromInteger(shift >= 0 ? value << (int)Math.Min(shift, 63) : value >> (int)Math.Min(-shift, 63)));
    });
  }

  /// <summary>Whether <c>eq</c> calls two objects the same.</summary>
  /// <remarks>
  /// Numbers compare by value across the two numeric types, strings by their characters, and a name
  /// and a string of the same characters are equal to each other — all three stated in the reference
  /// — and everything else is the same object or it is not.
  /// </remarks>
  private static bool _Same(PsObject b, PsObject a) {
    if (a.IsNumber && b.IsNumber)
      return a.Number == b.Number;

    var aText = a.Type switch { PsType.String => a.String.AsText(), PsType.Name => a.Name, _ => null };
    var bText = b.Type switch { PsType.String => b.String.AsText(), PsType.Name => b.Name, _ => null };
    if (aText != null && bText != null)
      return aText == bText;

    return a.Equals(b);
  }

  private static void _Compare(PostScriptInterpreter i, Func<int, bool> accept) {
    var b = i.Pop();
    var a = i.Pop();

    if (a.IsNumber && b.IsNumber) {
      i.Push(PsObject.FromBoolean(accept(a.Number.CompareTo(b.Number))));
      return;
    }

    if (a.Type == PsType.String && b.Type == PsType.String) {
      i.Push(PsObject.FromBoolean(accept(string.CompareOrdinal(a.String.AsText(), b.String.AsText()))));
      return;
    }

    throw new PsErrorException("typecheck", $"A PostScript comparison was given {a} ({a.TypeName}) and {b} ({b.TypeName}).");
  }

  private static void _Logical(PostScriptInterpreter i, Func<long, long, long> bits, Func<bool, bool, bool> logic) {
    var b = i.Pop();
    var a = i.Pop();
    if (a.Type == PsType.Boolean && b.Type == PsType.Boolean) {
      i.Push(PsObject.FromBoolean(logic(a.Boolean, b.Boolean)));
      return;
    }

    if (a.Type == PsType.Integer && b.Type == PsType.Integer) {
      i.Push(PsObject.FromInteger(bits(a.Integer, b.Integer)));
      return;
    }

    throw new PsErrorException("typecheck", $"A PostScript logical operator was given {a.TypeName} and {b.TypeName}.");
  }

  #endregion

  #region control

  private static void _Control(PsDictionary system) {
    Define(system, "exec", static i => i.Invoke(i.Pop()));

    Define(system, "if", static i => {
      var body = i.PopProcedure();
      if (i.PopBoolean())
        i.PushFrame(new PsProcedureFrame(body));
    });

    Define(system, "ifelse", static i => {
      var otherwise = i.PopProcedure();
      var body = i.PopProcedure();
      i.PushFrame(new PsProcedureFrame(i.PopBoolean() ? body : otherwise));
    });

    Define(system, "for", static i => {
      var body = i.PopProcedure();
      var limit = i.Pop();
      var increment = i.Pop();
      var start = i.Pop();
      if (!limit.IsNumber || !increment.IsNumber || !start.IsNumber)
        throw new PsErrorException("typecheck", "A PostScript for loop was given something that is not a number.");

      if (increment.Number == 0)
        throw new PsErrorException("rangecheck", "A PostScript for loop steps by zero, which never ends.");

      var integer = start.Type == PsType.Integer && increment.Type == PsType.Integer;
      i.PushFrame(new PsForFrame(body, start.Number, increment.Number, limit.Number, integer));
    });

    Define(system, "repeat", static i => {
      var body = i.PopProcedure();
      var count = i.PopInteger();
      if (count < 0)
        throw new PsErrorException("rangecheck", $"A PostScript program repeated something {count} times.");

      i.PushFrame(new PsRepeatFrame(body, count));
    });

    Define(system, "loop", static i => i.PushFrame(new PsLoopForeverFrame(i.PopProcedure())));
    Define(system, "exit", static i => i.Exit());
    Define(system, "stop", static i => i.Stop());

    Define(system, "stopped", static i => {
      var body = i.Pop();
      i.PushFrame(new PsStoppedFrame());
      i.Invoke(body);
    });

    Define(system, "quit", static i => i.Quit());

    Define(system, "forall", static i => {
      var body = i.PopProcedure();
      var over = i.Pop();
      switch (over.Type) {
        case PsType.Array: {
          var items = new PsObject[over.Array.Length];
          for (var index = 0; index < items.Length; ++index)
            items[index] = over.Array[index];

          i.PushFrame(new PsForAllFrame(body, items, 1));
          return;
        }

        case PsType.String: {
          var items = new PsObject[over.String.Length];
          for (var index = 0; index < items.Length; ++index)
            items[index] = PsObject.FromInteger(over.String[index]);

          i.PushFrame(new PsForAllFrame(body, items, 1));
          return;
        }

        case PsType.Dictionary or PsType.Font: {
          var dictionary = over.Dictionary;
          var items = new List<PsObject>(dictionary.Count * 2);
          foreach (var key in dictionary.Keys) {
            dictionary.TryGet(key, out var value);
            items.Add(key);
            items.Add(value);
          }

          i.PushFrame(new PsForAllFrame(body, items, 2));
          return;
        }

        default:
          throw new PsErrorException("typecheck", $"A PostScript program walked over {over.TypeName}.");
      }
    });
  }

  #endregion

  #region types and attributes

  private static void _Types(PsDictionary system) {
    Define(system, "type", static i => i.Push(PsObject.FromExecutableName(i.Pop().TypeName)));
    Define(system, "cvlit", static i => i.Push(i.Pop().WithExecutable(false)));
    Define(system, "cvx", static i => i.Push(i.Pop().WithExecutable(true)));
    Define(system, "xcheck", static i => i.Push(PsObject.FromBoolean(i.Pop().IsExecutable)));

    // Access attributes: this holds no read-only storage that a program could be surprised by, so
    // the questions answer yes and the changes are accepted and change nothing.
    Define(system, "rcheck", static i => {
      i.Pop();
      i.Push(PsObject.FromBoolean(true));
    });

    Define(system, "wcheck", static i => {
      var value = i.Pop();
      i.Push(PsObject.FromBoolean(value.Type != PsType.Dictionary || !value.Dictionary.IsReadOnly));
    });

    Define(system, "readonly", static i => {
      var value = i.Peek();
      if (value.Type == PsType.Dictionary)
        value.Dictionary.IsReadOnly = true;
    });

    Define(system, "executeonly", static _ => { });
    Define(system, "noaccess", static _ => { });

    Define(system, "cvi", static i => {
      var value = i.Pop();
      if (value.Type == PsType.String) {
        i.Push(PsObject.FromInteger((long)_ParseNumber(value.String.AsText())));
        return;
      }

      if (!value.IsNumber)
        throw new PsErrorException("typecheck", $"A PostScript program converted {value.TypeName} to an integer.");

      i.Push(PsObject.FromInteger((long)value.Number));
    });

    Define(system, "cvr", static i => {
      var value = i.Pop();
      if (value.Type == PsType.String) {
        i.Push(PsObject.FromReal(_ParseNumber(value.String.AsText())));
        return;
      }

      if (!value.IsNumber)
        throw new PsErrorException("typecheck", $"A PostScript program converted {value.TypeName} to a real.");

      i.Push(PsObject.FromReal(value.Number));
    });

    Define(system, "cvn", static i => {
      var value = i.Pop();
      if (value.Type != PsType.String)
        throw new PsErrorException("typecheck", $"A PostScript program made a name out of {value.TypeName}.");

      i.Push(PsObject.FromName(value.String.AsText()).WithExecutable(value.IsExecutable));
    });

    Define(system, "cvs", static i => {
      var target = i.PopString();
      var value = i.Pop();
      var text = value.Type switch {
        PsType.String => value.String.AsText(),
        PsType.Name => value.Name,
        PsType.Integer => value.Integer.ToString(CultureInfo.InvariantCulture),
        PsType.Real => value.Number.ToString("G6", CultureInfo.InvariantCulture),
        PsType.Boolean => value.Boolean ? "true" : "false",
        PsType.Null => "null",
        _ => "--nostringval--"
      };

      if (text.Length > target.Length)
        throw new PsErrorException("rangecheck", $"A PostScript program wrote {text.Length} characters into a string of {target.Length}.");

      for (var index = 0; index < text.Length; ++index)
        target[index] = (byte)text[index];

      i.Push(PsObject.FromString(target.Interval(0, text.Length)));
    });
  }

  private static double _ParseNumber(string text) {
    if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
      return value;

    throw new PsErrorException("syntaxerror", $"A PostScript program read \"{text}\" as a number.");
  }

  #endregion

  #region dictionaries

  private static void _Dictionaries(PsDictionary system) {
    Define(system, "dict", static i => i.Push(PsObject.FromDictionary(new(i.PopCount("dictionary slots")))));
    Define(system, "maxlength", static i => i.Push(PsObject.FromInteger(i.PopDictionary().Capacity)));
    Define(system, "begin", static i => i.PushDictionary(i.PopDictionary()));
    Define(system, "end", static i => i.PopDictionaryStack());
    Define(system, "currentdict", static i => i.Push(PsObject.FromDictionary(i.CurrentDictionary)));
    Define(system, "countdictstack", static i => i.Push(PsObject.FromInteger(i.DictionaryDepth)));

    Define(system, "def", static i => {
      var value = i.Pop();
      var key = i.Pop();
      i.CurrentDictionary.Put(_Key(key), value);
    });

    Define(system, "store", static i => {
      var value = i.Pop();
      var key = _Key(i.Pop());
      (i.WhereIs(key) ?? i.CurrentDictionary).Put(key, value);
    });

    Define(system, "load", static i => {
      var key = _Key(i.Pop());
      if (!i.TryLookup(key, out var value))
        throw new PsErrorException("undefined", $"The PostScript name {key} is not defined.");

      i.Push(value);
    });

    Define(system, "where", static i => {
      var key = _Key(i.Pop());
      var dictionary = i.WhereIs(key);
      if (dictionary == null) {
        i.Push(PsObject.FromBoolean(false));
        return;
      }

      i.Push(PsObject.FromDictionary(dictionary));
      i.Push(PsObject.FromBoolean(true));
    });

    Define(system, "known", static i => {
      var key = _Key(i.Pop());
      i.Push(PsObject.FromBoolean(i.PopDictionary().Contains(key)));
    });

    Define(system, "undef", static i => {
      var key = _Key(i.Pop());
      i.PopDictionary().Remove(key);
    });

    Define(system, "dictstack", static i => {
      var target = i.PopArray();
      var count = Math.Min(target.Length, i.DictionaryDepth);
      for (var index = 0; index < count; ++index)
        target[index] = PsObject.FromDictionary(i.DictionaryAt(index));

      i.Push(PsObject.FromArray(target.Interval(0, count)));
    });

    // << and >> gather a dictionary from what is between them, which is exactly a mark, the pairs,
    // and then counting back to the mark.
    Define(system, "<<", static i => i.Push(PsObject.Mark));
    Define(system, ">>", static i => {
      var pairs = new List<PsObject>();
      for (;;) {
        if (i.Count == 0)
          throw new PsErrorException("unmatchedmark", "A PostScript dictionary was closed without being opened.");

        var value = i.Pop();
        if (value.Type == PsType.Mark)
          break;

        pairs.Add(value);
      }

      if (pairs.Count % 2 != 0)
        throw new PsErrorException("rangecheck", "A PostScript dictionary was written with a key that has no value.");

      var dictionary = new PsDictionary(pairs.Count / 2);
      for (var index = pairs.Count - 1; index > 0; index -= 2)
        dictionary.Put(_Key(pairs[index]), pairs[index - 1]);

      i.Push(PsObject.FromDictionary(dictionary));
    });
  }

  /// <summary>A dictionary key, which is a name however it was written.</summary>
  private static PsObject _Key(PsObject key) => key.Type == PsType.Name ? PsObject.FromName(key.Name) : key;

  #endregion

  #region arrays

  private static void _Arrays(PsDictionary system) {
    Define(system, "array", static i => i.Push(PsObject.FromArray(new(i.PopCount("array elements")))));
    Define(system, "[", static i => i.Push(PsObject.Mark));

    Define(system, "]", static i => {
      var items = new List<PsObject>();
      for (;;) {
        if (i.Count == 0)
          throw new PsErrorException("unmatchedmark", "A PostScript array was closed without being opened.");

        var value = i.Pop();
        if (value.Type == PsType.Mark)
          break;

        items.Add(value);
      }

      items.Reverse();
      i.Push(PsObject.FromArray(new(items)));
    });

    Define(system, "astore", static i => {
      var target = i.PopArray();
      for (var index = target.Length - 1; index >= 0; --index)
        target[index] = i.Pop();

      i.Push(PsObject.FromArray(target));
    });

    Define(system, "aload", static i => {
      var source = i.Pop();
      if (source.Type != PsType.Array)
        throw new PsErrorException("typecheck", $"A PostScript program loaded {source.TypeName} onto the stack.");

      for (var index = 0; index < source.Array.Length; ++index)
        i.Push(source.Array[index]);

      i.Push(source);
    });

    // A packed array is an array that cannot be written to. Nothing here writes to one it did not
    // make, so the two are the same object and the switch that chooses between them is remembered
    // only so the program can read it back.
    var packing = false;
    Define(system, "packedarray", i => {
      var count = i.PopCount("array elements");
      var items = new PsObject[count];
      for (var index = count - 1; index >= 0; --index)
        items[index] = i.Pop();

      i.Push(PsObject.FromArray(new(items, 0, count)));
    });

    Define(system, "setpacking", i => packing = i.PopBoolean());
    Define(system, "currentpacking", i => i.Push(PsObject.FromBoolean(packing)));
  }

  #endregion

  #region strings, and the operators shared with arrays and dictionaries

  private static void _Strings(PsDictionary system) {
    Define(system, "string", static i => i.Push(PsObject.FromString(new(i.PopCount("string bytes")))));

    Define(system, "length", static i => {
      var value = i.Pop();
      i.Push(PsObject.FromInteger(value.Type switch {
        PsType.String => value.String.Length,
        PsType.Array => value.Array.Length,
        PsType.Dictionary or PsType.Font => value.Dictionary.Count,
        PsType.Name => value.Name.Length,
        _ => throw new PsErrorException("typecheck", $"A PostScript program asked {value.TypeName} for its length.")
      }));
    });

    Define(system, "get", static i => {
      var key = i.Pop();
      var source = i.Pop();
      switch (source.Type) {
        case PsType.Array:
          i.Push(source.Array[_Index(key, source.Array.Length, "array")]);
          return;

        case PsType.String:
          i.Push(PsObject.FromInteger(source.String[_Index(key, source.String.Length, "string")]));
          return;

        case PsType.Dictionary or PsType.Font:
          if (!source.Dictionary.TryGet(_Key(key), out var value))
            throw new PsErrorException("undefined", $"A PostScript dictionary was asked for {key}, which is not in it.");

          i.Push(value);
          return;

        default:
          throw new PsErrorException("typecheck", $"A PostScript program indexed into {source.TypeName}.");
      }
    });

    Define(system, "put", static i => {
      var value = i.Pop();
      var key = i.Pop();
      var target = i.Pop();
      switch (target.Type) {
        case PsType.Array:
          target.Array[_Index(key, target.Array.Length, "array")] = value;
          return;

        case PsType.String:
          if (!value.IsNumber)
            throw new PsErrorException("typecheck", $"A PostScript program put {value.TypeName} into a string.");

          target.String[_Index(key, target.String.Length, "string")] = (byte)value.Integer;
          return;

        case PsType.Dictionary or PsType.Font:
          if (target.Dictionary.IsReadOnly)
            throw new PsErrorException("invalidaccess", "A PostScript program wrote into a dictionary that refuses to be written to.");

          target.Dictionary.Put(_Key(key), value);
          return;

        default:
          throw new PsErrorException("typecheck", $"A PostScript program wrote into {target.TypeName}.");
      }
    });

    Define(system, "getinterval", static i => {
      var count = (int)i.PopInteger();
      var from = (int)i.PopInteger();
      var source = i.Pop();
      switch (source.Type) {
        case PsType.Array:
          _CheckInterval(from, count, source.Array.Length, "array");
          i.Push(PsObject.FromArray(source.Array.Interval(from, count)).WithExecutable(source.IsExecutable));
          return;

        case PsType.String:
          _CheckInterval(from, count, source.String.Length, "string");
          i.Push(PsObject.FromString(source.String.Interval(from, count)));
          return;

        default:
          throw new PsErrorException("typecheck", $"A PostScript program took an interval of {source.TypeName}.");
      }
    });

    Define(system, "putinterval", static i => {
      var source = i.Pop();
      var from = (int)i.PopInteger();
      var target = i.Pop();

      if (target.Type == PsType.Array && source.Type == PsType.Array) {
        _CheckInterval(from, source.Array.Length, target.Array.Length, "array");
        for (var index = 0; index < source.Array.Length; ++index)
          target.Array[from + index] = source.Array[index];

        return;
      }

      if (target.Type == PsType.String && source.Type == PsType.String) {
        _CheckInterval(from, source.String.Length, target.String.Length, "string");
        for (var index = 0; index < source.String.Length; ++index)
          target.String[from + index] = source.String[index];

        return;
      }

      throw new PsErrorException("typecheck", $"A PostScript program wrote {source.TypeName} into {target.TypeName}.");
    });

    Define(system, "search", static i => {
      var pattern = i.PopString();
      var text = i.PopString();
      var at = _IndexOf(text, pattern, 0);
      if (at < 0) {
        i.Push(PsObject.FromString(text));
        i.Push(PsObject.FromBoolean(false));
        return;
      }

      i.Push(PsObject.FromString(text.Interval(at + pattern.Length, text.Length - at - pattern.Length)));
      i.Push(PsObject.FromString(text.Interval(at, pattern.Length)));
      i.Push(PsObject.FromString(text.Interval(0, at)));
      i.Push(PsObject.FromBoolean(true));
    });

    Define(system, "anchorsearch", static i => {
      var pattern = i.PopString();
      var text = i.PopString();
      if (pattern.Length > text.Length || _IndexOf(text, pattern, 0) != 0) {
        i.Push(PsObject.FromString(text));
        i.Push(PsObject.FromBoolean(false));
        return;
      }

      i.Push(PsObject.FromString(text.Interval(pattern.Length, text.Length - pattern.Length)));
      i.Push(PsObject.FromString(text.Interval(0, pattern.Length)));
      i.Push(PsObject.FromBoolean(true));
    });

    Define(system, "token", static i => {
      var source = i.Pop();
      var file = source.Type switch {
        PsType.String => new PsFile(source.String.Bytes, source.String.Offset, source.String.Offset + source.String.Length),
        PsType.File => source.File,
        _ => throw new PsErrorException("typecheck", $"A PostScript program read a token out of {source.TypeName}.")
      };

      var token = PostScriptScanner.Next(file);
      if (token == null) {
        i.Push(PsObject.FromBoolean(false));
        return;
      }

      if (source.Type == PsType.String) {
        var used = Math.Clamp(file.ReadPosition - source.String.Offset, 0, source.String.Length);
        i.Push(PsObject.FromString(source.String.Interval(used, source.String.Length - used)));
      }

      i.Push(token.Value);
      i.Push(PsObject.FromBoolean(true));
    });
  }

  private static int _IndexOf(PsString text, PsString pattern, int from) {
    if (pattern.Length == 0)
      return from <= text.Length ? from : -1;

    for (var start = from; start + pattern.Length <= text.Length; ++start) {
      var found = true;
      for (var index = 0; index < pattern.Length && found; ++index)
        found = text[start + index] == pattern[index];

      if (found)
        return start;
    }

    return -1;
  }

  private static int _Index(PsObject key, int length, string what) {
    if (!key.IsNumber)
      throw new PsErrorException("typecheck", $"A PostScript program indexed a {what} with {key.TypeName}.");

    var index = (long)key.Number;
    if (index < 0 || index >= length)
      throw new PsErrorException("rangecheck", $"A PostScript program indexed element {index} of a {what} of {length}.");

    return (int)index;
  }

  private static void _CheckInterval(int from, int count, int length, string what) {
    if (from < 0 || count < 0 || (long)from + count > length)
      throw new PsErrorException("rangecheck", $"A PostScript program took {count} from position {from} of a {what} of {length}.");
  }

  #endregion

  #region files

  private static void _Files(PsDictionary system) {
    Define(system, "currentfile", static i => i.Push(PsObject.FromFile(i.Source)));

    Define(system, "closefile", static i => {
      var value = i.Pop();
      if (value.Type == PsType.File)
        value.File.IsClosed = true;
    });

    Define(system, "read", static i => {
      var file = _File(i.Pop());
      var value = file.ReadByte();
      if (value < 0) {
        i.Push(PsObject.FromBoolean(false));
        return;
      }

      i.Push(PsObject.FromInteger(value));
      i.Push(PsObject.FromBoolean(true));
    });

    Define(system, "readstring", static i => {
      var target = i.PopString();
      var file = _File(i.Pop());

      var read = 0;
      while (read < target.Length) {
        var value = file.ReadByte();
        if (value < 0)
          break;

        target[read++] = (byte)value;
      }

      i.Push(PsObject.FromString(target.Interval(0, read)));
      i.Push(PsObject.FromBoolean(read == target.Length));
    });

    Define(system, "readhexstring", static i => {
      var target = i.PopString();
      var file = _File(i.Pop());

      var read = 0;
      var high = -1;
      while (read < target.Length) {
        var c = file.ReadByte();
        if (c < 0)
          break;

        var digit = c switch {
          >= '0' and <= '9' => c - '0',
          >= 'a' and <= 'f' => c - 'a' + 10,
          >= 'A' and <= 'F' => c - 'A' + 10,
          _ => -1
        };

        if (digit < 0) {
          // Whitespace between digits is what a program writing hexadecimal into its own text uses
          // to keep the lines short. Anything else means the data is not what it says it is.
          if (PostScriptScanner.IsWhitespace(c) || c == '>')
            continue;

          throw new PsErrorException("ioerror", $"The character '{(char)c}' inside hexadecimal image data in a PostScript program.");
        }

        if (high < 0) {
          high = digit;
          continue;
        }

        target[read++] = (byte)((high << 4) | digit);
        high = -1;
      }

      i.Push(PsObject.FromString(target.Interval(0, read)));
      i.Push(PsObject.FromBoolean(read == target.Length));
    });

    Define(system, "readline", static i => {
      var target = i.PopString();
      var file = _File(i.Pop());

      var read = 0;
      var any = false;
      for (;;) {
        var c = file.ReadByte();
        if (c < 0)
          break;

        any = true;
        if (c == '\n')
          break;

        if (c == '\r') {
          if (file.PeekByte() == '\n')
            file.ReadByte();

          break;
        }

        if (read >= target.Length)
          throw new PsErrorException("rangecheck", $"A PostScript program read a line longer than the {target.Length} bytes it made room for.");

        target[read++] = (byte)c;
      }

      i.Push(PsObject.FromString(target.Interval(0, read)));
      i.Push(PsObject.FromBoolean(any));
    });

    Define(system, "bytesavailable", static i => {
      var file = _File(i.Pop());

      // A filter works its bytes out as they are asked for and cannot say how many are left without
      // decoding them, which is what the reference means by a file whose length is not known.
      i.Push(PsObject.FromInteger(file.IsDecoded ? -1 : Math.Max(0, file.End - file.ReadPosition)));
    });

    Define(system, "status", static i => {
      var value = i.Pop();
      i.Push(PsObject.FromBoolean(value.Type == PsType.File && !value.File.IsClosed));
    });

    Define(system, "flushfile", static i => i.Pop());
    Define(system, "resetfile", static i => i.Pop());

    // A program writing to the standard output is talking to whoever ran it, not drawing, so what
    // it says is consumed. There is no file system here at all: a program that tries to open one is
    // refused rather than let near the disk.
    Define(system, "print", static i => i.Pop());
    Define(system, "=", static i => i.Pop());
    Define(system, "==", static i => i.Pop());
    Define(system, "stack", static _ => { });
    Define(system, "pstack", static _ => { });
    Define(system, "flush", static _ => { });
    Define(system, "file", static i => {
      i.Drop(2);
      throw new PsUnsupportedException("A PostScript program opened a file of its own, which this reader does not give it.");
    });

    Define(system, "filter", PostScriptFilters.Filter);
  }

  private static PsFile _File(PsObject value)
    => value.Type == PsType.File ? value.File : throw new PsErrorException("typecheck", $"A PostScript program read from {value.TypeName}.");

  #endregion

  #region the rest

  private static void _Miscellaneous(PsDictionary system) {
    Define(system, "bind", static i => {
      var value = i.Peek();
      if (value is { Type: PsType.Array, IsExecutable: true })
        _Bind(i, value.Array, 0);
    });

    Define(system, "usertime", static i => i.Push(PsObject.FromInteger(0)));
    Define(system, "realtime", static i => i.Push(PsObject.FromInteger(0)));
    Define(system, "checkpassword", static i => {
      i.Pop();
      i.Push(PsObject.FromBoolean(false));
    });

    // These are entries in the system dictionary rather than operators, and the difference shows:
    // a program asks what level it is talking to with systemdict /languagelevel get and compares
    // the answer with a number, which only works if what is stored there is the number.
    system.Put("version", PsObject.FromString(PsString.Of("3011")));
    system.Put("product", PsObject.FromString(PsString.Of("PNGCrushCS PostScript")));
    system.Put("revision", PsObject.FromInteger(1));
    system.Put("serialnumber", PsObject.FromInteger(0));
    system.Put("languagelevel", PsObject.FromInteger(2));
    system.Put("jobname", PsObject.FromString(PsString.Of("")));
    system.Put("errordict", PsObject.FromDictionary(new(32)));
    system.Put("$error", PsObject.FromDictionary(new(32)));
    system.Put("statusdict", PsObject.FromDictionary(new(32)));
    system.Put("globaldict", PsObject.FromDictionary(new(32)));

    Define(system, "vmstatus", static i => {
      i.Push(PsObject.FromInteger(0));
      i.Push(PsObject.FromInteger(0));
      i.Push(PsObject.FromInteger(0));
    });

    Define(system, "gcheck", static i => {
      i.Pop();
      i.Push(PsObject.FromBoolean(false));
    });

    Define(system, "setglobal", static i => i.PopBoolean());
    Define(system, "currentglobal", static i => i.Push(PsObject.FromBoolean(false)));

    // save and restore roll the graphics state back, which is the part of them that shows on the
    // page. They do not roll back a definition: this keeps one copy of the program's memory, and a
    // definition made and then restored away would have to be a copy of every dictionary to undo.
    // Every file here uses them the way the reference recommends, paired around a page.
    Define(system, "save", static i => {
      i.GraphicsSave();
      i.Push(PsObject.FromSave(i.GraphicsDepth - 1));
    });

    Define(system, "restore", static i => {
      var value = i.Pop();
      if (value.Type != PsType.Save)
        throw new PsErrorException("typecheck", $"A PostScript program restored {value.TypeName}.");

      i.GraphicsRestoreTo(value.SaveDepth);
    });

    Define(system, "handleerror", static _ => { });
    Define(system, "start", static _ => { });
    Define(system, "executive", static _ => { });
    Define(system, "banddevice", static _ => { });
    Define(system, "framedevice", static _ => { });
    Define(system, "renderbands", static _ => { });
  }

  /// <summary>
  /// Replaces the operator names in a procedure with the operators themselves.
  /// </summary>
  /// <remarks>
  /// What <c>bind</c> is for is speed, and it has one visible effect: an operator bound into a
  /// procedure keeps working after the program redefines the name. Programs rely on that, so it is
  /// done rather than treated as a no-op.
  /// </remarks>
  private static void _Bind(PostScriptInterpreter interpreter, PsArray body, int depth) {
    if (depth > 64)
      return;

    for (var index = 0; index < body.Length; ++index) {
      var item = body[index];
      switch (item) {
        case { Type: PsType.Array, IsExecutable: true }:
          _Bind(interpreter, item.Array, depth + 1);
          continue;

        case { Type: PsType.Name, IsExecutable: true }:
          if (interpreter.TryLookup(PsObject.FromName(item.Name), out var value) && value.Type == PsType.Operator)
            body[index] = value;

          continue;
      }
    }
  }

  #endregion
}
