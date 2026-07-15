using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;

namespace DwgAutoResize;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            string? dwgPath = SelectDwgFile();

            if (string.IsNullOrWhiteSpace(dwgPath))
            {
                return;
            }

            // DWG 파일 전체를 메모리로 읽는다.
            CadDocument document = DwgReader.Read(dwgPath);

            // 문서 전체 정보는 document 안에 그대로 들어 있다.
            DrawingData drawingData = ReadDrawingData(document);

            EntityData mainChigu = FindMainChigu(drawingData);

            EntityData topPlate =
                FindTopPlate(
                    drawingData,
                    mainChigu
                );

            EntityData bottomPlate =
                FindBottomPlate(
                    drawingData,
                    mainChigu
                );
            
            EntityData leftPlate =
                FindLeftPlate(
                    drawingData,
                    mainChigu
                );

            EntityData rightPlate =
                FindRightPlate(
                    drawingData,
                    mainChigu
                );

            List<EntityData> topRedBlocks =
                FindPlateRedBlocks(
                    drawingData,
                    topPlate,
                    3.0
                );

            List<EntityData> bottomRedBlocks =
                FindPlateRedBlocks(
                    drawingData,
                    bottomPlate,
                    3.0
                );

            List<EntityData> largeCircles =
                FindMainChiguLargeCircles(
                    drawingData,
                    mainChigu,
                    18.0,
                    2.0
                );
            EntityData topLeftCircle =
                largeCircles[0];

            EntityData topRightCircle =
                largeCircles[1];

            EntityData bottomLeftCircle =
                largeCircles[2];

            EntityData bottomRightCircle =
                largeCircles[3];

            List<BoltHolePair> centerBoltHoles =
                FindCenterBoltHoles(
                    drawingData,
                    mainChigu
                );

            BoltHolePair leftCenterBolt =
                centerBoltHoles[0];

            BoltHolePair rightCenterBolt =
                centerBoltHoles[1];

            foreach (BoltHolePair pair in centerBoltHoles)
            {
                Console.WriteLine(
                    $"중심=({pair.CenterX}, {pair.CenterY})"
                );

                Console.WriteLine(
                    $"바깥 원 Handle={pair.OuterCircle.Handle}, " +
                    $"Radius={pair.OuterCircle.Radius}"
                );

                Console.WriteLine(
                    $"안쪽 원 Handle={pair.InnerCircle.Handle}, " +
                    $"Radius={pair.InnerCircle.Radius}"
                );

                Console.WriteLine();
            }

            Console.WriteLine(
                $"Handle={topLeftCircle.Handle}, " +
                $"X={topLeftCircle.CenterX}, " +
                $"Y={topLeftCircle.CenterY}"
            );

            Console.WriteLine(
                $"Handle={topRightCircle.Handle}, " +
                $"X={topRightCircle.CenterX}, " +
                $"Y={topRightCircle.CenterY}"
            );

            Console.WriteLine(
                $"Handle={bottomLeftCircle.Handle}, " +
                $"X={bottomLeftCircle.CenterX}, " +
                $"Y={bottomLeftCircle.CenterY}"
            );

            Console.WriteLine(
                $"Handle={bottomRightCircle.Handle}, " +
                $"X={bottomRightCircle.CenterX}, " +
                $"Y={bottomRightCircle.CenterY}"
            );
                        // Console.WriteLine("위쪽 빨간 블록");

            // foreach (EntityData block in topRedBlocks)
            // {
            //     Console.WriteLine(
            //         $"Handle={block.Handle}, " +
            //         $"크기={block.Width} x {block.Height}, " +
            //         $"중심=({block.CenterBoxX}, {block.CenterBoxY})"
            //     );
            // }

            // Console.WriteLine("아래쪽 빨간 블록");

            // foreach (EntityData block in bottomRedBlocks)
            // {
            //     Console.WriteLine(
            //         $"Handle={block.Handle}, " +
            //         $"크기={block.Width} x {block.Height}, " +
            //         $"중심=({block.CenterBoxX}, {block.CenterBoxY})"
            //     );
            // }
            // Console.WriteLine("오른쪽 세로판");
            // Console.WriteLine($"Handle={rightPlate.Handle}");
            // Console.WriteLine(
            //     $"크기={rightPlate.Width} x {rightPlate.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({rightPlate.CenterBoxX}, {rightPlate.CenterBoxY})"
            // );

            // Console.WriteLine("왼쪽 세로판");
            // Console.WriteLine($"Handle={leftPlate.Handle}");
            // Console.WriteLine(
            //     $"크기={leftPlate.Width} x {leftPlate.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({leftPlate.CenterBoxX}, {leftPlate.CenterBoxY})"
            // );

            // Console.WriteLine("중심 판");
            // Console.WriteLine($"Handle={mainChigu.Handle}");
            // Console.WriteLine(
            //     $"크기={mainChigu.Width} x {mainChigu.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({mainChigu.CenterBoxX}, {mainChigu.CenterBoxY})"
            // );

            // Console.WriteLine("아래쪽 긴 판");
            // Console.WriteLine($"Handle={bottomPlate.Handle}");
            // Console.WriteLine(
            //     $"크기={bottomPlate.Width} x {bottomPlate.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({bottomPlate.CenterBoxX}, {bottomPlate.CenterBoxY})"
            // );

            // Console.WriteLine("위쪽 긴 판");
            // Console.WriteLine($"Handle={topPlate.Handle}");
            // Console.WriteLine(
            //     $"크기={topPlate.Width} x {topPlate.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({topPlate.CenterBoxX}, {topPlate.CenterBoxY})"
            // );
                    }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private static string? SelectDwgFile()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "확인할 DWG 파일 선택",
            Filter = "AutoCAD DWG 파일 (*.dwg)|*.dwg|모든 파일 (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    /// <summary>
    /// DWG 문서 전체 정보를 읽어서 DrawingData로 정리한다.
    /// </summary>
    private static DrawingData ReadDrawingData(
        CadDocument document
    )
    {
        return new DrawingData
        {
            Document = document,
            Version = document.Header.Version.ToString(),
            LayerCount = document.Layers.Count,
            BlockCount = document.BlockRecords.Count,
            EntityCount = document.Entities.Count(),
            Entities = document.Entities
                .Select(CreateEntityData)
                .ToList()
        };
    }

    /// <summary>
    /// CAD 객체 하나의 공통 정보와 종류별 정보를 읽는다.
    /// </summary>
    private static EntityData CreateEntityData(
        Entity entity
    )
    {
        EntityData data = new()
        {
            Entity = entity,
            ObjectName = entity.ObjectName,
            Handle = entity.Handle.ToString(),
            LayerName = entity.Layer?.Name ?? ""
        };

        switch (entity)
        {
            case Line line:
                data.StartX = line.StartPoint.X;
                data.StartY = line.StartPoint.Y;
                data.StartZ = line.StartPoint.Z;

                data.EndX = line.EndPoint.X;
                data.EndY = line.EndPoint.Y;
                data.EndZ = line.EndPoint.Z;
                break;

            case Arc arc:
                data.CenterX = arc.Center.X;
                data.CenterY = arc.Center.Y;
                data.CenterZ = arc.Center.Z;

                data.Radius = arc.Radius;
                data.StartAngle = arc.StartAngle;
                data.EndAngle = arc.EndAngle;
                break;

            case Circle circle:
                data.CenterX = circle.Center.X;
                data.CenterY = circle.Center.Y;
                data.CenterZ = circle.Center.Z;

                data.Radius = circle.Radius;
                data.Diameter = circle.Radius * 2.0;
                break;

            case LwPolyline polyline:
                data.IsClosed = polyline.IsClosed;

                data.Vertices = polyline.Vertices
                    .Select(vertex => new PointData
                    {
                        X = vertex.Location.X,
                        Y = vertex.Location.Y,
                        Z = 0.0
                    })
                    .ToList();
                break;

            case Polyline2D polyline:
                data.IsClosed = polyline.IsClosed;

                data.Vertices = polyline.Vertices
                    .Select(vertex => new PointData
                    {
                        X = vertex.Location.X,
                        Y = vertex.Location.Y,
                        Z = vertex.Location.Z
                    })
                    .ToList();
                break;

            case TextEntity text:
                data.TextValue = text.Value;

                data.InsertX = text.InsertPoint.X;
                data.InsertY = text.InsertPoint.Y;
                data.InsertZ = text.InsertPoint.Z;
                break;

            case MText mtext:
                data.TextValue = mtext.Value;

                data.InsertX = mtext.InsertPoint.X;
                data.InsertY = mtext.InsertPoint.Y;
                data.InsertZ = mtext.InsertPoint.Z;
                break;

            case Insert insert:
                data.BlockName = insert.Block?.Name ?? "";

                data.InsertX = insert.InsertPoint.X;
                data.InsertY = insert.InsertPoint.Y;
                data.InsertZ = insert.InsertPoint.Z;
                break;

            case Dimension dimension:
                data.TextValue = dimension.Text;

                data.TextPositionX =
                    dimension.TextMiddlePoint.X;

                data.TextPositionY =
                    dimension.TextMiddlePoint.Y;

                data.TextPositionZ =
                    dimension.TextMiddlePoint.Z;
                break;
        }

        /*
         * 바운딩박스를 지원하는 객체는 위치와 크기도 저장한다.
         */
        try
        {
            var box = entity.GetBoundingBox();

            data.MinX = box.Min.X;
            data.MinY = box.Min.Y;
            data.MinZ = box.Min.Z;

            data.MaxX = box.Max.X;
            data.MaxY = box.Max.Y;
            data.MaxZ = box.Max.Z;

            data.Width = Math.Abs(
                box.Max.X - box.Min.X
            );

            data.Height = Math.Abs(
                box.Max.Y - box.Min.Y
            );

            data.CenterBoxX =
                (box.Min.X + box.Max.X) / 2.0;

            data.CenterBoxY =
                (box.Min.Y + box.Max.Y) / 2.0;
        }
        catch
        {
            // 바운딩박스를 지원하지 않는 객체는 기본값 유지
        }

        return data;
    }

    /// <summary>
    /// DWG 문서 전체를 메모리에서 사용하기 위한 데이터.
    /// </summary>
    internal sealed class DrawingData
    {
        public required CadDocument Document { get; init; }

        public required string Version { get; init; }

        public int LayerCount { get; init; }

        public int BlockCount { get; init; }

        public int EntityCount { get; init; }

        public required List<EntityData> Entities { get; init; }
    }

    /// <summary>
    /// CAD 객체 하나의 정보를 저장한다.
    /// 실제 원본 객체는 Entity 속성에 그대로 보관된다.
    /// </summary>
    internal sealed class EntityData
    {
        public required Entity Entity { get; init; }

        public required string ObjectName { get; init; }

        public required string Handle { get; init; }

        public required string LayerName { get; init; }

        public bool IsClosed { get; set; }

        public List<PointData> Vertices { get; set; } = new();

        public double StartX { get; set; }
        public double StartY { get; set; }
        public double StartZ { get; set; }

        public double EndX { get; set; }
        public double EndY { get; set; }
        public double EndZ { get; set; }

        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double CenterZ { get; set; }

        public double Radius { get; set; }
        public double Diameter { get; set; }

        public double StartAngle { get; set; }
        public double EndAngle { get; set; }

        public string TextValue { get; set; } = "";

        public string BlockName { get; set; } = "";

        public double InsertX { get; set; }
        public double InsertY { get; set; }
        public double InsertZ { get; set; }

        public double TextPositionX { get; set; }
        public double TextPositionY { get; set; }
        public double TextPositionZ { get; set; }

        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }

        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }

        public double Width { get; set; }
        public double Height { get; set; }

        public double CenterBoxX { get; set; }
        public double CenterBoxY { get; set; }
    }

    internal sealed class PointData
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
    }

    internal sealed class BoltHolePair
    {
        public required EntityData OuterCircle { get; init; }
        public required EntityData InnerCircle { get; init; }

        public double CenterX => OuterCircle.CenterX;
        public double CenterY => OuterCircle.CenterY;
    }

    private static EntityData FindMainChigu(DrawingData drawingData)
    {
        EntityData? result = null;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
                continue;

            if (x.ObjectName != "LWPOLYLINE")
                continue;

            if (!x.IsClosed)
                continue;

            if (result == null)
            {
                result = x;
                continue;
            }

            if (x.Vertices.Count > result.Vertices.Count)
            {
                result = x;
            }
        }

        if (result == null)
            throw new Exception("메인 치구를 찾지 못했습니다.");

        return result;
    }

    private static EntityData FindTopPlate(
        DrawingData drawingData,
        EntityData mainChigu
    )
    {
        EntityData? result = null;
        double bestDistance = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
                continue;

            if (x.ObjectName != "LWPOLYLINE")
                continue;

            if (!x.IsClosed)
                continue;

            // 메인 치구보다 위쪽에 있어야 함
            if (x.CenterBoxY <= mainChigu.MaxY)
                continue;

            // 가로로 긴 판이어야 함
            if (x.Width <= x.Height)
                continue;

            // 메인 치구와 가로 크기가 비슷해야 함
            if (x.Width < mainChigu.Width * 0.7)
                continue;

            if (x.Width > mainChigu.Width * 1.3)
                continue;

            // 메인 치구 중심과 X 위치가 비슷한 것 우선
            double xDistance =
                Math.Abs(x.CenterBoxX - mainChigu.CenterBoxX);

            // 메인 치구 위쪽과 가장 가까운 판 우선
            double yDistance =
                x.MinY - mainChigu.MaxY;

            double score =
                xDistance + yDistance;

            if (score < bestDistance)
            {
                bestDistance = score;
                result = x;
            }
        }

        if (result == null)
        {
            throw new Exception(
                "위쪽 긴 판을 찾지 못했습니다."
            );
        }

        return result;
    }

    private static EntityData FindBottomPlate(
        DrawingData drawingData,
        EntityData mainChigu
    )
    {
        EntityData? result = null;
        double bestDistance = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
                continue;

            if (x.ObjectName != "LWPOLYLINE")
                continue;

            if (!x.IsClosed)
                continue;

            // 메인 치구보다 아래쪽에 있어야 함
            if (x.CenterBoxY >= mainChigu.MinY)
                continue;

            // 가로로 긴 판이어야 함
            if (x.Width <= x.Height)
                continue;

            // 메인 치구와 가로 크기가 비슷해야 함
            if (x.Width < mainChigu.Width * 0.7)
                continue;

            if (x.Width > mainChigu.Width * 1.3)
                continue;

            // 메인 치구 중심과 X 위치가 비슷한 것 우선
            double xDistance =
                Math.Abs(x.CenterBoxX - mainChigu.CenterBoxX);

            // 메인 치구 아래쪽과 가장 가까운 판 우선
            double yDistance =
                mainChigu.MinY - x.MaxY;

            double score =
                xDistance + yDistance;

            if (score < bestDistance)
            {
                bestDistance = score;
                result = x;
            }
        }

        if (result == null)
        {
            throw new Exception(
                "아래쪽 긴 판을 찾지 못했습니다."
            );
        }

        return result;
    }

    private static EntityData FindLeftPlate(
    DrawingData drawingData,
    EntityData mainChigu
    )
    {
        EntityData? result = null;
        double bestDistance = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
                continue;

            if (x.ObjectName != "LWPOLYLINE")
                continue;

            if (!x.IsClosed)
                continue;

            // 메인 치구보다 왼쪽에 있어야 함
            if (x.CenterBoxX >= mainChigu.MinX)
                continue;

            // 세로로 긴 판이어야 함
            if (x.Height <= x.Width)
                continue;

            // 메인 치구와 세로 크기가 비슷해야 함
            if (x.Height < mainChigu.Height * 0.7)
                continue;

            if (x.Height > mainChigu.Height * 1.3)
                continue;

            // 메인 치구 중심과 Y 위치가 비슷한 것 우선
            double yDistance =
                Math.Abs(x.CenterBoxY - mainChigu.CenterBoxY);

            // 메인 치구 왼쪽 경계와 가장 가까운 판 우선
            double xDistance =
                mainChigu.MinX - x.MaxX;

            double score =
                xDistance + yDistance;

            if (score < bestDistance)
            {
                bestDistance = score;
                result = x;
            }
        }

        if (result == null)
        {
            throw new Exception(
                "왼쪽 세로판을 찾지 못했습니다."
            );
        }

        return result;
    }

    private static EntityData FindRightPlate(
    DrawingData drawingData,
    EntityData mainChigu
    )
    {
        EntityData? result = null;
        double bestDistance = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
                continue;

            if (x.ObjectName != "LWPOLYLINE")
                continue;

            if (!x.IsClosed)
                continue;

            // 메인 치구보다 오른쪽에 있어야 함
            if (x.CenterBoxX <= mainChigu.MaxX)
                continue;

            // 세로로 긴 판이어야 함
            if (x.Height <= x.Width)
                continue;

            // 메인 치구와 세로 크기가 비슷해야 함
            if (x.Height < mainChigu.Height * 0.7)
                continue;

            if (x.Height > mainChigu.Height * 1.3)
                continue;

            // 메인 치구 중심과 Y 위치가 비슷한 것 우선
            double yDistance =
                Math.Abs(x.CenterBoxY - mainChigu.CenterBoxY);

            // 메인 치구 오른쪽 경계와 가장 가까운 판 우선
            double xDistance =
                x.MinX - mainChigu.MaxX;

            double score =
                xDistance + yDistance;

            if (score < bestDistance)
            {
                bestDistance = score;
                result = x;
            }
        }

        if (result == null)
        {
            throw new Exception(
                "오른쪽 세로판을 찾지 못했습니다."
            );
        }

        return result;
    }

    private static List<EntityData> FindPlateRedBlocks(
    DrawingData drawingData,
    EntityData plate,
    double tolerance = 3.0
    )
    {
        List<EntityData> result = new();

        foreach (EntityData x in drawingData.Entities)
        {
            // 치구 레이어만
            if (x.LayerName != "치구")
            {
                continue;
            }

            // LWPOLYLINE만
            if (x.ObjectName != "LWPOLYLINE")
            {
                continue;
            }

            // 닫힌 도형만
            if (!x.IsClosed)
            {
                continue;
            }

            // 긴 판 자기 자신 제외
            if (ReferenceEquals(x, plate))
            {
                continue;
            }

            /*
            * 작은 빨간 블록은 세로로 길고 가로로 좁다.
            * 긴 판보다 작은 객체만 허용한다.
            */
            if (x.Width >= plate.Width * 0.5)
            {
                continue;
            }

            if (x.Height < plate.Height * 0.5)
            {
                continue;
            }

            /*
            * 작은 블록 중심이 긴 판의 가로 범위 안에 있어야 한다.
            */
            bool insideX =
                x.CenterBoxX >= plate.MinX - tolerance &&
                x.CenterBoxX <= plate.MaxX + tolerance;

            /*
            * 작은 블록은 판 위아래로 약 3 정도 튀어나올 수 있으므로
            * Y 범위를 tolerance만큼 확장한다.
            */
            bool insideY =
                x.CenterBoxY >= plate.MinY - tolerance &&
                x.CenterBoxY <= plate.MaxY + tolerance;

            if (!insideX || !insideY)
            {
                continue;
            }

            /*
            * 실제 도형 범위도 긴 판과 겹치는지 확인한다.
            */
            bool overlapsPlate =
                x.MaxX >= plate.MinX - tolerance &&
                x.MinX <= plate.MaxX + tolerance &&
                x.MaxY >= plate.MinY - tolerance &&
                x.MinY <= plate.MaxY + tolerance;

            if (!overlapsPlate)
            {
                continue;
            }

            result.Add(x);
        }

        /*
        * 왼쪽 블록 → 오른쪽 블록 순서
        */
        result = result
            .OrderBy(x => x.CenterBoxX)
            .ToList();

        return result;
    }

    private static List<EntityData> FindMainChiguLargeCircles(
    DrawingData drawingData,
    EntityData mainChigu,
    double wallDistance = 18.0,
    double tolerance = 2.0
    )
    {
        List<EntityData> result = new();

        foreach (EntityData x in drawingData.Entities)
        {
            // 치구 레이어만
            if (x.LayerName != "치구")
            {
                continue;
            }

            // 원만
            if (x.ObjectName != "CIRCLE")
            {
                continue;
            }

            // 메인 치구 내부에 중심이 있어야 함
            bool centerInside =
                x.CenterX >= mainChigu.MinX &&
                x.CenterX <= mainChigu.MaxX &&
                x.CenterY >= mainChigu.MinY &&
                x.CenterY <= mainChigu.MaxY;

            if (!centerInside)
            {
                continue;
            }

            // 각 벽에서 원 외곽까지 거리
            double leftGap =
                (x.CenterX - x.Radius) -
                mainChigu.MinX;

            double rightGap =
                mainChigu.MaxX -
                (x.CenterX + x.Radius);

            double bottomGap =
                (x.CenterY - x.Radius) -
                mainChigu.MinY;

            double topGap =
                mainChigu.MaxY -
                (x.CenterY + x.Radius);

            bool nearLeft =
                Math.Abs(leftGap - wallDistance)
                <= tolerance;

            bool nearRight =
                Math.Abs(rightGap - wallDistance)
                <= tolerance;

            bool nearBottom =
                Math.Abs(bottomGap - wallDistance)
                <= tolerance;

            bool nearTop =
                Math.Abs(topGap - wallDistance)
                <= tolerance;

            /*
            * 네 모서리 중 하나에 있어야 함:
            * 왼쪽 위, 오른쪽 위,
            * 왼쪽 아래, 오른쪽 아래
            */
            bool isCornerCircle =
                (nearLeft || nearRight) &&
                (nearTop || nearBottom);

            if (!isCornerCircle)
            {
                continue;
            }

            result.Add(x);
        }

        /*
        * 위쪽부터,
        * 같은 줄에서는 왼쪽부터 정렬
        */
        result = result
            .OrderByDescending(x => x.CenterY)
            .ThenBy(x => x.CenterX)
            .ToList();

        return result;
    }

    private static List<BoltHolePair> FindCenterBoltHoles(
    DrawingData drawingData,
    EntityData mainChigu,
    double centerTolerance = 0.001
    )
    {
        List<EntityData> boltCircles = new();

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "볼트 구멍")
            {
                continue;
            }

            if (x.ObjectName != "CIRCLE")
            {
                continue;
            }

            // 메인 치구 내부에 있는 원만
            bool insideMain =
                x.CenterX >= mainChigu.MinX &&
                x.CenterX <= mainChigu.MaxX &&
                x.CenterY >= mainChigu.MinY &&
                x.CenterY <= mainChigu.MaxY;

            if (!insideMain)
            {
                continue;
            }

            boltCircles.Add(x);
        }

        List<BoltHolePair> result = new();
        HashSet<string> usedHandles = new();

        foreach (EntityData outerCandidate in boltCircles)
        {
            if (usedHandles.Contains(outerCandidate.Handle))
            {
                continue;
            }

            EntityData? innerCandidate = null;

            foreach (EntityData other in boltCircles)
            {
                if (ReferenceEquals(outerCandidate, other))
                {
                    continue;
                }

                // 중심 좌표가 같은지 확인
                bool sameCenter =
                    Math.Abs(
                        outerCandidate.CenterX -
                        other.CenterX
                    ) <= centerTolerance &&
                    Math.Abs(
                        outerCandidate.CenterY -
                        other.CenterY
                    ) <= centerTolerance;

                if (!sameCenter)
                {
                    continue;
                }

                // 반지름이 더 작은 원을 안쪽 원으로 선택
                if (other.Radius >= outerCandidate.Radius)
                {
                    continue;
                }

                if (innerCandidate == null ||
                    other.Radius > innerCandidate.Radius)
                {
                    innerCandidate = other;
                }
            }

            if (innerCandidate == null)
            {
                continue;
            }

            result.Add(new BoltHolePair
            {
                OuterCircle = outerCandidate,
                InnerCircle = innerCandidate
            });

            usedHandles.Add(
                outerCandidate.Handle
            );

            usedHandles.Add(
                innerCandidate.Handle
            );
        }

        // 왼쪽 볼트 구멍 → 오른쪽 볼트 구멍 순서
        result = result
            .OrderBy(pair => pair.CenterX)
            .ToList();

        return result;
    }
}