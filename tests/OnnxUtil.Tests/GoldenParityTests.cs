using System.Security.Cryptography;
using Sdcb.SimdOnnx.Engine;
using Sdcb.SimdOnnx.Onnx;

namespace OnnxUtil.Tests;

/// <summary>
/// M0-04 / M1-02 的数值基线：引擎 CPU 前向 vs onnxruntime 录制的金标准。
/// 只读 <c>tests/Fixtures</c>（由 <c>tools/fixtures/record.py</c> 录制），CI 不依赖 ORT。
/// </summary>
public class GoldenParityTests
{
    // 实测（yolo11n-opset12，128x128）：最大绝对偏差 5.493e-4（Conv 累加顺序不同 + DFL softmax）
    private const float AbsoluteTolerance = 1e-3f;

    [SkippableFact]
    public void Yolo11n_OpSet12_MatchesOnnxRuntime()
    {
        string? dir = GoldenFixture.FindDirectory("yolo/yolo11n-opset12");
        Skip.If(dir is null, "没有 tests/Fixtures/yolo/yolo11n-opset12：先跑 tools/fixtures/record.py");

        GoldenFixture golden = GoldenFixture.Load(dir!);
        string? modelPath = golden.ResolveModel("yolo/yolo11n-opset12.onnx");
        Skip.If(modelPath is null, "找不到模型文件");

        Assert.Equal(golden.ModelSha256, Sha256(modelPath!));   // 模型没被换掉

        float[] input = golden.ReadInput();
        OnnxModel model = OnnxModel.Load(modelPath!);
        using InferenceSession session = CompiledModel.Compile(model, golden.Input.Shape).CreateSession();
        Assert.Equal(input.Length, session.Input.Length);
        input.CopyTo(session.Input);
        session.Run();

        float[] actual = session.Output.ToArray();
        Assert.Equal((int)GoldenFixture.ElementCount(golden.Outputs[0].Shape), actual.Length);

        (float maxAbs, int at) = Compare(golden.ReadOutput(0), actual);
        Assert.True(maxAbs < AbsoluteTolerance,
            $"输出与 ORT 偏差 {maxAbs:E3}（阈值 {AbsoluteTolerance:E1}），最大偏差位置 #{at}");
    }

    private static (float maxAbs, int at) Compare(float[] expected, float[] actual)
    {
        if (expected.Length != actual.Length)
            throw new InvalidDataException($"长度不同：期望 {expected.Length}，实际 {actual.Length}");
        float max = 0f;
        int at = -1;
        for (int i = 0; i < expected.Length; i++)
        {
            float diff = MathF.Abs(expected[i] - actual[i]);
            if (diff > max)
            {
                max = diff;
                at = i;
            }
        }
        return (max, at);
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
