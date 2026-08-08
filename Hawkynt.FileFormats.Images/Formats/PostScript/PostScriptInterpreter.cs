using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core.Vector;

namespace FileFormat.PostScript;

/// <summary>
/// An error the language itself defines, which a program may guard against with <c>stopped</c>.
/// </summary>
/// <remarks>
/// PostScript's errors are part of its control flow: <c>{ ... } stopped</c> is how a program asks
/// whether something worked, and <c>/setcmykcolor where</c> is how it asks whether an operator
/// exists at all. A program that guards a call and takes a different route when it fails is not a
/// program that has gone wrong, so these are caught where it guards and only escape where it does
/// not.
/// </remarks>
public sealed class PsErrorException(string name, string message) : Exception(message) {

  /// <summary>Which of the language's errors this is: <c>undefined</c>, <c>typecheck</c> and so on.</summary>
  public string Name { get; } = name;
}

/// <summary>
/// A construct this interpreter will not approximate, which no guard in the program can catch.
/// </summary>
/// <remarks>
/// The difference from <see cref="PsErrorException"/> is the whole of the honesty of this reader. An
/// error the language defines is something the program may legitimately be testing for. A part of
/// the language this does not implement is something the program is relying on, and passing over it
/// would put a figure on the page in the wrong colour, in the wrong place, or not at all — the
/// picture would be wrong and nothing would say so. So it stops the render instead.
/// </remarks>
public sealed class PsUnsupportedException(string message) : Exception(message);

/// <summary>Runs a PostScript program, drawing what it draws.</summary>
/// <remarks>
/// The machine is the one in chapter 3 of the PostScript Language Reference: an operand stack, a
/// dictionary stack, and an execution stack of things left to do. Executing an object pushes it
/// unless it is executable, in which case a name is looked up through the dictionary stack, an
/// operator is run, and a procedure is put on the execution stack to be run object by object.
/// <para/>
/// Loops live on the execution stack rather than in the host language's own stack, which is what
/// lets <c>exit</c> leave one and <c>stop</c> unwind through several; a loop implemented by
/// recursion could do neither without unwinding the host stack too.
/// </remarks>
public sealed class PostScriptInterpreter {

  /// <summary>How many objects the operand stack may hold, which the reference also bounds.</summary>
  public const int MaxOperandStack = 65536;

  /// <summary>How deep the execution stack may go.</summary>
  public const int MaxExecutionStack = 4096;

  /// <summary>How deep the dictionary stack may go.</summary>
  public const int MaxDictionaryStack = 256;

  /// <summary>How many objects one program may execute, which bounds what a runaway loop can cost.</summary>
  public const long MaxSteps = 80_000_000;

  private readonly List<PsObject> _operands = [];
  private readonly List<PsDictionary> _dictionaries = [];
  private readonly List<PsFrame> _execution = [];
  private readonly List<PsGraphicsState> _graphicsStack = [];

  private long _steps;

  /// <summary>The dictionary the operators live in.</summary>
  public PsDictionary SystemDictionary { get; }

  /// <summary>The dictionary a program's own definitions land in by default.</summary>
  public PsDictionary UserDictionary { get; }

  /// <summary>The page being drawn on.</summary>
  public PsPage Page { get; }

  /// <summary>The current graphics state.</summary>
  public PsGraphicsState Graphics { get; private set; } = new();

  /// <summary>The program's own source, which <c>currentfile</c> hands back.</summary>
  public PsFile Source { get; }

  /// <summary>Whether <c>showpage</c> has been reached, which ends the page this reader draws.</summary>
  public bool PageFinished { get; private set; }

  /// <summary>How many pages the program has shown.</summary>
  public int PagesShown { get; private set; }

  /// <summary>
  /// The last operator the interpreter began, which is what an error message names.
  /// </summary>
  /// <remarks>
  /// A message saying only that something wanted an operand and the stack was empty says nothing
  /// about which file is wrong or where. The operator that raised it is the first thing anyone
  /// reading the message needs, so it is kept.
  /// </remarks>
  public string Running { get; private set; } = "the program";

