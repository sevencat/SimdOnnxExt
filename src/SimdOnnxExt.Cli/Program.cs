using System.Text.Json;
using Sdcb.SimdOnnxExt.Onnx;

// simdonnxx：SimdOnnxExt 的命令行入口。
// 目前只有 inspect（M0-03）；run / ocr / sam / bench 随 M1..M5 落地。
// 约定：结果写标准输出，异常写标准错误；0 = 可运行，1 = 有缺口/失败，2 = 参数错误。

const string Usage = """
simdonnxx — SimdOnnxExt 命令行

  inspect -m <model.onnx> [--shape 1,3,640,640] [--no-probe] [--json]
      体检一个 ONNX：引擎编译探测（能不能跑）+ 算子直方图 + 缺口与路线（E/P/H）。
      退出码：0 = 可运行；1 = 有缺口或编译失败；2 = 参数错误。
""";

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    return args[0] switch
    {
        "inspect" => Inspect(args),
        "-h" or "--help" or "help" => Help(),
        _ => Unknown(args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    return 1;
}

int Help()
{
    Console.WriteLine(Usage);
    return 0;
}

int Unknown(string command)
{
    Console.Error.WriteLine($"未知命令：{command}");
    Console.Error.WriteLine(Usage);
    return 2;
}

int Inspect(string[] argv)
{
    string? model = null;
    int[]? shape = null;
    bool probe = true;
    bool json = false;
    for (int i = 1; i < argv.Length; i++)
    {
        switch (argv[i])
        {
            case "-m" or "--model":
                model = Next(argv, ref i);
                break;
            case "--shape":
                shape = ParseShape(Next(argv, ref i));
                break;
            case "--no-probe":
                probe = false;
                break;
            case "--json":
                json = true;
                break;
            default:
                if (!argv[i].StartsWith('-') && model is null) model = argv[i];
                else throw new ArgumentException($"未知参数：{argv[i]}");
                break;
        }
    }
    if (model is null) throw new ArgumentException("inspect 需要 -m <model.onnx>");

    GraphReport report = GraphInspector.Inspect(model, shape, probe);
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(report, Program.JsonOptions));
        return report.IsRunnable ? 0 : 1;
    }

    Console.WriteLine(report.Source);
    Console.WriteLine($"  opset {report.Opset} | producer '{report.Producer}' | nodes {report.NodeCount} | initializers {report.InitializerCount}");
    Console.WriteLine("  in : " + string.Join(", ", report.Inputs.Select(t => $"{t.Name}{t.ShapeText} elem={t.ElementType}")));
    Console.WriteLine("  out: " + string.Join(", ", report.Outputs.Select(t => $"{t.Name}{t.ShapeText}")));
    Console.WriteLine("  ops: " + string.Join(", ", report.Ops.Select(o => o.Op + " " + o.Count + (o.Supported ? "" : " !"))));

    if (report.Issues.Length > 0)
    {
        Console.WriteLine("  issues:");
        foreach (GraphIssue issue in report.Issues)
        {
            string where = issue.Op is "Outputs" or "Input" ? issue.Node : $"{issue.Op} '{issue.Node}'";
            Console.WriteLine($"    [{(issue.Route == FixRoute.None ? "--" : issue.PlanTask ?? issue.Route.ToString())}] {where} {issue.Reason}");
        }
    }
    else
    {
        Console.WriteLine("  issues: none");
    }

    Console.WriteLine(report.Compiles switch
    {
        true => $"  engine: COMPILES (probe [{string.Join(",", report.ProbeShape!)}])",
        false => $"  engine: FAILS (probe [{string.Join(",", report.ProbeShape ?? [])}])\n    {report.Blocker}",
        null => "  engine: not probed（无静态形状：用 --shape 指定）",
    });
    return report.IsRunnable && report.Compiles != false ? 0 : 1;
}

string Next(string[] argv, ref int i)
{
    if (i + 1 >= argv.Length) throw new ArgumentException($"{argv[i]} 缺少取值");
    return argv[++i];
}

int[] ParseShape(string text)
{
    string[] parts = text.Split(['x', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    int[] dims = new int[parts.Length];
    for (int i = 0; i < parts.Length; i++)
        dims[i] = int.TryParse(parts[i], out int v) && v > 0 ? v : throw new ArgumentException($"形状里的 '{parts[i]}' 不是正整数");
    return dims;
}

internal partial class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

