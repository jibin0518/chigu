using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;

namespace DwgAutoResize;

/// <summary>
/// Handle을 사용하지 않고 도면의 형상과 위치를 기준으로
/// 주요 부위를 판단하기 위한 코드.
/// 현재 단계에서는 '치구' 레이어의 폐합 도형 중
/// 바운딩박스 넓이가 가장 큰 객체를 정면 외곽 후보로 찾는다.
/// </summary>
internal static class PartDetector
{
    internal sealed class DetectedShape
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

        /// <summary>
        /// 실제 도형 면적이 아니라 객체를 감싸는 바운딩박스 면적.
        /// 서로 다른 형상의 전체 크기를 비교하는 용도다.
        /// </summary>
        public double BoundingArea => Width * Height;

        public double CenterX => (MinX + MaxX) / 2.0;

        public double CenterY => (MinY + MaxY) / 2.0;

        public override string ToString()
        {
            return
                $"종류={EntityType}, " +
                $"Layer={LayerName}, " +
                $"Handle={Entity.Handle}, " +
                $"크기={Width:F6} x {Height:F6}, " +
                $"넓이={BoundingArea:F6}, " +
                $"중심=({CenterX:F6}, {CenterY:F6}), " +
                $"범위=({MinX:F6}, {MinY:F6}) ~ " +
                $"({MaxX:F6}, {MaxY:F6})";
        }
    }

    /// <summary>
    /// 치구 레이어의 폐합 폴리라인을 바운딩박스 넓이순으로 정렬한다.
    /// </summary>
    internal static List<DetectedShape> GetFixtureClosedShapesByArea(
        CadDocument document
    )
    {
        return document.Entities
            .Where(IsFixtureLayer)
            .Where(IsClosedShape)
            .Select(TryCreateDetectedShape)
            .Where(shape => shape != null)
            .Cast<DetectedShape>()
            .Where(shape =>
                shape.Width > 0.000001 &&
                shape.Height > 0.000001
            )
            .OrderByDescending(shape => shape.BoundingArea)
            .ToList();
    }

    /// <summary>
    /// 치구 레이어의 폐합 도형 중 가장 큰 객체를 반환한다.
    /// 현재 도면 형식에서는 정면 외곽 후보로 사용한다.
    /// </summary>
    internal static DetectedShape FindLargestFixtureShape(
        CadDocument document
    )
    {
        DetectedShape? largest = GetFixtureClosedShapesByArea(document)
            .FirstOrDefault();

        if (largest == null)
        {
            throw new InvalidOperationException(
                "'치구' 레이어에서 넓이를 계산할 수 있는 " +
                "폐합 폴리라인을 찾지 못했습니다."
            );
        }

        return largest;
    }

    /// <summary>
    /// 가장 큰 치구 도형과 전체 후보 순위를 콘솔에 출력한다.
    /// </summary>
    internal static void PrintLargestFixtureShape(
        CadDocument document
    )
    {
        List<DetectedShape> shapes =
            GetFixtureClosedShapesByArea(document);

        Console.WriteLine();
        Console.WriteLine(
            "===== 치구 폐합 도형 넓이순 판단 결과 ====="
        );

        if (shapes.Count == 0)
        {
            Console.WriteLine(
                "판단 가능한 폐합 도형이 없습니다."
            );
            return;
        }

        for (int i = 0; i < shapes.Count; i++)
        {
            string role = i == 0
                ? "  ← 가장 큰 도형 / 정면 외곽 후보"
                : "";

            Console.WriteLine(
                $"{i + 1}. {shapes[i]}{role}"
            );
        }
    }

    private static bool IsFixtureLayer(Entity entity)
    {
        return string.Equals(
            entity.Layer?.Name?.Trim(),
            "치구",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsClosedShape(Entity entity)
    {
        return entity switch
        {
            LwPolyline lwPolyline => lwPolyline.IsClosed,
            Polyline2D polyline2D => polyline2D.IsClosed,
            Circle => true,
            _ => false
        };
    }

    private static DetectedShape? TryCreateDetectedShape(
        Entity entity
    )
    {
        try
        {
            var box = entity.GetBoundingBox();

            return new DetectedShape
            {
                Entity = entity,
                EntityType = GetEntityTypeName(entity),
                LayerName = entity.Layer?.Name ?? "",
                MinX = box.Min.X,
                MinY = box.Min.Y,
                MaxX = box.Max.X,
                MaxY = box.Max.Y
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetEntityTypeName(Entity entity)
    {
        return entity switch
        {
            LwPolyline => "LWPOLYLINE",
            Polyline2D => "POLYLINE2D",
            Arc => "ARC",
            Circle => "CIRCLE",            
            Line => "LINE",
            _ => entity.ObjectName
        };
    }
}