  /// <summary>Builds an interpreter that will run the given program onto the given page.</summary>
  public PostScriptInterpreter(byte[] program, int start, int end, PsPage page) {
    ArgumentNullException.ThrowIfNull(program);
    ArgumentNullException.ThrowIfNull(page);

    this.Page = page;
    this.Source = new(program, start, end);
    this.Graphics.Ctm = page.DefaultMatrix;

    this.SystemDictionary = new(1024);
    this.UserDictionary = new(512);
    PostScriptOperators.Install(this.SystemDictionary);

    this.SystemDictionary.Put("systemdict", PsObject.FromDictionary(this.SystemDictionary));
    this.SystemDictionary.Put("userdict", PsObject.FromDictionary(this.UserDictionary));
    this.SystemDictionary.IsReadOnly = true;

    this._dictionaries.Add(this.SystemDictionary);
    this._dictionaries.Add(this.UserDictionary);
  }

  #region operand stack

  /// <summary>How many operands there are.</summary>
  public int Count => this._operands.Count;

  /// <summary>Puts an object on the operand stack.</summary>
  public void Push(PsObject value) {
    if (this._operands.Count >= MaxOperandStack)
      throw new PsErrorException("stackoverflow", "A PostScript program put more on the operand stack than it can hold.");

    this._operands.Add(value);
  }

  /// <summary>Takes the top object off the operand stack.</summary>
  public PsObject Pop() {
    var count = this._operands.Count;
    if (count == 0)
      throw new PsErrorException("stackunderflow", "A PostScript operator wanted an operand and the stack was empty.");

    var value = this._operands[count - 1];
    this._operands.RemoveAt(count - 1);
    return value;
  }

  /// <summary>The object <paramref name="depth"/> places down the operand stack, without taking it.</summary>
  public PsObject Peek(int depth = 0) {
    var index = this._operands.Count - 1 - depth;
    if (index < 0)
      throw new PsErrorException("stackunderflow", $"A PostScript operator looked {depth + 1} deep into a stack of {this._operands.Count}.");

    return this._operands[index];
  }

  /// <summary>Replaces the object <paramref name="depth"/> places down the operand stack.</summary>
  public void Poke(int depth, PsObject value) {
    var index = this._operands.Count - 1 - depth;
    if (index < 0)
      throw new PsErrorException("stackunderflow", $"A PostScript operator wrote {depth + 1} deep into a stack of {this._operands.Count}.");

    this._operands[index] = value;
  }

  /// <summary>Throws away the top <paramref name="count"/> operands.</summary>
  public void Drop(int count) {
    for (var i = 0; i < count; ++i)
      this.Pop();
  }

  /// <summary>The whole operand stack, bottom first, for the operators that walk it.</summary>
  public IReadOnlyList<PsObject> Operands => this._operands;

  /// <summary>Empties the operand stack, which <c>clear</c> does.</summary>
  public void ClearOperands() => this._operands.Clear();

  /// <summary>Removes the operand at the given depth, which <c>roll</c> and <c>index</c> build on.</summary>
  public void RemoveAt(int depth) {
    var index = this._operands.Count - 1 - depth;
    if (index < 0)
      throw new PsErrorException("stackunderflow", "A PostScript operator reached past the bottom of the stack.");

    this._operands.RemoveAt(index);
  }

  /// <summary>Takes a number off the stack.</summary>
  public double PopNumber() {
    var value = this.Pop();
    if (!value.IsNumber)
      throw new PsErrorException("typecheck", $"A PostScript operator wanted a number and was given {value.TypeName}.");

    return value.Number;
  }

  /// <summary>Takes an integer off the stack.</summary>
  public long PopInteger() {
    var value = this.Pop();
    if (value.Type != PsType.Integer)
      return value.Type == PsType.Real && value.Number == Math.Floor(value.Number)
        ? (long)value.Number
        : throw new PsErrorException("typecheck", $"A PostScript operator wanted an integer and was given {value.TypeName}.");

    return value.Integer;
  }

