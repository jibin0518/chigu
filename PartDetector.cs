using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;

namespace DwgAutoResize;

internal static class PartDetector
{
    internal sealed class ShapeInfo
    {
        public required Entity Entity { get; init; }
        public required string EntityType { get; init; }
        public required string LayerName { get; init; }

        public double MinX { get; init; }
        public double MinY { get; init; }
        public double MaxX { get; init; }
        public double MaxY { get; init; }

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double Area => Width * Height;
        public double CenterX => (MinX + MaxX) / 2.0;
        public double CenterY => (MinY + MaxY) / 2.0;

        public override string ToString()
        {
            return
                $"종류={EntityType}, Layer={LayerName}, Handle={Entity.Handle}, " +
                $"크기={Width:F3} x {Height:F3}, " +
                $"중심=({CenterX:F3}, {CenterY:F3}), " +
                $"범위=({MinX:F3}, {MinY:F3}) ~ ({MaxX:F3}, {MaxY:F3})";
        }
    }

    internal sealed class RightPanel
    {
        public required LwPolyline Outline { get; init; }
        public required ShapeInfo OriginalOutlineInfo { get; init; }

        /// <summary>
        /// 패널 외곽 안에 들어 있는 원, 폴리라인 등.
        /// 외곽 자체와 치수/문자는 제외한다.
        /// </summary>
        public required List<Entity> Contents { get; init; }

        /// <summary>
        /// 바로 앞 패널과의 원래 X 간격.
        /// 첫 패널은 0.
        /// </summary>
        public double GapBefore { get; init; }
    }

    internal sealed class DetectedParts
    {
        public required CadDocument Document { get; init; }

        /// <summary>
        /// 맨 왼쪽 조립도 중앙의 큰 블록.
        /// 크기 변화량 계산 기준이며 실제 수정은 하지 않는다.
        /// </summary>
        public required LwPolyline ReferenceFront { get; init; }
        public required ShapeInfo ReferenceFrontInfo { get; init; }

        public required LwPolyline ReferenceTop { get; init; }
        public required LwPolyline ReferenceBottom { get; init; }
        public required LwPolyline ReferenceLeft { get; init; }
        public required LwPolyline ReferenceRight { get; init; }

        /// <summary>
        /// 기준 조립도 오른쪽에 있는 독립 패널들.
        /// 이 패널들만 수정한다.
        /// </summary>
        public required List<RightPanel> RightPanels { get; init; }

        // 표시 치수 보정
        public double CurrentWidth => ReferenceFrontInfo.Width + 0.7;
        public double CurrentHeight => ReferenceFrontInfo.Height + 0.7;

        public double CurrentDepth
        {
            get
            {
                double[] values =
                [
                    ShortSide(ReferenceTop),
                    ShortSide(ReferenceBottom),
                    ShortSide(ReferenceLeft),
                    ShortSide(ReferenceRight)
                ];

                Array.Sort(values);
                return ((values[1] + values[2]) / 2.0) + 15.0;
            }
        }

        private static double ShortSide(Entity entity)
        {
            ShapeInfo info = CreateShapeInfo(entity);
            return Math.Min(info.Width, info.Height);
        }
    }

