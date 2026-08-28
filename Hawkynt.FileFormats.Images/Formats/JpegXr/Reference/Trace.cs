using System.Text;

namespace SharpAstro.Jxr;

/// <summary>
/// Diagnostic tracing for per-MB oracle comparison against jxrlib's
/// <c>JXRLIB_TRACE</c> hooks (strPredQuantEnc.c). Enabled by setting the
/// <c>JXR_TRACE</c> environment variable; writes to stderr in a format
/// designed to line up with jxrlib's stderr output for diffing. No-op and
/// allocation-free when disabled.
/// </summary>
internal static class Trace
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("JXR_TRACE");
    public static readonly bool On = Path is { Length: > 0 };

    private static void Write(string line)
    {
        if (Path is null) return;
        // JXR_TRACE may be a file path (preferred — survives xunit's console capture)
        // or "1"/"on" for stderr.
        if (Path is "1" or "on" or "true") Console.Error.WriteLine(line);
        else File.AppendAllText(Path, line + "\n");
    }

    public static void Mb(string tag, int mbX, int mbY, int mode, int dcMode, int adMode, int acMode, Macroblock mb, bool pre)
    {
        if (pre)
            Write($"{tag} mb=({mbX},{mbY}) iDCACPredMode=0x{mode:X} (iDCMode={dcMode} iADMode={adMode}) iACPredMode={acMode}");
        string label = pre ? "preDC" : "postDC";
        for (var ch = 0; ch < mb.BlockDc.Length; ch++)
        {
            var sb = new StringBuilder($"  {tag} ch={ch} {label}: ");
            for (var j = 0; j < 16; j++) sb.Append(mb.BlockDc[ch][j]).Append(' ');
            Write(sb.ToString());
        }
    }

    public static void Model(int mbX, int mbY, CodingContext ctx)
    {
        Write($"  MODEL mb=({mbX},{mbY}) DC FlcBits=[{ctx.ModelDc.FlcBits[0]},{ctx.ModelDc.FlcBits[1]}] " +
            $"LP=[{ctx.ModelLp.FlcBits[0]},{ctx.ModelLp.FlcBits[1]}] " +
            $"HP=[{ctx.ModelAc.FlcBits[0]},{ctx.ModelAc.FlcBits[1]}]");
    }
}
