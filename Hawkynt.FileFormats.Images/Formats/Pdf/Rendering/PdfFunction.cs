using System;
using System.Collections.Generic;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// A PDF function (PDF 32000-1 §7.10): sampled, exponential, stitching, or a PostScript calculator
/// program, mapping m inputs to n outputs.
/// </summary>
/// <remarks>
/// Written from the specification (rung 3). Sampled functions interpolate multilinearly across
/// their sample grid, which is what the specification asks for when <c>/Order</c> is 1 and what
/// both pdf.js and poppler do for order 3 as well. The calculator is compiled once into a flat
/// list of operations with jump offsets for <c>if</c> and <c>ifelse</c>, and run on a fixed stack.
/// </remarks>
internal abstract class PdfFunction {

  protected double[] Domain = [0, 1];
  protected double[]? Range;

  /// <summary>How many values the function returns.</summary>
  public abstract int Outputs { get; }

  /// <summary>How many values the function takes.</summary>
  public virtual int Inputs => Math.Max(1, this.Domain.Length / 2);

  /// <summary>Evaluates the function.</summary>
  public abstract void Evaluate(ReadOnlySpan<double> input, Span<double> output);

  /// <summary>Evaluates a one-input function at a point.</summary>
  public void Evaluate(double t, Span<double> output) {
    Span<double> input = [t];
    this.Evaluate(input, output);
  }

  protected double ClipInput(double value, int index) {
    var lo = this.Domain[2 * index];
    var hi = this.Domain[2 * index + 1];
    return double.IsNaN(value) ? lo : Math.Clamp(value, Math.Min(lo, hi), Math.Max(lo, hi));
  }

  protected void ClipOutputs(Span<double> output) {
    if (this.Range == null)
      return;

    for (var i = 0; i < output.Length && 2 * i + 1 < this.Range.Length; ++i) {
      var lo = this.Range[2 * i];
      var hi = this.Range[2 * i + 1];
      output[i] = double.IsNaN(output[i]) ? lo : Math.Clamp(output[i], Math.Min(lo, hi), Math.Max(lo, hi));
    }
  }

  /// <summary>Reads a function, or an array of one-output functions taken together as one.</summary>
  public static PdfFunction Parse(PdfDocument document, object? value, int depth = 0) {
    if (depth > 16)
      throw new NotSupportedException("PDF functions nested more than sixteen deep are not supported.");

    var resolved = document.Resolve(value);
    if (resolved is List<object?> list) {
      var parts = new List<PdfFunction>();
      foreach (var item in list)
        parts.Add(Parse(document, item, depth + 1));
      return new ArrayFunction(parts);
    }

    var dict = document.Dict(resolved) ?? throw new NotSupportedException("A PDF function is neither a dictionary nor a stream.");
    var type = document.Integer(dict, "FunctionType", -1);
    PdfFunction function = type switch {
      0 => SampledFunction.Create(document, resolved as PdfStreamObject ?? throw new NotSupportedException("A sampled PDF function must be a stream."), dict),
      2 => ExponentialFunction.Create(document, dict),
      3 => StitchingFunction.Create(document, dict, depth),
      4 => PostScriptFunction.Create(document, resolved as PdfStreamObject ?? throw new NotSupportedException("A PostScript calculator function must be a stream."), dict),
      _ => throw new NotSupportedException($"PDF function type {type} is not supported."),
    };

    function.Domain = document.Numbers(dict.Raw("Domain")) is { Length: >= 2 } domain ? domain : [0, 1];
    function.Range = document.Numbers(dict.Raw("Range")) is { Length: >= 2 } range ? range : null;
    if (function is SampledFunction or PostScriptFunction && function.Range == null)
      throw new NotSupportedException($"A type {type} PDF function without /Range is not supported.");

    return function;
  }

  private sealed class ArrayFunction(List<PdfFunction> parts) : PdfFunction {
    public override int Outputs => parts.Count;
    public override int Inputs => parts.Count > 0 ? parts[0].Inputs : 1;

    public override void Evaluate(ReadOnlySpan<double> input, Span<double> output) {
      Span<double> one = stackalloc double[Math.Max(1, parts.Count == 0 ? 1 : parts[0].Outputs)];
      for (var i = 0; i < parts.Count && i < output.Length; ++i) {
        if (one.Length < parts[i].Outputs)
          one = new double[parts[i].Outputs];
        parts[i].Evaluate(input, one);
        output[i] = one[0];
      }
    }
  }