    internal static DetectedParts Analyze(CadDocument document)
    {
        List<LwPolyline> fixturePolylines = document.Entities
            .OfType<LwPolyline>()
            .Where(p => p.IsClosed && IsLayer(p, "치구"))
            .ToList();

        if (fixturePolylines.Count < 5)
        {
            throw new InvalidOperationException(
                "'치구' 레이어의 폐합 LWPOLYLINE이 부족합니다."
            );
        }

        List<(LwPolyline Shape, ShapeInfo Info)> all =
            fixturePolylines
                .Select(p => (p, CreateShapeInfo(p)))
                .ToList();

        double maximumArea = all.Max(x => x.Info.Area);

        // 가장 큰 도형들과 비슷한 후보 중 가장 왼쪽을 기준 정면도로 사용한다.
        (LwPolyline Shape, ShapeInfo Info) reference = all
            .Where(x => x.Info.Area >= maximumArea * 0.70)
            .OrderBy(x => x.Info.CenterX)
            .First();

        LwPolyline referenceFront = reference.Shape;
        ShapeInfo referenceInfo = reference.Info;

        List<(LwPolyline Shape, ShapeInfo Info)> nearbyCandidates = all
            .Where(x => !ReferenceEquals(x.Shape, referenceFront))
            .ToList();

        LwPolyline referenceTop = FindReferenceView(
            nearbyCandidates,
            x =>
                x.Info.CenterY > referenceInfo.CenterY &&
                IsHorizontalReferenceView(x.Info, referenceInfo),
            x =>
                Math.Abs(x.Info.CenterX - referenceInfo.CenterX) +
                Math.Abs(x.Info.MinY - referenceInfo.MaxY),
            "기준 상단 평면도"
        );

        LwPolyline referenceBottom = FindReferenceView(
            nearbyCandidates,
            x =>
                x.Info.CenterY < referenceInfo.CenterY &&
                IsHorizontalReferenceView(x.Info, referenceInfo),
            x =>
                Math.Abs(x.Info.CenterX - referenceInfo.CenterX) +
                Math.Abs(x.Info.MaxY - referenceInfo.MinY),
            "기준 하단 평면도"
        );

        LwPolyline referenceLeft = FindReferenceView(
            nearbyCandidates,
            x =>
                x.Info.CenterX < referenceInfo.CenterX &&
                IsVerticalReferenceView(x.Info, referenceInfo),
            x =>
                Math.Abs(x.Info.CenterY - referenceInfo.CenterY) +
                Math.Abs(x.Info.MaxX - referenceInfo.MinX),
            "기준 좌측 측면도"
        );

        LwPolyline referenceRight = FindReferenceView(
            nearbyCandidates,
            x =>
                x.Info.CenterX > referenceInfo.CenterX &&
                IsVerticalReferenceView(x.Info, referenceInfo),
            x =>
                Math.Abs(x.Info.CenterY - referenceInfo.CenterY) +
                Math.Abs(x.Info.MinX - referenceInfo.MaxX),
            "기준 우측 측면도"
        );

        HashSet<Entity> referenceEntities =
        [
            referenceFront,
            referenceTop,
            referenceBottom,
            referenceLeft,
            referenceRight
        ];

        // 기준 정면도 오른쪽에 있으며 기준 정면도의 45% 이상 크기를 가진 폐합 도형을
        // 오른쪽 독립 패널 외곽 후보로 본다.
        List<(LwPolyline Shape, ShapeInfo Info)> panelCandidates = all
            .Where(x =>
                !referenceEntities.Contains(x.Shape) &&
                x.Info.CenterX > referenceInfo.MaxX &&
                x.Info.Width >= referenceInfo.Width * 0.45 &&
                x.Info.Height >= referenceInfo.Height * 0.45)
            .ToList();

        // 다른 후보 안에 완전히 들어 있는 폴리라인은 외곽이 아니라 내부 형상으로 판단한다.
        List<(LwPolyline Shape, ShapeInfo Info)> panelOutlines = panelCandidates
            .Where(candidate =>
                !panelCandidates.Any(other =>
                    !ReferenceEquals(candidate.Shape, other.Shape) &&
                    ContainsBox(other.Info, candidate.Info)))
            .OrderBy(x => x.Info.MinX)
            .ToList();

        if (panelOutlines.Count == 0)
        {
            throw new InvalidOperationException(
                "기준 조립도 오른쪽에서 수정할 큰 패널을 찾지 못했습니다."
            );
        }

        List<RightPanel> rightPanels = new();

        for (int i = 0; i < panelOutlines.Count; i++)
        {
            var panel = panelOutlines[i];

            List<Entity> contents = document.Entities
                .Where(entity =>
                    !ReferenceEquals(entity, panel.Shape) &&
                    entity is not Dimension &&
                    entity is not TextEntity &&
                    entity is not MText)
                .Select(entity => new
                {
                    Entity = entity,
                    Info = TryCreateShapeInfo(entity)
                })
                .Where(x =>
                    x.Info != null &&
                    IsCenterInside(x.Info, panel.Info))
                .Select(x => x.Entity)
                .ToList();

            double gapBefore = 0.0;

            if (i > 0)
            {
                gapBefore =
                    panel.Info.MinX -
                    panelOutlines[i - 1].Info.MaxX;
            }

            rightPanels.Add(new RightPanel
            {
                Outline = panel.Shape,
                OriginalOutlineInfo = panel.Info,
                Contents = contents,
                GapBefore = gapBefore
            });
        }

        return new DetectedParts
        {
            Document = document,
            ReferenceFront = referenceFront,
            ReferenceFrontInfo = referenceInfo,
            ReferenceTop = referenceTop,
            ReferenceBottom = referenceBottom,
            ReferenceLeft = referenceLeft,
            ReferenceRight = referenceRight,
            RightPanels = rightPanels
        };
    }

