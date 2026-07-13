using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;

namespace DwgAutoResize;

internal static class DrawingModifier
{
    internal sealed class TargetSize
    {
        public double Width { get; init; }
        public double Height { get; init; }
        public double Depth { get; init; }

        public override string ToString()
        {
            return $"{Width:0.###}x{Height:0.###}x{Depth:0.###}";
        }
    }

    internal static void Apply(
        PartDetector.DetectedParts parts,
        TargetSize target
    )
    {
        ValidateTarget(target);

        // 지금 정상 동작한 기존 방식 그대로 치수 레이어만 제거한다.
        RemoveDimensionLayerEntities(parts.Document);

        double widthDelta =
            target.Width - parts.CurrentWidth;

        double heightDelta =
            target.Height - parts.CurrentHeight;

        Console.WriteLine();
        Console.WriteLine("===== 오른쪽 패널 수정 =====");
        Console.WriteLine("맨 왼쪽 기준 조립도는 수정하지 않습니다.");
        Console.WriteLine(
            $"기준 현재 크기: " +
            $"{parts.CurrentWidth:F3} x " +
            $"{parts.CurrentHeight:F3}"
        );
        Console.WriteLine(
            $"목표 크기: {target.Width:F3} x {target.Height:F3}"
        );
        Console.WriteLine(
            $"가로 변화량: {widthDelta:+0.###;-0.###;0}"
        );
        Console.WriteLine(
            $"세로 변화량: {heightDelta:+0.###;-0.###;0}"
        );

        // 각 패널의 원래 정보를 기준으로 먼저 크기 변경
        foreach (PartDetector.RightPanel panel in parts.RightPanels)
        {
            ResizePanelOutline(
                panel.Outline,
                panel.OriginalOutlineInfo,
                widthDelta,
                heightDelta
            );

            MovePanelContents(
                panel.Contents,
                panel.OriginalOutlineInfo,
                widthDelta,
                heightDelta
            );
        }

        // 크기 변경 후 패널끼리 원래 X 간격 유지
        ReLayoutRightPanels(parts.RightPanels);

        Console.WriteLine(
            $"오른쪽 패널 {parts.RightPanels.Count}개 수정 완료."
        );
    }

    internal static void SaveAsNewDwg(
        CadDocument document,
        string outputPath
    )
    {
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using DwgWriter writer = new(
            outputPath,
            document
        );

        writer.Write();
    }

    private static void ResizePanelOutline(
        LwPolyline outline,
        PartDetector.ShapeInfo original,
        double widthDelta,
        double heightDelta
    )
    {
        double halfWidth = widthDelta / 2.0;
        double halfHeight = heightDelta / 2.0;

        foreach (var vertex in outline.Vertices)
        {
            double x = vertex.Location.X;
            double y = vertex.Location.Y;

            if (Math.Abs(x - original.CenterX) > 0.000001)
            {
                x += x < original.CenterX
                    ? -halfWidth
                    : halfWidth;
            }

            if (Math.Abs(y - original.CenterY) > 0.000001)
            {
                y += y < original.CenterY
                    ? -halfHeight
                    : halfHeight;
            }

            vertex.Location = new XY(x, y);
        }
    }

    private static void MovePanelContents(
    IEnumerable<Entity> contents,
    PartDetector.ShapeInfo panel,
    double widthDelta,
    double heightDelta
    )
    {
        double halfWidth = widthDelta / 2.0;
        double halfHeight = heightDelta / 2.0;

        foreach (Entity entity in contents)
        {
            PartDetector.ShapeInfo? info =
                TryGetShapeInfo(entity);

            if (info == null)
            {
                continue;
            }

            /*
            * 중앙 볼트 구멍은 크기 변경 중에는 이동하지 않는다.
            *
            * 이후 ReLayoutRightPanels()에서 패널 전체가 이동할 때
            * 볼트 구멍도 패널과 함께 같은 거리만큼 이동한다.
            */
            if (IsLayer(entity, "볼트 구멍"))
            {
                continue;
            }

            double dx = 0.0;
            double dy = 0.0;

            /*
            * 패널 중앙에 거의 붙어 있는 객체는 고정한다.
            * 작은 좌표 오차 때문에 잘못 분류되지 않도록 허용 범위를 둔다.
            */
            double centerToleranceX =
                Math.Max(panel.Width * 0.03, 1.0);

            double centerToleranceY =
                Math.Max(panel.Height * 0.03, 1.0);

            double offsetX =
                info.CenterX - panel.CenterX;

            double offsetY =
                info.CenterY - panel.CenterY;

            if (Math.Abs(offsetX) > centerToleranceX)
            {
                dx = offsetX < 0
                    ? -halfWidth
                    : halfWidth;
            }

            if (Math.Abs(offsetY) > centerToleranceY)
            {
                dy = offsetY < 0
                    ? -halfHeight
                    : halfHeight;
            }

            MoveEntity(entity, dx, dy);
        }
    }