  private sealed class SampledFunction : PdfFunction {
    private int[] _size = [];
    private double[] _encode = [];
    private double[] _decode = [];
    private double[] _samples = [];
    private int _inputs, _outputs;

    public override int Outputs => this._outputs;

    public static SampledFunction Create(PdfDocument document, PdfStreamObject stream, PdfDict dict) {
      var size = document.Numbers(dict.Raw("Size")) ?? throw new NotSupportedException("A sampled PDF function has no /Size.");
      var range = document.Numbers(dict.Raw("Range")) ?? throw new NotSupportedException("A sampled PDF function has no /Range.");
      var domain = document.Numbers(dict.Raw("Domain")) ?? [0, 1];
      var bits = document.Integer(dict, "BitsPerSample", 8);
      if (bits is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32))
        throw new NotSupportedException($"A sampled PDF function with {bits} bits per sample is not supported.");

      var inputs = domain.Length / 2;
      var outputs = range.Length / 2;
      var f = new SampledFunction { _inputs = inputs, _outputs = outputs, _size = new int[inputs] };
      long total = outputs;
      for (var i = 0; i < inputs; ++i) {
        f._size[i] = Math.Max(1, i < size.Length ? (int)size[i] : 1);
        total *= f._size[i];
      }

      if (total > 1 << 26)
        throw new NotSupportedException("A sampled PDF function this large is not supported.");

      var encode = document.Numbers(dict.Raw("Encode"));
      f._encode = new double[inputs * 2];
      for (var i = 0; i < inputs; ++i) {
        f._encode[2 * i] = encode != null && encode.Length > 2 * i + 1 ? encode[2 * i] : 0;
        f._encode[2 * i + 1] = encode != null && encode.Length > 2 * i + 1 ? encode[2 * i + 1] : f._size[i] - 1;
      }

      f._decode = document.Numbers(dict.Raw("Decode")) is { } decode && decode.Length >= outputs * 2 ? decode : range;

      var data = document.Decode(stream);
      f._samples = new double[total];
      var max = bits == 32 ? uint.MaxValue : (1L << bits) - 1;
      long bitPos = 0;
      for (long n = 0; n < total; ++n) {
        long v = 0;
        for (var b = 0; b < bits; ++b, ++bitPos) {
          var byteIndex = bitPos >> 3;
          var bit = byteIndex < data.Length ? (data[byteIndex] >> (7 - (int)(bitPos & 7))) & 1 : 0;
          v = (v << 1) | (long)bit;
        }

        f._samples[n] = v / (double)max;
      }

      return f;
    }

