using ImageFacade;

namespace OnnxUtil.Ocr;

/// <summary>
/// DB（Differentiable Binarization）检测后处理：概率图 → 文本框。
/// 简化版 PaddleOCR <c>DBPostProcess</c>：连通域求外接框（轴对齐）+ 按面积/周长外扩（unclip），
/// 不做 min-area-rect（竖排/倾斜文本仍能框住，只是框是正的）。
/// </summary>
public static class DbPostprocess
{
    /// <summary>一个检测框（概率图坐标系，调用方负责缩放回原图）。</summary>
    public readonly record struct DetBox(RectF Rect, float Score);

    public static List<DetBox> Detect(ReadOnlySpan<float> probability, int width, int height,
        float threshold = 0.3f, float boxThreshold = 0.6f, float unclipRatio = 1.6f, int minSize = 3)
    {
        if (probability.Length < (long)width * height)
            throw new ArgumentException($"概率图长度 {probability.Length} 小于 {width}x{height}", nameof(probability));

        bool[] visited = new bool[width * height];
        Queue<int> queue = new();
        List<DetBox> boxes = [];

        for (int start = 0; start < visited.Length; start++)
        {
            if (visited[start] || probability[start] <= threshold) continue;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            double sum = 0;
            int count = 0;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % width, y = index / width;
                sum += probability[index];
                count++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if ((uint)ny >= (uint)height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        if ((uint)nx >= (uint)width) continue;
                        int next = ny * width + nx;
                        if (visited[next] || probability[next] <= threshold) continue;
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }

            float boxWidth = maxX - minX + 1, boxHeight = maxY - minY + 1;
            float score = (float)(sum / count);
            if (boxWidth < minSize || boxHeight < minSize || score < boxThreshold) continue;

            // unclip：distance = area * ratio / perimeter（PaddleOCR 用 pyclipper 的多边形外扩）
            double area = boxWidth * boxHeight;
            double perimeter = 2 * (boxWidth + boxHeight);
            float grow = (float)(area * unclipRatio / perimeter);
            boxes.Add(new DetBox(new RectF(minX - grow, minY - grow, boxWidth + 2 * grow, boxHeight + 2 * grow), score));
        }

        // 阅读顺序：先上后下、先左后右
        boxes.Sort((a, b) =>
        {
            float dy = a.Rect.Y - b.Rect.Y;
            if (MathF.Abs(dy) > MathF.Min(a.Rect.Height, b.Rect.Height) * 0.5f) return dy < 0 ? -1 : 1;
            return a.Rect.X.CompareTo(b.Rect.X);
        });
        return boxes;
    }

    /// <summary>把概率图坐标系的框缩放到原图，并对齐到像素边界内。</summary>
    public static RectF Scale(RectF box, float scaleX, float scaleY, int imageWidth, int imageHeight)
    {
        float x = MathF.Max(0, box.X * scaleX);
        float y = MathF.Max(0, box.Y * scaleY);
        float right = MathF.Min(imageWidth, (box.X + box.Width) * scaleX);
        float bottom = MathF.Min(imageHeight, (box.Y + box.Height) * scaleY);
        return new RectF(x, y, MathF.Max(1, right - x), MathF.Max(1, bottom - y));
    }
}