    internal static void PrintAnalysis(DetectedParts parts)
    {
        Console.WriteLine();
        Console.WriteLine("===== 기준 조립도 / 오른쪽 패널 판단 결과 =====");
        Console.WriteLine($"기준 정면도: {parts.ReferenceFrontInfo}");
        Console.WriteLine(
            $"현재 기준 크기: " +
            $"{parts.CurrentWidth:F3} x " +
            $"{parts.CurrentHeight:F3} x " +
            $"{parts.CurrentDepth:F3}"
        );
        Console.WriteLine($"오른쪽 패널: {parts.RightPanels.Count}개");

        for (int i = 0; i < parts.RightPanels.Count; i++)
        {
            RightPanel panel = parts.RightPanels[i];

            Console.WriteLine(
                $"{i + 1}. {panel.OriginalOutlineInfo}, " +
                $"내부 객체={panel.Contents.Count}개, " +
                $"앞 패널과 간격={panel.GapBefore:F3}"
            );
        }
    }

    internal static void SaveAnalysisReport(
        DetectedParts parts,
        string outputPath
    )
    {
        using StreamWriter writer = new(
            outputPath,
            false,
            new UTF8Encoding(true)
        );

        writer.WriteLine("===== 기준 조립도 / 오른쪽 패널 판단 결과 =====");
        writer.WriteLine($"기준 정면도: {parts.ReferenceFrontInfo}");
        writer.WriteLine(
            $"현재 기준 크기: " +
            $"{parts.CurrentWidth:F3} x " +
            $"{parts.CurrentHeight:F3} x " +
            $"{parts.CurrentDepth:F3}"
        );
        writer.WriteLine($"오른쪽 패널: {parts.RightPanels.Count}개");

        for (int i = 0; i < parts.RightPanels.Count; i++)
        {
            RightPanel panel = parts.RightPanels[i];

            writer.WriteLine(
                $"{i + 1}. {panel.OriginalOutlineInfo}, " +
                $"내부 객체={panel.Contents.Count}개, " +
                $"앞 패널과 간격={panel.GapBefore:F3}"
            );
        }
    }

    internal static ShapeInfo CreateShapeInfo(Entity entity)
    {
        var box = entity.GetBoundingBox();

        return new ShapeInfo
        {
            Entity = entity,
            EntityType = entity.ObjectName,
            LayerName = entity.Layer?.Name ?? "",
            MinX = box.Min.X,
            MinY = box.Min.Y,
            MaxX = box.Max.X,
            MaxY = box.Max.Y
        };
    }

    private static ShapeInfo? TryCreateShapeInfo(Entity entity)
    {
        try
        {
            return CreateShapeInfo(entity);
        }
        catch
        {
            return null;
        }
    }

    private static LwPolyline FindReferenceView(
        IEnumerable<(LwPolyline Shape, ShapeInfo Info)> candidates,
        Func<(LwPolyline Shape, ShapeInfo Info), bool> condition,
        Func<(LwPolyline Shape, ShapeInfo Info), double> distance,
        string name
    )
    {
        return candidates
            .Where(condition)
            .OrderBy(distance)
            .Select(x => x.Shape)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"{name}를 찾지 못했습니다.");
    }

    private static bool IsHorizontalReferenceView(
        ShapeInfo candidate,
        ShapeInfo front
    )
    {
        return candidate.Width >= candidate.Height &&
               candidate.Width >= front.Width * 0.70 &&
               candidate.Width <= front.Width * 1.30 &&
               candidate.Height < front.Height * 0.60;
    }

    private static bool IsVerticalReferenceView(
        ShapeInfo candidate,
        ShapeInfo front
    )
    {
        return candidate.Height >= candidate.Width &&
               candidate.Height >= front.Height * 0.70 &&
               candidate.Height <= front.Height * 1.30 &&
               candidate.Width < front.Width * 0.60;
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

    private static bool ContainsBox(
        ShapeInfo outer,
        ShapeInfo inner
    )
    {
        const double tolerance = 0.001;

        return inner.MinX >= outer.MinX - tolerance &&
               inner.MaxX <= outer.MaxX + tolerance &&
               inner.MinY >= outer.MinY - tolerance &&
               inner.MaxY <= outer.MaxY + tolerance;
    }

    private static bool IsCenterInside(
        ShapeInfo child,
        ShapeInfo parent
    )
    {
        return child.CenterX >= parent.MinX &&
               child.CenterX <= parent.MaxX &&
               child.CenterY >= parent.MinY &&
               child.CenterY <= parent.MaxY;
    }
}