    public override void Evaluate(ReadOnlySpan<double> input, Span<double> output) {
      // Each input is clipped, encoded onto the sample grid, and the 2^m surrounding samples are
      // weighted by their distances — multilinear interpolation.
      Span<int> lower = stackalloc int[this._inputs];
      Span<double> frac = stackalloc double[this._inputs];
      for (var i = 0; i < this._inputs; ++i) {
        var x = this.ClipInput(i < input.Length ? input[i] : 0, i);
        var d0 = this.Domain[2 * i];
        var d1 = this.Domain[2 * i + 1];
        var e = d1 == d0 ? this._encode[2 * i] : this._encode[2 * i] + (x - d0) * (this._encode[2 * i + 1] - this._encode[2 * i]) / (d1 - d0);
        e = Math.Clamp(e, 0, this._size[i] - 1);
        var l = Math.Min((int)Math.Floor(e), Math.Max(0, this._size[i] - 2));
        lower[i] = l;
        frac[i] = this._size[i] == 1 ? 0 : e - l;
      }

      // Only inputs that fall between two samples need both neighbours; the rest contribute one
      // sample each, so the corner count is 2 to the number of in-between inputs, not of all inputs.
      Span<int> active = stackalloc int[this._inputs];
      var activeCount = 0;
      long baseIndex = 0;
      long stride = this._outputs;
      Span<long> strides = stackalloc long[this._inputs];
      for (var i = 0; i < this._inputs; ++i) {
        strides[i] = stride;
        baseIndex += lower[i] * stride;
        if (frac[i] > 1e-12 && this._size[i] > 1 && activeCount < 16)
          active[activeCount++] = i;
        stride *= this._size[i];
      }

      for (var o = 0; o < this._outputs && o < output.Length; ++o) {
        var sum = 0.0;
        var corners = 1 << activeCount;
        for (var c = 0; c < corners; ++c) {
          var weight = 1.0;
          var index = baseIndex;
          for (var k = 0; k < activeCount; ++k) {
            var i = active[k];
            if (((c >> k) & 1) == 1) {
              weight *= frac[i];
              index += strides[i];
            } else
              weight *= 1 - frac[i];
          }

          if (weight != 0)
            sum += weight * this._samples[index + o];
        }

        output[o] = this._decode[2 * o] + sum * (this._decode[2 * o + 1] - this._decode[2 * o]);
      }

      this.ClipOutputs(output);
    }
  }

  private sealed class ExponentialFunction : PdfFunction {
    private double[] _c0 = [0];
    private double[] _c1 = [1];
    private double _n;

    public override int Outputs => this._c0.Length;

    public static ExponentialFunction Create(PdfDocument document, PdfDict dict) {
      var c0 = document.Numbers(dict.Raw("C0")) ?? [0];
      var c1 = document.Numbers(dict.Raw("C1")) ?? [1];
      if (c1.Length != c0.Length) {
        var n = Math.Max(c0.Length, c1.Length);
        Array.Resize(ref c0, n);
        Array.Resize(ref c1, n);
      }

      return new ExponentialFunction { _c0 = c0, _c1 = c1, _n = document.Number(dict, "N", 1) };
    }

    public override void Evaluate(ReadOnlySpan<double> input, Span<double> output) {
      var x = this.ClipInput(input.Length > 0 ? input[0] : 0, 0);
      var p = this._n == 1 ? x : Math.Pow(x, this._n);
      for (var i = 0; i < this._c0.Length && i < output.Length; ++i)
        output[i] = this._c0[i] + p * (this._c1[i] - this._c0[i]);
      this.ClipOutputs(output);
    }
  }

  private sealed class StitchingFunction : PdfFunction {
    private PdfFunction[] _functions = [];
    private double[] _bounds = [];
    private double[] _encode = [];

    public override int Outputs => this._functions.Length > 0 ? this._functions[0].Outputs : 1;

    public static StitchingFunction Create(PdfDocument document, PdfDict dict, int depth) {
      var list = document.Array(dict.Raw("Functions")) ?? throw new NotSupportedException("A stitching PDF function has no /Functions.");
      var functions = new PdfFunction[list.Count];
      for (var i = 0; i < list.Count; ++i)
        functions[i] = Parse(document, list[i], depth + 1);

      return new StitchingFunction {
        _functions = functions,
        _bounds = document.Numbers(dict.Raw("Bounds")) ?? [],
        _encode = document.Numbers(dict.Raw("Encode")) ?? [],
      };
    }

    public override void Evaluate(ReadOnlySpan<double> input, Span<double> output) {
      if (this._functions.Length == 0)
        return;

      var x = this.ClipInput(input.Length > 0 ? input[0] : 0, 0);
      var k = 0;
      while (k < this._bounds.Length && k < this._functions.Length - 1 && x >= this._bounds[k])
        ++k;

      var low = k == 0 ? this.Domain[0] : this._bounds[k - 1];
      var high = k >= this._bounds.Length ? this.Domain[1] : this._bounds[k];
      var e0 = 2 * k < this._encode.Length ? this._encode[2 * k] : 0;
      var e1 = 2 * k + 1 < this._encode.Length ? this._encode[2 * k + 1] : 1;
      var t = high == low ? e0 : e0 + (x - low) * (e1 - e0) / (high - low);
      Span<double> one = [t];
      this._functions[k].Evaluate(one, output);
      this.ClipOutputs(output);
    }
  }

  /// <summary>The type 4 calculator: a PostScript subset over numbers and booleans (§7.10.5).</summary>
  private sealed class PostScriptFunction : PdfFunction {

    private enum Op : byte {
      Push, Abs, Add, Atan, Ceiling, Cos, Cvi, Cvr, Div, Exp, Floor, Idiv, Ln, Log, Mod, Mul, Neg, Round, Sin, Sqrt, Sub, Truncate,
      And, Bitshift, Eq, False, Ge, Gt, Le, Lt, Ne, Not, Or, True, Xor,
      Copy, Dup, Exch, Index, Pop, Roll,
      JumpIfFalse, Jump,
    }

    private Op[] _ops = [];
    private double[] _args = [];
    private int _outputs;

    public override int Outputs => this._outputs;

    public static PostScriptFunction Create(PdfDocument document, PdfStreamObject stream, PdfDict dict) {
      var data = document.Decode(stream);
      var lexer = new PdfLexer(data);
      var ops = new List<Op>();
      var args = new List<double>();
      lexer.SkipWhitespace();
      if (lexer.Position >= data.Length || data[lexer.Position] != (byte)'{')
        throw new NotSupportedException("A PostScript calculator function does not open with '{'.");
      ++lexer.Position;
      _Compile(lexer, ops, args, 0);
      var range = document.Numbers(dict.Raw("Range")) ?? [];
      return new PostScriptFunction { _ops = ops.ToArray(), _args = args.ToArray(), _outputs = range.Length / 2 };
    }

    private static void _Compile(PdfLexer lexer, List<Op> ops, List<double> args, int depth) {
      if (depth > 64)
        throw new NotSupportedException("A PostScript calculator function nests procedures too deeply.");

      // Procedures are held as pending blocks until the 'if' or 'ifelse' after them says which.
      var pending = new List<(List<Op> Ops, List<double> Args)>();
      while (true) {
        lexer.SkipWhitespace();
        if (lexer.Position >= lexer.End)
          throw new NotSupportedException("A PostScript calculator function is not closed.");

        var b = lexer.Data[lexer.Position];
        if (b == (byte)'}') {
          ++lexer.Position;
          _Flush(pending, ops, args);
          return;
        }

        if (b == (byte)'{') {
          ++lexer.Position;
          var blockOps = new List<Op>();
          var blockArgs = new List<double>();
          _Compile(lexer, blockOps, blockArgs, depth + 1);
          pending.Add((blockOps, blockArgs));
          continue;
        }

        var token = lexer.ReadContentObject();
        if (token is int or double) {
          _Flush(pending, ops, args);
          ops.Add(Op.Push);
          args.Add(PdfValues.Number(token));
          continue;
        }

        if (token is bool flag) {
          _Flush(pending, ops, args);
          ops.Add(flag ? Op.True : Op.False);
          args.Add(0);
          continue;
        }

        var name = (token as PdfOperator?)?.Name ?? throw new NotSupportedException("A PostScript calculator function contains something other than numbers and operators.");
        if (name == "if") {
          if (pending.Count < 1)
            throw new NotSupportedException("A PostScript 'if' without a procedure.");
          var proc = pending[^1];
          pending.RemoveAt(pending.Count - 1);
          _Flush(pending, ops, args);
          ops.Add(Op.JumpIfFalse);
          args.Add(proc.Ops.Count);
          ops.AddRange(proc.Ops);
          args.AddRange(proc.Args);
          continue;
        }

        if (name == "ifelse") {
          if (pending.Count < 2)
            throw new NotSupportedException("A PostScript 'ifelse' without two procedures.");
          var whenFalse = pending[^1];
          var whenTrue = pending[^2];
          pending.RemoveRange(pending.Count - 2, 2);
          _Flush(pending, ops, args);
          ops.Add(Op.JumpIfFalse);
          args.Add(whenTrue.Ops.Count + 1);
          ops.AddRange(whenTrue.Ops);
          args.AddRange(whenTrue.Args);
          ops.Add(Op.Jump);
          args.Add(whenFalse.Ops.Count);
          ops.AddRange(whenFalse.Ops);
          args.AddRange(whenFalse.Args);
          continue;
        }

        _Flush(pending, ops, args);
        ops.Add(name switch {
          "abs" => Op.Abs, "add" => Op.Add, "atan" => Op.Atan, "ceiling" => Op.Ceiling, "cos" => Op.Cos, "cvi" => Op.Cvi, "cvr" => Op.Cvr,
          "div" => Op.Div, "exp" => Op.Exp, "floor" => Op.Floor, "idiv" => Op.Idiv, "ln" => Op.Ln, "log" => Op.Log, "mod" => Op.Mod,
          "mul" => Op.Mul, "neg" => Op.Neg, "round" => Op.Round, "sin" => Op.Sin, "sqrt" => Op.Sqrt, "sub" => Op.Sub, "truncate" => Op.Truncate,
          "and" => Op.And, "bitshift" => Op.Bitshift, "eq" => Op.Eq, "ge" => Op.Ge, "gt" => Op.Gt, "le" => Op.Le, "lt" => Op.Lt,
          "ne" => Op.Ne, "not" => Op.Not, "or" => Op.Or, "xor" => Op.Xor, "true" => Op.True, "false" => Op.False,
          "copy" => Op.Copy, "dup" => Op.Dup, "exch" => Op.Exch, "index" => Op.Index, "pop" => Op.Pop, "roll" => Op.Roll,
          _ => throw new NotSupportedException($"The PostScript calculator operator '{name}' is not supported."),
        });
        args.Add(0);
      }
    }

    private static void _Flush(List<(List<Op> Ops, List<double> Args)> pending, List<Op> ops, List<double> args) {
      // A procedure no 'if' claimed is not valid in a calculator function; ignore it as viewers do.
      pending.Clear();
    }

    public override void Evaluate(ReadOnlySpan<double> input, Span<double> output) {
      Span<double> stack = stackalloc double[128];
      var sp = 0;
      for (var i = 0; i < input.Length && i < this.Domain.Length / 2; ++i)
        stack[sp++] = this.ClipInput(input[i], i);

      var ops = this._ops;
      for (var pc = 0; pc < ops.Length; ++pc) {
        if (sp > 120)
          break;

        switch (ops[pc]) {
          case Op.Push: stack[sp++] = this._args[pc]; break;
          case Op.Abs: if (sp >= 1) stack[sp - 1] = Math.Abs(stack[sp - 1]); break;
          case Op.Add: if (sp >= 2) { stack[sp - 2] += stack[sp - 1]; --sp; } break;
          case Op.Sub: if (sp >= 2) { stack[sp - 2] -= stack[sp - 1]; --sp; } break;
          case Op.Mul: if (sp >= 2) { stack[sp - 2] *= stack[sp - 1]; --sp; } break;
          case Op.Div: if (sp >= 2) { stack[sp - 2] = stack[sp - 1] == 0 ? 0 : stack[sp - 2] / stack[sp - 1]; --sp; } break;
          case Op.Idiv: if (sp >= 2) { var d = (long)stack[sp - 1]; stack[sp - 2] = d == 0 ? 0 : (long)stack[sp - 2] / d; --sp; } break;
          case Op.Mod: if (sp >= 2) { var d = (long)stack[sp - 1]; stack[sp - 2] = d == 0 ? 0 : (long)stack[sp - 2] % d; --sp; } break;
          case Op.Atan: if (sp >= 2) { var a = Math.Atan2(stack[sp - 2], stack[sp - 1]) * 180 / Math.PI; stack[sp - 2] = a < 0 ? a + 360 : a; --sp; } break;
          case Op.Exp: if (sp >= 2) { stack[sp - 2] = Math.Pow(stack[sp - 2], stack[sp - 1]); --sp; } break;
          case Op.Ceiling: if (sp >= 1) stack[sp - 1] = Math.Ceiling(stack[sp - 1]); break;
          case Op.Floor: if (sp >= 1) stack[sp - 1] = Math.Floor(stack[sp - 1]); break;
          case Op.Round: if (sp >= 1) stack[sp - 1] = Math.Floor(stack[sp - 1] + 0.5); break;
          case Op.Truncate: if (sp >= 1) stack[sp - 1] = Math.Truncate(stack[sp - 1]); break;
          case Op.Cvi: if (sp >= 1) stack[sp - 1] = Math.Truncate(stack[sp - 1]); break;
          case Op.Cvr: break;
          case Op.Cos: if (sp >= 1) stack[sp - 1] = Math.Cos(stack[sp - 1] * Math.PI / 180); break;
          case Op.Sin: if (sp >= 1) stack[sp - 1] = Math.Sin(stack[sp - 1] * Math.PI / 180); break;
          case Op.Sqrt: if (sp >= 1) stack[sp - 1] = Math.Sqrt(Math.Max(0, stack[sp - 1])); break;
          case Op.Ln: if (sp >= 1) stack[sp - 1] = Math.Log(stack[sp - 1]); break;
          case Op.Log: if (sp >= 1) stack[sp - 1] = Math.Log10(stack[sp - 1]); break;
          case Op.Neg: if (sp >= 1) stack[sp - 1] = -stack[sp - 1]; break;
          case Op.True: stack[sp++] = 1; break;
          case Op.False: stack[sp++] = 0; break;
          case Op.And: if (sp >= 2) { stack[sp - 2] = (long)stack[sp - 2] & (long)stack[sp - 1]; --sp; } break;
          case Op.Or: if (sp >= 2) { stack[sp - 2] = (long)stack[sp - 2] | (long)stack[sp - 1]; --sp; } break;
          case Op.Xor: if (sp >= 2) { stack[sp - 2] = (long)stack[sp - 2] ^ (long)stack[sp - 1]; --sp; } break;
          case Op.Not:
            // Booleans are held as 0 and 1, so 'not' of one of them stays a boolean; of an integer it is the bitwise complement.
            if (sp >= 1) stack[sp - 1] = stack[sp - 1] is 0 or 1 ? 1 - stack[sp - 1] : ~(long)stack[sp - 1];
            break;
          case Op.Bitshift: if (sp >= 2) { var s = (int)stack[sp - 1]; var v = (long)stack[sp - 2]; stack[sp - 2] = s >= 0 ? v << s : v >> -s; --sp; } break;
          case Op.Eq: if (sp >= 2) { stack[sp - 2] = stack[sp - 2] == stack[sp - 1] ? 1 : 0; --sp; } break;
          case Op.Ne: if (sp >= 2) { stack[sp - 2] = stack[sp - 2] != stack[sp - 1] ? 1 : 0; --sp; } break;
          case Op.Ge: if (sp >= 2) { stack[sp - 2] = stack[sp - 2] >= stack[sp - 1] ? 1 : 0; --sp; } break;
          case Op.Gt: if (sp >= 2) { stack[sp - 2] = stack[sp - 2] > stack[sp - 1] ? 1 : 0; --sp; } break;
          case Op.Le: if (sp >= 2) { stack[sp - 2] = stack[sp - 2] <= stack[sp - 1] ? 1 : 0; --sp; } break;
          case Op.Lt: if (sp >= 2) { stack[sp - 2] = stack[sp - 2] < stack[sp - 1] ? 1 : 0; --sp; } break;
          case Op.Dup: if (sp >= 1) { stack[sp] = stack[sp - 1]; ++sp; } break;
          case Op.Pop: if (sp >= 1) --sp; break;
          case Op.Exch: if (sp >= 2) (stack[sp - 1], stack[sp - 2]) = (stack[sp - 2], stack[sp - 1]); break;
          case Op.Copy: {
            if (sp < 1) break;
            var n = (int)stack[--sp];
            if (n < 0 || n > sp || sp + n > 120) break;
            for (var k = 0; k < n; ++k)
              stack[sp + k] = stack[sp - n + k];
            sp += n;
            break;
          }
          case Op.Index: {
            if (sp < 1) break;
            var n = (int)stack[sp - 1];
            if (n < 0 || n >= sp - 1) break;
            stack[sp - 1] = stack[sp - 2 - n];
            break;
          }
          case Op.Roll: {
            if (sp < 2) break;
            var j = (int)stack[--sp];
            var n = (int)stack[--sp];
            if (n <= 0 || n > sp) break;
            j = ((j % n) + n) % n;
            if (j == 0) break;
            Span<double> tmp = stackalloc double[n];
            for (var k = 0; k < n; ++k)
              tmp[(k + j) % n] = stack[sp - n + k];
            tmp.CopyTo(stack[(sp - n)..]);
            break;
          }
          case Op.JumpIfFalse:
            if (sp >= 1 && stack[--sp] == 0)
              pc += (int)this._args[pc];
            break;
          case Op.Jump:
            pc += (int)this._args[pc];
            break;
        }
      }

      // The results are whatever is left on the stack, the last one on top.
      var outputs = Math.Min(this._outputs, output.Length);
      for (var i = 0; i < outputs; ++i) {
        var at = sp - outputs + i;
        output[i] = at >= 0 ? stack[at] : 0;
      }

      this.ClipOutputs(output);
    }
  }
}
