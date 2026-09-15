using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CdsHelper.Game.Engine.Disev;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견 대본 흐름도 — <see cref="DisevFlow.Graph"/> 을 왼쪽에서 오른쪽으로 편 그림이다.
/// </summary>
/// <remarks>
/// 분기는 파란 마름모, 묶음은 초록 네모, 멎는 묶음은 갈색 네모다. 칸(열)은 들머리에서 가장 긴
/// 앞으로 가는 길의 걸음 수로 잡고, 한 열 안에서는 앞 노드 높이에 맞춘다.
/// <b>분기의 아니오(다음 줄)는 마름모 오른쪽 꼭짓점에서 같은 줄로, 예(뛰는 쪽)는 아래 꼭짓점에서
/// 내려 마름모보다 밑 줄로</b> 간다. 예 노드는 가는 길에 걸리는 사이 열 노드 밑으로 민다.
/// 뒤로 뛰는 화살은 두 노드 밑으로 돌려 그린다.
/// </remarks>
internal static class DisevFlowView
{
    private const double BoxW = 280, DiamondW = 190, DiamondH = 90, OutsideW = 190, OutsideH = 44;
    private const double LineH = 18, Pad = 8, GapX = 80, GapY = 28, Edge0 = 16;
    private const int MaxLines = 10, MaxChars = 34;

    private static readonly Brush LineBrush = Solid(0x44, 0x72, 0xC4);
    private static readonly Brush StrokeBrush = Solid(0x1F, 0x38, 0x64);
    private static readonly Brush DecisionFill = Solid(0x44, 0x72, 0xC4);
    private static readonly Brush BlockFill = Solid(0x54, 0x82, 0x35);
    private static readonly Brush EndFill = Solid(0x9E, 0x48, 0x0E);
    private static readonly Brush OutsideFill = Solid(0x7F, 0x7F, 0x7F);

    private static SolidColorBrush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>흐름도를 그린다.</summary>
    /// <param name="pick">노드를 누르면 그 첫 명령 자리를 넘긴다.</param>
    public static FrameworkElement Build(DisevFlow.Graph graph, Action<int> pick)
    {
        var canvas = new Canvas { Background = Brushes.White };
        var nodes = graph.Nodes;
        int n = nodes.Count;
        if (n == 0) return canvas;

        var size = nodes.Select(SizeOf).ToArray();

        // 열 — 앞으로 가는 화살(To > From)만 따라 가장 긴 걸음 수.
        var col = new int[n];
        var outs = graph.Edges.ToLookup(e => e.From);
        for (int i = 0; i < n; i++)
            foreach (var e in outs[i])
                if (e.To > i) col[e.To] = Math.Max(col[e.To], col[i] + 1);

        int columns = col.Max() + 1;
        var colX = new double[columns];
        double x0 = Edge0;
        for (int c = 0; c < columns; c++)
        {
            colX[c] = x0;
            double width = Enumerable.Range(0, n).Where(i => col[i] == c).Select(i => size[i].Width).DefaultIfEmpty(0).Max();
            x0 += width + GapX;
        }

        // 줄 — 열마다 앞 노드 가운데 높이에 맞추고, 겹치면 밑으로 민다.
        var y = new double[n];

        bool IsDown(DisevFlow.Edge e) => e.Jump && nodes[e.From].Kind == DisevFlow.NodeKind.Decision;

        // 예 노드의 윗변 — 마름모 밑으로 내리고, 가로 화살이 사이 열 노드를 꿰지 않게 더 민다.
        // 열은 왼쪽부터 놓으므로 사이 열은 이미 자리가 잡혀 있다.
        double BelowDecision(int decision, int target)
        {
            double want = y[decision] + size[decision].Height + GapY;
            for (bool moved = true; moved;)
            {
                moved = false;
                double line = want + size[target].Height / 2;
                for (int k = 0; k < n; k++)
                {
                    if (col[k] <= col[decision] || col[k] >= col[target]) continue;
                    if (line < y[k] - GapY / 2 || line > y[k] + size[k].Height + GapY / 2) continue;
                    want = y[k] + size[k].Height + GapY - size[target].Height / 2;
                    moved = true;
                }
            }
            return want;
        }
        var incoming = graph.Edges.Where(e => e.From < e.To).ToLookup(e => e.To);
        for (int c = 0; c < columns; c++)
        {
            var order = Enumerable.Range(0, n).Where(i => col[i] == c)
                .Select(i =>
                {
                    var from = incoming[i].OrderBy(e => e.From).FirstOrDefault();
                    bool has = incoming[i].Any();
                    bool down = has && IsDown(from);
                    double want = !has ? Edge0
                        : down ? BelowDecision(from.From, i)
                        : y[from.From] + size[from.From].Height / 2 - size[i].Height / 2;
                    // 아니오가 먼저(같은 줄), 예는 맨 뒤(밑 줄)다.
                    int rank = !has ? 1 : down ? 2 : from.Label.Length > 0 ? 0 : 1;
                    return (Node: i, Want: want, Label: rank);
                })
                .OrderBy(t => t.Want).ThenBy(t => t.Label).ThenBy(t => t.Node);

            double free = Edge0;
            foreach (var (node, want, _) in order)
            {
                y[node] = Math.Max(Math.Max(want, Edge0), free);
                free = y[node] + size[node].Height + GapY;
            }
        }

        double right = Enumerable.Range(0, n).Max(i => colX[col[i]] + size[i].Width) + Edge0;
        double bottom = Enumerable.Range(0, n).Max(i => y[i] + size[i].Height) + Edge0;

        // 화살 먼저 — 노드가 그 위를 덮는다.
        int back = 0;
        foreach (var e in graph.Edges)
        {
            // 예는 마름모 아래 꼭짓점, 그 밖은 오른쪽 가운데에서 나간다.
            bool down = IsDown(e);
            double sx = down ? colX[col[e.From]] + size[e.From].Width / 2 : colX[col[e.From]] + size[e.From].Width;
            double sy = down ? y[e.From] + size[e.From].Height : y[e.From] + size[e.From].Height / 2;
            double tx = colX[col[e.To]], ty = y[e.To] + size[e.To].Height / 2;
            var line = new Polyline { Stroke = LineBrush, StrokeThickness = 1.2 };
            double labelX, labelY = ty - 19;

            if (down)
            {
                labelX = sx + 6;
                labelY = sy + 2;
                if (col[e.To] > col[e.From] && ty >= sy + 14)
                    line.Points = [new(sx, sy), new(sx, ty), new(tx, ty)];
                else
                {
                    // 과녁이 꼭짓점보다 높거나 뒤에 있으면 한 번 내려 섰다가 돌아 들어간다.
                    double by = col[e.To] > col[e.From]
                        ? sy + 14
                        : Math.Max(sy, y[e.To] + size[e.To].Height) + 14 + back++ * 8;
                    double lx = tx - (col[e.To] > col[e.From] ? GapX / 2 : 14);
                    line.Points = [new(sx, sy), new(sx, by), new(lx, by), new(lx, ty), new(tx, ty)];
                    bottom = Math.Max(bottom, by + Edge0);
                }
            }
            else if (col[e.To] > col[e.From])
            {
                double mx = tx - GapX / 2;
                line.Points = [new(sx, sy), new(mx, sy), new(mx, ty), new(tx, ty)];
                labelX = mx + 4;
            }
            else
            {
                double by = Math.Max(y[e.From] + size[e.From].Height, y[e.To] + size[e.To].Height) + 14 + back++ * 8;
                double rx = sx + 14, lx = tx - 14;
                line.Points = [new(sx, sy), new(rx, sy), new(rx, by), new(lx, by), new(lx, ty), new(tx, ty)];
                labelX = lx - 34;
                bottom = Math.Max(bottom, by + Edge0);
            }

            canvas.Children.Add(line);
            canvas.Children.Add(new Polygon
            {
                Fill = LineBrush,
                Points = [new(tx, ty), new(tx - 9, ty - 4.5), new(tx - 9, ty + 4.5)],
            });

            if (e.Label.Length > 0)
            {
                var label = new TextBlock { Text = e.Label, Foreground = StrokeBrush, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(label, labelX);
                Canvas.SetTop(label, labelY);
                canvas.Children.Add(label);
            }
        }

        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            var element = Draw(node, size[i]);
            element.Cursor = Cursors.Hand;
            element.ToolTip = node.Ops.Count > 0 ? string.Join(Environment.NewLine, node.Ops.Select(o => o.Text)) : node.Title;
            int offset = node.Offset;
            element.MouseLeftButtonUp += (_, args) => { args.Handled = true; pick(offset); };

            Canvas.SetLeft(element, colX[col[i]]);
            Canvas.SetTop(element, y[i]);
            canvas.Children.Add(element);
        }

        canvas.Width = right;
        canvas.Height = bottom;
        return canvas;
    }

