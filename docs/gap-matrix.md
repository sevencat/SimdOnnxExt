# 算子差距矩阵（实测）

统计口径：本机 `onnx 1.23.2` / `onnxruntime 1.31.1`（`F:\code\TinyLabel\train\py314\python`），只读模型文件。
引擎支持集依据 `src/Sdcb.SimdOnnx/Engine/GraphCompiler.cs` 的 `EmitNode` float 分支与 `IntOps` 编译期整数求值集。
**这张表是排期依据：每个缺口必须落 E / P / H 之一，并挂一个 `PLAN.md` 的任务 ID。**

## 1. 引擎侧硬约束（源码依据）

| 约束 | 依据 | 影响 |
|---|---|---|
| 单输出 | `GraphCompiler.Run`：`g.Outputs.Count != 1` 即抛 | seg / RF-DETR / SAM3 无法单次运行取多输出 |
| 先 emit 全部节点、后剪枝 | `Run()` 顺序 emit → `FuseAndPlan` 里才 `Prune` | 尾部不支持算子挡住整模型；`OutputName` 不能绕 |
| 图输入只有 float | 输入 `CValue` 固定 `DType = 1`，`Input` 是 `Span<float>` | SAM3 的 int64 `tokens` 不能直接喂 |
| Softmax 仅最后一维 | `EmitSoftmax`：`axis != rank-1` 抛 | YOLO 新导出的 DFL softmax 直接失败 |
| ConvTranspose 仅 group=1 / stride==kernel / 无 pad | `EmitConvTranspose` | YOLO seg 的 2x2/s2 恰好满足 |
| Resize nearest 仅 asymmetric + floor | `EmitResize` | YOLO 上采样满足 |
| `ExtOp` 只能替换已 lower 的节点 | `EmitNode` 的 default 分支抛"unsupported Op" | 新 ONNX 逻辑算子**只能进引擎** |
| GPU 不逐节点回退 | `Extensions.cs`（ExtOp/PassContext.Target 注释） | 新算子必须同时给 CPU 与 Vulkan/Metal 发射 |

## 2. 引擎当前覆盖的算子

**float 路径**（`EmitNode`）：`Add` `Sub` `Mul` `Div` `Conv` `ConvTranspose`(受限) `MaxPool` `AveragePool` `GlobalAveragePool` `BatchNormalization`(常量) `Relu` `Sigmoid` `Tanh` `LeakyRelu` `HardSigmoid` `Clip` `Erf` `Sqrt` `Pow`(指数=2) `MatMul` `Gemm` `Softmax`(末维) `LayerNormalization`(末维) `ReduceMean`(末维) `Resize`(bilinear / nearest asymmetric+floor) `Transpose` `Reshape` `Squeeze` `Unsqueeze` `Concat` `Split` `Slice` `Pad` `DepthToSpace` `Identity`。

**编译期整数求值**（不产生运行时 kernel）：`Shape` `Size` `Constant` + `Squeeze` `Unsqueeze` `Reshape` `Concat` `Slice` `Gather` `Cast` `Div` `Mod` `Add` `Sub` `Mul` `Neg` `Abs` `Floor` `Ceil` `Min` `Max` `Equal` `Less` `Where` `ConstantOfShape` `Expand` `Tile` `Identity`。
→ 这些名字在"**只有整数张量参与**"时才可用；一旦有 float 输入就落到 float 分支并抛错（例如运行期 `Gather`、`Expand`、`Where`）。

## 3. 逐个模型