    private static bool IsLayer(
    Entity entity,
    string layerName
    )
    {
        return string.Equals(
            entity.Layer?.Name?.Trim(),
            layerName,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static void ReLayoutRightPanels(
        IReadOnlyList<PartDetector.RightPanel> panels
    )
    {
        if (panels.Count <= 1)
        {
            return;
        }

        for (int i = 1; i < panels.Count; i++)
        {
            PartDetector.RightPanel previous = panels[i - 1];
            PartDetector.RightPanel current = panels[i];

            PartDetector.ShapeInfo previousInfo =
                PartDetector.CreateShapeInfo(previous.Outline);

            PartDetector.ShapeInfo currentInfo =
                PartDetector.CreateShapeInfo(current.Outline);

            double desiredMinX =
                previousInfo.MaxX +
                current.GapBefore;

            double dx =
                desiredMinX -
                currentInfo.MinX;

            MovePanelGroup(current, dx, 0.0);
        }
    }

    private static void MovePanelGroup(
        PartDetector.RightPanel panel,
        double dx,
        double dy
    )
    {
        MoveEntity(panel.Outline, dx, dy);

        foreach (Entity entity in panel.Contents)
        {
            MoveEntity(entity, dx, dy);
        }
    }

    private static void MoveEntity(
        Entity entity,
        double dx,
        double dy
    )
    {
        if (Math.Abs(dx) < 0.000001 &&
            Math.Abs(dy) < 0.000001)
        {
            return;
        }

        switch (entity)
        {
            case LwPolyline polyline:
                foreach (var vertex in polyline.Vertices)
                {
                    vertex.Location = new XY(
                        vertex.Location.X + dx,
                        vertex.Location.Y + dy
                    );
                }
                break;

            case Circle circle:
                circle.Center = new XYZ(
                    circle.Center.X + dx,
                    circle.Center.Y + dy,
                    circle.Center.Z
                );
                break;

            case Line line:
                line.StartPoint = new XYZ(
                    line.StartPoint.X + dx,
                    line.StartPoint.Y + dy,
                    line.StartPoint.Z
                );

                line.EndPoint = new XYZ(
                    line.EndPoint.X + dx,
                    line.EndPoint.Y + dy,
                    line.EndPoint.Z
                );
                break;

            default:
                // TEXT는 사용자가 무시해도 된다고 했으므로 건드리지 않는다.
                break;
        }
    }

    private static PartDetector.ShapeInfo? TryGetShapeInfo(
        Entity entity
    )
    {
        try
        {
            return PartDetector.CreateShapeInfo(entity);
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveDimensionLayerEntities(
        CadDocument document
    )
    {
        List<Entity> dimensions = document.Entities
            .Where(entity =>
                string.Equals(
                    entity.Layer?.Name?.Trim(),
                    "치수",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();

        foreach (Entity entity in dimensions)
        {
            document.Entities.Remove(entity);
        }

        Console.WriteLine(
            $"치수 레이어 객체 제거: {dimensions.Count}개"
        );
    }

    private static void ValidateTarget(
        TargetSize target
    )
    {
        if (target.Width <= 0 ||
            target.Height <= 0 ||
            target.Depth <= 0)
        {
            throw new ArgumentException(
                "가로, 세로, 두께는 모두 0보다 커야 합니다."
            );
        }
    }
}