  /// <summary>Takes an integer off the stack and checks it is a count something can have.</summary>
  public int PopCount(string what) {
    var value = this.PopInteger();
    if (value is < 0 or > (1 << 24))
      throw new PsErrorException("rangecheck", $"A PostScript program asked for {value} {what}.");

    return (int)value;
  }

  /// <summary>Takes a boolean off the stack.</summary>
  public bool PopBoolean() {
    var value = this.Pop();
    return value.Type == PsType.Boolean
      ? value.Boolean
      : throw new PsErrorException("typecheck", $"A PostScript operator wanted a boolean and was given {value.TypeName}.");
  }

  /// <summary>Takes a procedure off the stack.</summary>
  public PsArray PopProcedure() {
    var value = this.Pop();
    return value is { Type: PsType.Array, IsExecutable: true }
      ? value.Array
      : throw new PsErrorException("typecheck", $"A PostScript operator wanted a procedure and was given {value.TypeName}.");
  }

  /// <summary>Takes an array off the stack, executable or not.</summary>
  public PsArray PopArray() {
    var value = this.Pop();
    return value.Type == PsType.Array
      ? value.Array
      : throw new PsErrorException("typecheck", $"A PostScript operator wanted an array and was given {value.TypeName}.");
  }

  /// <summary>Takes a dictionary off the stack.</summary>
  public PsDictionary PopDictionary() {
    var value = this.Pop();
    return value.Type is PsType.Dictionary or PsType.Font
      ? value.Dictionary
      : throw new PsErrorException("typecheck", $"A PostScript operator wanted a dictionary and was given {value.TypeName}.");
  }

  /// <summary>Takes a string off the stack.</summary>
  public PsString PopString() {
    var value = this.Pop();
    return value.Type == PsType.String
      ? value.String
      : throw new PsErrorException("typecheck", $"A PostScript operator wanted a string and was given {value.TypeName}.");
  }

  /// <summary>Takes as many numbers off the stack as asked, in the order they were pushed.</summary>
  public double[] PopNumbers(int count) {
    var values = new double[count];
    for (var i = count - 1; i >= 0; --i)
      values[i] = this.PopNumber();

    return values;
  }

  #endregion

  #region dictionary stack

  /// <summary>The dictionary definitions land in.</summary>
  public PsDictionary CurrentDictionary => this._dictionaries[^1];

  /// <summary>How deep the dictionary stack is.</summary>
  public int DictionaryDepth => this._dictionaries.Count;

  /// <summary>The dictionary at the given depth, counting from the bottom.</summary>
  public PsDictionary DictionaryAt(int index) => this._dictionaries[index];

  /// <summary>Puts a dictionary on the dictionary stack.</summary>
  public void PushDictionary(PsDictionary dictionary) {
    if (this._dictionaries.Count >= MaxDictionaryStack)
      throw new PsErrorException("dictstackoverflow", "A PostScript program nested more dictionaries than the stack holds.");

    this._dictionaries.Add(dictionary);
  }

  /// <summary>Takes the top dictionary off, which <c>end</c> does.</summary>
  public void PopDictionaryStack() {
    if (this._dictionaries.Count <= 2)
      throw new PsErrorException("dictstackunderflow", "A PostScript program ended more dictionaries than it began.");

    this._dictionaries.RemoveAt(this._dictionaries.Count - 1);
  }

  /// <summary>Looks a name up through the dictionary stack, topmost first.</summary>
  public bool TryLookup(PsObject key, out PsObject value) {
    for (var i = this._dictionaries.Count - 1; i >= 0; --i)
      if (this._dictionaries[i].TryGet(key, out value))
        return true;

    value = PsObject.Null;
    return false;
  }

  /// <summary>Which dictionary on the stack holds the name, or nothing when none does.</summary>
  public PsDictionary? WhereIs(PsObject key) {
    for (var i = this._dictionaries.Count - 1; i >= 0; --i)
      if (this._dictionaries[i].Contains(key))
        return this._dictionaries[i];

    return null;
  }

  #endregion