| fixture | 输入 / 输出 | 实测关键算子 | 缺口 | 路线 | 任务 |
|---|---|---|---|---|---|
| `yolo11n-opset12.onnx` | `images 1x3x128x128` → `output0 1x84x336`（**128 尺寸小导出，非 640**） | 454 节点：`Mul 97` `Conv 88` `Sigmoid 78` `Constant 72` `Add 25` `Concat 23` `Slice 18` `Div 10` `Shape 9` `Gather 9`(整数) `Reshape 8` `MaxPool 3` `Split 3` `Transpose 4` `MatMul 2` `Softmax 2`(`axis=3`) `Resize 2` `Sub 2` | 无算子缺口 | **H** | M1-02..M1-06 |
| `yolo11n.onnx` / `yolov8n.onnx` / `yolov9t.onnx` | 同上 | 同上，但 DFL 是 `Transpose → [1,16,4,8400]` + `Softmax axis=1` | Softmax 非末维 | **E-01**（或 P 插 Transpose） | M2-04 |
| `yolo11n-seg.onnx` | `output0 1x116x8400` + `output1 1x32x160x160` | 上表 + `ConvTranspose 1` | 双输出 | **E-02**（或 P 合并成一个张量） | M2-01 |
| `yolo11n-pose.onnx` | `output0 1x56x8400` | 同 yolo11n（`Softmax axis=1`） | Softmax 非末维 | **E-01** | M2-02 |
| `yolov10n.onnx` / `yolo26n.onnx` | `output0 1x300x6` | 上表 + `TopK 2` `GatherElements 2` `ConstantOfShape 2` `Expand 2` `Tile 2` `Flatten 2` `ReduceMax 1` `Mod 1` | 端到端头（图内 top-k） | **P** 剪尾自写 top-k，或 **E-03** | M2-03 |
| `rfdetr-small.onnx`（1537 节点 / 354 权重） | `input 1x3x512x512` → `dets 1x300x4` + `labels 1x300x91` | `Cast 36` `Expand 9` `Where 8` `ConstantOfShape 7` `Equal 6` `ReduceSum 4` `Gather 4` `Sin 4` `Cos 4` `Not 3` `Exp 3` `GridSample 3` `Tile 2` `GatherElements 2` `Greater 2` `Less 1` `And 1` `ReduceMax 1` `TopK 1`（另 `MatMul 140` `LayerNormalization 49` `Softmax 18` `Erf 12` `Conv 9` `Gemm 3`） | 19 类算子 + 双输出（DINO 式 deformable decoder 的 `GridSample`/`TopK`/`Sin/Cos`） | **E-03 + E-02**，解码走 **H** | M4-01..M4-04 |
| `rfdetr-small` **经 onnxslim**（P 路线，`tools/prep/slim-model.py`） | 同上 | **821 节点**；`Cast/Equal/Not/And/Greater/Less/ConstantOfShape` 被折叠成常量；数值等价（`dets` 1.9e-5 / `labels` 4.0e-5） | 只剩 **5 类 / 10 节点**：`ReduceMax 1` `TopK 1` `GatherElements 2` `GridSample 3` `ReduceSum 3`（另需引擎补 `Sin/Cos/Exp/Gather/Expand/Tile/Where`）。**引擎实测首个失败**：`Tensor '/transformer/Cast_2_output_0': raw dtype 9`（**bool 张量**） | **P + E-03** | M4-01（已验证） |
| SAM3（`wkentaro/sam3-onnx` v0.6.0） | image encoder / language encoder（`tokens int64 1x32`）/ decoder | 待下载 fixture 后补本行 | 已知：int64 图输入、多输出；注意力算子待实测 | **E-04/P + E-02**，tokenizer 与后处理走 **H** | M5-01..M5-06 |
| PP-OCRv6 tiny det（`Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny`） | `x[N,3,H,W]` 动态 → `[N,1,h,w]` | opset 14，242 节点；`Conv/Mul/Add/Relu/Div/Erf/HardSigmoid/GlobalAveragePool/Resize/ReduceMean/ConvTranspose/MaxPool/Sigmoid` | 无算子缺口（**COMPILES** @1,3,960,960） | **H**（DB 后处理） | M3-01 |
| PP-OCRv6 tiny rec | `x[N,3,48,W]` 动态 → `[N,T,6906]` | opset 11，217 节点；输出**已带 Softmax**（分值即概率） | 无算子缺口（**COMPILES** @1,3,48,320） | **H**（CTC + 字典） | M3-02 |
| PP-LCNet 文本行方向 cls | `x[N,3,80,160]` → `[N,2]` | opset 7，116 节点；`Softmax` 在 `[N,2]` 上（末维） | 无算子缺口（**COMPILES** @1,3,80,160） | **H**（180° 判定的接线） | M3-03 |
| PP-OCRv6（SimdPaddleOCR） | det `1x3x960x960` / rec `1x3x48xW` / cls | 引擎已由 SimdPaddleOCR 跑通；解码有 `CtcProjection` / `CtcGreedyDecoder` 公共 API | 无算子缺口 | **H** | M3-01..M3-05 |

