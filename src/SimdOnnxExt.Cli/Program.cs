using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ImageFacade;
using Sdcb.SimdOnnxExt;
using Sdcb.SimdOnnxExt.Imaging;
using Sdcb.SimdOnnxExt.Onnx;
using Sdcb.SimdOnnxExt.Yolo;

// simdonnxx：SimdOnnxExt 的命令行入口。
// 目前只有 inspect（M0-03）；run / ocr / sam / bench 随 M1..M5 落地。
// 约定：结果写标准输出，异常写标准错误；0 = 可运行，1 = 有缺口/失败，2 = 参数错误。

const string Usage = """
simdonnxx — SimdOnnxExt 命令行

  inspect -m <model.onnx> [--shape 1,3,640,640] [--no-probe] [--json]
      体检一个 ONNX：引擎编译探测（能不能跑）+ 算子直方图 + 缺口与路线（E/P/H）。

  run -m <model.onnx> -i <image> [-o <out.jpg>] [--conf 0.25] [--iou 0.45]
      [--backend auto|cpu|vulkan|metal] [--json]
      YOLO 检测：letterbox → 引擎推理 → DFL 解码 + NMS → 把框画到图上并另存。
      默认输出 <image>.detected.<ext>，同时把结果摘要打印到标准输出。

  退出码：0 = 成功；1 = 失败（有缺口/异常）；2 = 参数错误。
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
        "run" => RunCommand(args),
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

int RunCommand(string[] argv)
{
    string? model = null, imagePath = null, outPath = null;
    float conf = 0.25f, iou = 0.45f;
    BackendKind kind = BackendKind.Auto;
    bool json = false;
    for (int i = 1; i < argv.Length; i++)
    {
        switch (argv[i])
        {
            case "-m" or "--model": model = Next(argv, ref i); break;
            case "-i" or "--image": imagePath = Next(argv, ref i); break;
            case "-o" or "--out": outPath = Next(argv, ref i); break;
            case "--conf": conf = float.Parse(Next(argv, ref i), CultureInfo.InvariantCulture); break;
            case "--iou": iou = float.Parse(Next(argv, ref i), CultureInfo.InvariantCulture); break;
            case "--backend": kind = Enum.Parse<BackendKind>(Next(argv, ref i), ignoreCase: true); break;
            case "--json": json = true; break;
            default: throw new ArgumentException($"未知参数：{argv[i]}");
        }
    }
    if (model is null || imagePath is null) throw new ArgumentException("run 需要 -m <model.onnx> 和 -i <image>");

    using ImageEngine engine = ImageHost.CreateEngine();
    using FacadeImage source = engine.Load(imagePath);
    using YoloDetector detector = YoloDetector.Load(model, kind, conf, iou);

    string output = outPath ?? Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(imagePath))!,
        Path.GetFileNameWithoutExtension(imagePath) + ".detected" + Path.GetExtension(imagePath));

    Stopwatch watch = Stopwatch.StartNew();
    YoloDetection[] detections = detector.Detect(source);
    double elapsed = watch.Elapsed.TotalMilliseconds;

    detector.Draw(source, detections);
    string? directory = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    source.Save(output);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            image = Path.GetFullPath(imagePath),
            output = Path.GetFullPath(output),
            backend = detector.Backend.Kind.ToString(),
            device = detector.Backend.Name,
            fallback = detector.FallbackReason,
            elapsedMs = Math.Round(elapsed, 2),
            detections = detections.Select(d => new
            {
                cls = d.ClassId,
                label = d.Label,
                score = Math.Round(d.Score, 4),
                box = new[] { Math.Round(d.Box.X, 1), Math.Round(d.Box.Y, 1), Math.Round(d.Box.Width, 1), Math.Round(d.Box.Height, 1) },
            }),
        }, Program.JsonOptions));
        return 0;
    }

    Console.WriteLine($"{Path.GetFileName(imagePath)} {source.Width}x{source.Height}  ->  {Path.GetFullPath(output)}");
    Console.WriteLine($"  model  : {detector.Model}");
    Console.WriteLine($"  backend: {detector.Backend.Kind} ({detector.Backend.Name})" +
        (detector.FallbackReason is { } reason ? "  [" + reason + "]" : string.Empty));
    Console.WriteLine($"  result : {detections.Length} 个目标，{elapsed:F1} ms");
    foreach (YoloDetection d in detections.OrderByDescending(d => d.Score))
        Console.WriteLine($"    {d.Label,-12} {d.Score:F3}  框 [{d.Box.X:F1}, {d.Box.Y:F1}, {d.Box.Width:F1}x{d.Box.Height:F1}]");
    return 0;
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