  #region graphics stack

  /// <summary>How deep the graphics state stack is.</summary>
  public int GraphicsDepth => this._graphicsStack.Count;

  /// <summary>Puts the graphics state away.</summary>
  public void GraphicsSave() {
    if (this._graphicsStack.Count >= 1024)
      throw new PsErrorException("limitcheck", "A PostScript program saved the graphics state deeper than it can be saved.");

    this._graphicsStack.Add(this.Graphics.Clone());
  }

  /// <summary>Brings the graphics state back.</summary>
  public void GraphicsRestore() {
    var count = this._graphicsStack.Count;
    if (count == 0)
      throw new PsErrorException("gsaveundefined", "A PostScript program restored a graphics state it never saved.");

    this.Graphics = this._graphicsStack[count - 1];
    this._graphicsStack.RemoveAt(count - 1);
  }

  /// <summary>Brings the graphics state back to the depth a save was taken at.</summary>
  public void GraphicsRestoreTo(int depth) {
    while (this._graphicsStack.Count > depth)
      this.GraphicsRestore();
  }

  #endregion

  #region execution

  /// <summary>Says a page has been shown, and whether the program should stop.</summary>
  public void ShowPage() {
    ++this.PagesShown;

    // Only the first page is drawn: a raster is one picture, and the second page would draw over the
    // first. Which page was taken is stated rather than left for the caller to guess.
    this.PageFinished = true;
  }

  /// <summary>Runs the whole program, or until the first page is done.</summary>
  public void Run() {
    this._Push(new PsFileFrame(this.Source));
    this._Loop();
  }

  /// <summary>Runs one object to completion, for the operators that call back into the program.</summary>
  /// <remarks>
  /// <c>image</c> with a procedure data source and <c>forall</c> over a dictionary both have to run a
  /// piece of the program from inside an operator. Running it on a fresh execution stack down to the
  /// depth it started at keeps that call self-contained: it cannot fall out into whatever the outer
  /// program was doing, and the outer program cannot see it.
  /// </remarks>
  public void RunNested(PsObject procedure) {
    var depth = this._execution.Count;
    this.Invoke(procedure);
    this._Loop(depth);
  }

  private void _Loop(int floor = 0) {
    while (this._execution.Count > floor) {
      if (this.PageFinished)
        return;

      if (++this._steps > MaxSteps)
        throw new PsUnsupportedException($"A PostScript program still running after {MaxSteps} steps, which is longer than a page takes.");

      var frame = this._execution[^1];
      PsObject next;
      try {
        if (!frame.Next(this, out next)) {
          this._execution.RemoveAt(this._execution.Count - 1);
          frame.Finished(this);
          continue;
        }
      } catch (PsErrorException error) {
        this._Raise(error, floor);
        continue;
      }

      try {
        this.Execute(next);
      } catch (PsErrorException error) {
        this._Raise(error, floor);
      }
    }
  }

  /// <summary>
  /// Deals with an error the language defines: the nearest <c>stopped</c> catches it, and where
  /// there is none it ends the render.
  /// </summary>
  private void _Raise(PsErrorException error, int floor) {
    for (var i = this._execution.Count - 1; i >= floor; --i) {
      if (this._execution[i] is not PsStoppedFrame)
        continue;

      this._execution.RemoveRange(i, this._execution.Count - i);
      this.Push(PsObject.FromBoolean(true));
      return;
    }

    throw new InvalidDataException($"PostScript error {error.Name} in {this.Running}: {error.Message}");
  }

  /// <summary>
  /// Executes an object the interpreter came upon, which for most of them means pushing it.
  /// </summary>
  /// <remarks>
  /// A procedure met like this — read out of the program, or sitting inside another procedure that
  /// is running — is put on the operand stack rather than run. That is what the reference says and
  /// it is the whole of how the language works: <c>{ ... } def</c> would run the body instead of
  /// defining it otherwise, and <c>ifelse</c> would have nothing left to choose between. A procedure
  /// runs when something asks it to, which is <see cref="Invoke"/>.
  /// </remarks>
  public void Execute(PsObject value) {
    if (value is { IsExecutable: true, Type: PsType.Array }) {
      this.Push(value);
      return;
    }

    this.Invoke(value);
  }