## 4. 复现命令（`simdonnxx inspect`）

```powershell
# 引擎编译探测 + 算子直方图 + 缺口路线（M0-03 起可用）
dotnet run --project src\OnnxUtil.Cli -- inspect -m F:\code\SimdYolo\models\onnx\yolo11n.onnx
dotnet run --project src\OnnxUtil.Cli -- inspect -m F:\code\SimdYolo\models\onnx\yolo11n-opset12.onnx   # COMPILES
dotnet run --project src\OnnxUtil.Cli -- inspect -m F:\code\TinyLabel\train\testrftedr\runs\rfdetr-small.onnx --no-probe
```

`inspect` 的判定顺序：**先让引擎编译**（唯一可靠的"能不能跑"，报错会点名节点），再用头部扫描补
引擎公共面没有的信息（图 I/O 形状、节点属性）来解释"缺什么、走 E/P/H 哪条"。
下表里的算子统计是同一份事实的原始证据。

```powershell
# 只看算子直方图（无 dotnet 环境时的兜底）
$py = 'F:\code\TinyLabel\train\py314\python\python.exe'
$code = 'import onnx,collections,sys;m=onnx.load(sys.argv[1],load_external_data=False);print(len(m.graph.node));print(dict(sorted(collections.Counter(n.op_type for n in m.graph.node).items(),key=lambda kv:-kv[1])))'
$code | & $py - F:\code\SimdYolo\models\onnx\yolo11n.onnx
```

## 5. 实测异常（待查）

| 日期 | 现象 | 数据 | 归属 |
|---|---|---|---|
| 2026-11-10 | YOLO11n(128) 在 Vulkan 上**能跑、输出有限**，但与 CPU 不一致 | 最大绝对偏差 **3.502E-001 @#967**（`[1,84,336]` 的第 2 行，DFL 框距离段）；CPU vs ORT 只有 5.493E-004 | M1-05 排查 |

怀疑方向：4-D DFL `Softmax`（`[1,336,4,16]`）或 `MatMul` 的 fp16 路径。引擎自己的设备验证覆盖
OCR / GcdrNet，YOLO 的这条路径没有设备记录；`tests/OnnxUtil.Tests/PipelineSmokeTests.GpuBackend_MatchesCpu`
以 `Skip` 保留该断言，定位后去掉 Skip。在此之前 GPU 只保证"能跑"（`GpuBackend_RunsAndProducesFiniteOutput`）。

## 6. 结论

1. **YOLO（旧导出）+ OCR 现在就能一键跑**——缺口全在主机侧，而其中 letterbox、张量化、NMS、标注绘制由
   `sevencat/ImageFacade` 提供（`Letterbox.Apply` / `Tensorizer.ToTensor` / `Nms.Suppress` / `DetectionDrawing`），
   本仓库只需写 DFL 解码、DETR top-k、掩码上采样、OCR 的 DB 后处理与 CTC 解码。
2. **YOLO 新导出的 DFL** 只差一个"Softmax 任意轴"，是引擎里性价比最高的改动（E-01）。
3. **seg / RF-DETR / SAM3 的共同前置是多输出**（E-02）；RF-DETR 还额外要 E-03 那批判算子，SAM3 还要 int64 输入改造（E-04）。
4. 所有"剪掉尾部"的诉求都要 **P**（离线 ONNX 读写），因为引擎先 emit 后剪枝——这也是 `tools/OnnxUtil.ModelPrep` 存在的理由。