    private static Size SizeOf(DisevFlow.Node node) => node.Kind switch
    {
        DisevFlow.NodeKind.Decision => new Size(DiamondW, DiamondH),
        DisevFlow.NodeKind.Outside => new Size(OutsideW, OutsideH),
        _ => new Size(BoxW, Pad * 2 + Math.Min(node.Ops.Count, MaxLines) * LineH),
    };

    private static FrameworkElement Draw(DisevFlow.Node node, Size size)
    {
        if (node.Kind == DisevFlow.NodeKind.Decision)
        {
            var grid = new Grid { Width = size.Width, Height = size.Height, Background = Brushes.Transparent };
            grid.Children.Add(new Polygon
            {
                Fill = DecisionFill,
                Stroke = StrokeBrush,
                StrokeThickness = 1,
                Points = [new(0, size.Height / 2), new(size.Width / 2, 0), new(size.Width, size.Height / 2), new(size.Width / 2, size.Height)],
            });
            grid.Children.Add(new TextBlock
            {
                Text = node.Title,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = size.Width * 0.62,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return grid;
        }

        var lines = new StackPanel { Margin = new Thickness(Pad, Pad - 1, Pad, 0) };
        if (node.Kind == DisevFlow.NodeKind.Outside)
            lines.Children.Add(Text(node.Title));
        else
        {
            bool cut = node.Ops.Count > MaxLines;
            foreach (var op in node.Ops.Take(cut ? MaxLines - 1 : MaxLines)) lines.Children.Add(Text(Short(op.Text)));
            if (cut) lines.Children.Add(Text($"… 외 {node.Ops.Count - (MaxLines - 1)}줄"));
        }

        return new Border
        {
            Width = size.Width,
            Height = size.Height,
            Background = node.Kind switch
            {
                DisevFlow.NodeKind.End => EndFill,
                DisevFlow.NodeKind.Outside => OutsideFill,
                _ => BlockFill,
            },
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            Child = lines,
        };
    }

    private static TextBlock Text(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        Height = LineH,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static string Short(string text) => text.Length > MaxChars ? text[..(MaxChars - 1)] + "…" : text;
}