  /// <summary>Runs an object, as <c>exec</c> and a name standing for a procedure do.</summary>
  public void Invoke(PsObject value) {
    if (!value.IsExecutable) {
      this.Push(value);
      return;
    }

    switch (value.Type) {
      case PsType.Name:
        this._ExecuteName(value);
        return;

      case PsType.Operator:
        this.Running = value.Operator.Name;
        value.Operator.Action(this);
        return;

      case PsType.Array:
        this._Push(new PsProcedureFrame(value.Array));
        return;

      case PsType.String:
        this._Push(new PsFileFrame(new(value.String.Bytes, value.String.Offset, value.String.Offset + value.String.Length)));
        return;

      default:
        this.Push(value);
        return;
    }
  }

  private void _ExecuteName(PsObject name) {
    var text = name.Name;

    // Two slashes before a name ask for what it means now rather than for the name itself, so the
    // lookup happens here and the value is executed in its place.
    if (text.StartsWith("//", StringComparison.Ordinal)) {
      var immediate = PsObject.FromName(text[2..]);
      if (!this.TryLookup(immediate, out var resolved))
        throw new PsErrorException("undefined", $"The PostScript name //{text[2..]} is not defined.");

      this.Push(resolved);
      return;
    }

    if (!this.TryLookup(PsObject.FromName(text), out var value))
      throw new PsErrorException("undefined", $"The PostScript name {text} is not defined.");

    // A name whose value is itself a name that executes would loop forever if it named itself; the
    // step budget catches that, and everything short of it is what the language does.
    if (value.IsExecutable && value.Type == PsType.Name && value.Name == text)
      throw new PsErrorException("undefined", $"The PostScript name {text} is defined as itself.");

    this.Invoke(value);
  }

  /// <summary>Puts a frame on the execution stack.</summary>
  internal void PushFrame(PsFrame frame) => this._Push(frame);

  private void _Push(PsFrame frame) {
    if (this._execution.Count >= MaxExecutionStack)
      throw new PsErrorException("execstackoverflow", "A PostScript program nested execution deeper than the stack holds.");

    this._execution.Add(frame);
  }

  /// <summary>Leaves the innermost loop, which <c>exit</c> does.</summary>
  public void Exit() {
    for (var i = this._execution.Count - 1; i >= 0; --i) {
      if (this._execution[i] is PsStoppedFrame)
        break;

      if (!this._execution[i].IsLoop)
        continue;

      this._execution.RemoveRange(i, this._execution.Count - i);
      return;
    }

    throw new PsErrorException("invalidexit", "A PostScript program left a loop it was not in.");
  }

  /// <summary>Stops, which the nearest <c>stopped</c> catches.</summary>
  public void Stop() {
    for (var i = this._execution.Count - 1; i >= 0; --i) {
      if (this._execution[i] is not PsStoppedFrame)
        continue;

      this._execution.RemoveRange(i, this._execution.Count - i);
      this.Push(PsObject.FromBoolean(true));
      return;
    }

    throw new InvalidDataException("A PostScript program stopped outside any guard, which ends the page.");
  }

  /// <summary>Ends the program, which <c>quit</c> does.</summary>
  public void Quit() {
    this._execution.Clear();
    this.PageFinished = true;
  }

  #endregion
}

/// <summary>One thing left to do on the execution stack.</summary>
internal abstract class PsFrame {

  /// <summary>The next object to execute, or nothing when the frame is done.</summary>
  public abstract bool Next(PostScriptInterpreter interpreter, out PsObject value);

  /// <summary>Whether <c>exit</c> leaves this frame.</summary>
  public virtual bool IsLoop => false;

  /// <summary>What happens when the frame runs out on its own.</summary>
  public virtual void Finished(PostScriptInterpreter interpreter) { }
}

