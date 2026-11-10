using ImageFacade;
using ImageFacade.Pipeline;

namespace OnnxUtil.Yolo;

/// <summary>
/// YOLOv8/v9/v11/v26 检测头解码：输出是 <c>[1, 4 + nc, anchors]</c>，
/// 前 4 行是**已经在模型输入像素坐标系里**的 cx,cy,w,h（图里已做 DFL + sigmoid），
/// 后面每行是一个类别的分数；NMS 在主机侧做。
/// </summary>
public static class YoloPostprocess
{
    /// <summary>
    /// 解码 + 阈值 + NMS，返回**源图坐标**下的检测框。
    /// </summary>
    /// <param name="output">引擎输出（连续 float，行主序 [channels, anchors]）。</param>
    /// <param name="channels">4 + 类别数。</param>
    /// <param name="anchors">anchor 数。</param>
    /// <param name="classCount">类别数。</param>
    /// <param name="confidence">分数阈值（含）。</param>
    /// <param name="iouThreshold">NMS 的 IoU 阈值。</param>
    /// <param name="info">letterbox 几何，用来把模型坐标还原到源图。</param>
    public static Detection[] Detect(ReadOnlySpan<float> output, int channels, int anchors, int classCount,
        float confidence, float iouThreshold, in LetterboxInfo info)
    {
        if (output.Length < (long)channels * anchors)
            throw new ArgumentException($"输出长度 {output.Length} 小于 {channels}x{anchors}", nameof(output));
        int classes = Math.Min(classCount, channels - 4);

        List<Detection> candidates = [];
        for (int a = 0; a < anchors; a++)
        {
            int best = -1;
            float bestScore = confidence;
            for (int c = 0; c < classes; c++)
            {
                float score = output[(4 + c) * anchors + a];
                if (score >= bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            if (best < 0) continue;

            float cx = output[a];
            float cy = output[anchors + a];
            float w = output[2 * anchors + a];
            float h = output[3 * anchors + a];
            RectF box = info.ToSource(new RectF(cx - w / 2f, cy - h / 2f, w, h));
            candidates.Add(new Detection(box, null, bestScore) { ClassId = best });
        }

        if (candidates.Count == 0) return [];
        Detection[] detections = [.. candidates];
        int kept = Nms.Suppress(detections, iouThreshold, classAware: true);
        return kept == detections.Length ? detections : detections[..kept];
    }
}