/// <summary>Objects read from a file, which is how a whole program is run.</summary>
internal sealed class PsFileFrame(PsFile file) : PsFrame {

  public override bool Next(PostScriptInterpreter interpreter, out PsObject value) {
    var token = PostScriptScanner.Next(file);
    if (token == null) {
      value = PsObject.Null;
      return false;
    }

    value = token.Value;
    return true;
  }
}

/// <summary>The objects of a procedure, one after another.</summary>
internal sealed class PsProcedureFrame(PsArray body) : PsFrame {

  private int _index;

  public override bool Next(PostScriptInterpreter interpreter, out PsObject value) {
    if (this._index >= body.Length) {
      value = PsObject.Null;
      return false;
    }

    value = body[this._index++];
    return true;
  }
}

/// <summary>A guard <c>stop</c> and the language's own errors unwind to.</summary>
internal sealed class PsStoppedFrame : PsFrame {

  public override bool Next(PostScriptInterpreter interpreter, out PsObject value) {
    value = PsObject.Null;
    return false;
  }

  /// <summary>Falling off the end of the guarded procedure means it did not stop.</summary>
  public override void Finished(PostScriptInterpreter interpreter) => interpreter.Push(PsObject.FromBoolean(false));
}

/// <summary>A body run over and over, which every one of the language's loops is.</summary>
internal abstract class PsLoopFrame(PsArray body) : PsFrame {

  /// <summary>Something to execute that does nothing, so a loop over an empty body still steps.</summary>
  private static readonly PsObject _DoNothing = PsObject.FromOperator(new("%loop", static _ => { }));

  private int _index = int.MaxValue;

  public override bool IsLoop => true;

  /// <summary>Sets up the next turn, or says there is not one.</summary>
  protected abstract bool StartIteration(PostScriptInterpreter interpreter);

  public override bool Next(PostScriptInterpreter interpreter, out PsObject value) {
    if (this._index >= body.Length) {
      if (!this.StartIteration(interpreter)) {
        value = PsObject.Null;
        return false;
      }

      this._index = 0;

      // A loop over an empty body would spin without ever executing anything, so it hands back one
      // object that does nothing: the step budget then bounds it as it bounds every other loop.
      if (body.Length == 0) {
        value = _DoNothing;
        return true;
      }
    }

    value = body[this._index++];
    return true;
  }
}

/// <summary>A body run for each value from a start by a step to a limit.</summary>
internal sealed class PsForFrame(PsArray body, double start, double increment, double limit, bool integer) : PsLoopFrame(body) {

  private double _value = start;

  protected override bool StartIteration(PostScriptInterpreter interpreter) {
    if (increment >= 0 ? this._value > limit : this._value < limit)
      return false;

    interpreter.Push(integer ? PsObject.FromInteger((long)this._value) : PsObject.FromReal(this._value));
    this._value += increment;
    return true;
  }
}

/// <summary>A body run a stated number of times.</summary>
internal sealed class PsRepeatFrame(PsArray body, long count) : PsLoopFrame(body) {

  private long _left = count;

  protected override bool StartIteration(PostScriptInterpreter interpreter) {
    if (this._left <= 0)
      return false;

    --this._left;
    return true;
  }
}

/// <summary>A body run until something leaves it.</summary>
internal sealed class PsLoopForeverFrame(PsArray body) : PsLoopFrame(body) {

  protected override bool StartIteration(PostScriptInterpreter interpreter) => true;
}

/// <summary>
/// A body run once for each element of an array, a string or a dictionary.
/// </summary>
/// <param name="stride">
/// How many objects one turn puts on the stack: one for an array or a string, and two for a
/// dictionary, which hands the body a key and its value together.
/// </param>
internal sealed class PsForAllFrame(PsArray body, IReadOnlyList<PsObject> items, int stride) : PsLoopFrame(body) {

  private int _index;

  protected override bool StartIteration(PostScriptInterpreter interpreter) {
    if (this._index + stride > items.Count)
      return false;

    for (var i = 0; i < stride; ++i)
      interpreter.Push(items[this._index++]);

    return true;
  }
}
