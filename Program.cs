using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            string? dwgPath = SelectDwgFile();

            if (string.IsNullOrWhiteSpace(dwgPath))
            {
                Console.WriteLine("파일 선택이 취소되었습니다.");
                return;
            }

            CadDocument document = DwgReader.Read(dwgPath);

            PartDetector.PrintLargestFixtureShape(document);

            PartDetector.DetectedShape largestShape =
                PartDetector.FindLargestFixtureShape(document);

            Console.WriteLine();
            Console.WriteLine("가장 큰 도형:");
            Console.WriteLine(largestShape);

            PrintDocumentInfo(document);
            PrintEntities(document);
            PrintTargetLayerClassification(document);

            string directory = Path.GetDirectoryName(dwgPath)!;
            string name = Path.GetFileNameWithoutExtension(dwgPath);

            string allObjectsPath = Path.Combine(directory, $"{name}_objects.txt");
            string classificationPath = Path.Combine(
                directory,
                $"{name}_치구_볼트구멍_분류.txt"
            );

            SaveEntityReport(document, allObjectsPath);
            SaveTargetLayerClassificationReport(document, classificationPath);

            Console.WriteLine();
            Console.WriteLine("결과 저장 완료:");
            Console.WriteLine(allObjectsPath);
            Console.WriteLine(classificationPath);
            Console.WriteLine();
            Console.WriteLine("아무 키나 누르면 종료됩니다.");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("오류 발생:");
            Console.WriteLine(ex);
            Console.ReadKey();
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

    private static void PrintDocumentInfo(CadDocument document)
    {
        Console.WriteLine("===== 문서 정보 =====");
        Console.WriteLine($"버전: {document.Header.Version}");
        Console.WriteLine($"레이어 수: {document.Layers.Count}");
        Console.WriteLine($"블록 수: {document.BlockRecords.Count}");
        Console.WriteLine($"Model Space 객체 수: {document.Entities.Count()}");
        Console.WriteLine();
    }

    private static void PrintEntities(CadDocument document)
    {
        Console.WriteLine("===== 전체 객체 목록 =====");

        int index = 0;
        foreach (Entity entity in document.Entities)
        {
            Console.WriteLine(GetEntityDescription(index, entity));
            index++;
        }
    }

    private static string GetEntityDescription(int index, Entity entity)
    {
        string common =
            $"[{index}] 종류={entity.ObjectName}, " +
            $"Handle={entity.Handle}, Layer={entity.Layer?.Name}";

        return entity switch
        {
            Line line =>
                $"{common}, Start={FormatPoint(line.StartPoint)}, End={FormatPoint(line.EndPoint)}",

            Arc arc =>
                $"{common}, Center={FormatPoint(arc.Center)}, Radius={arc.Radius:F6}, " +
                $"StartAngle={arc.StartAngle:F6}, EndAngle={arc.EndAngle:F6}",

            Circle circle =>
                $"{common}, Center={FormatPoint(circle.Center)}, Radius={circle.Radius:F6}, " +
                $"Diameter={circle.Radius * 2.0:F6}",

            LwPolyline polyline =>
                $"{common}, Closed={polyline.IsClosed}, Vertices={FormatLwPolyline(polyline)}",

            Polyline2D polyline =>
                $"{common}, Closed={polyline.IsClosed}, Vertices={FormatPolyline2D(polyline)}",

            TextEntity text =>
                $"{common}, Insert={FormatPoint(text.InsertPoint)}, Text=\"{text.Value}\"",

            MText mtext =>
                $"{common}, Insert={FormatPoint(mtext.InsertPoint)}, Text=\"{mtext.Value}\"",

            Insert insert =>
                $"{common}, Block={insert.Block?.Name}, Insert={FormatPoint(insert.InsertPoint)}",

            Dimension dimension =>
                $"{common}, Text=\"{dimension.Text}\", " +
                $"TextPosition={FormatPoint(dimension.TextMiddlePoint)}",

            _ => common
        };
    }

    private static string FormatPoint(dynamic point)
    {
        try
        {
            return $"({point.X:F6}, {point.Y:F6}, {point.Z:F6})";
        }
        catch
        {
            return point?.ToString() ?? "";
        }
    }

    private static string FormatLwPolyline(LwPolyline polyline)
    {
        return string.Join(
            " | ",
            polyline.Vertices.Select(v => $"({v.Location.X:F6}, {v.Location.Y:F6})")
        );
    }

    private static string FormatPolyline2D(Polyline2D polyline)
    {
        return string.Join(
            " | ",
            polyline.Vertices.Select(
                v => $"({v.Location.X:F6}, {v.Location.Y:F6}, {v.Location.Z:F6})"
            )
        );
    }

    private static void SaveEntityReport(CadDocument document, string outputPath)
    {
        using StreamWriter writer = new(outputPath, false, new UTF8Encoding(true));

        writer.WriteLine($"버전: {document.Header.Version}");
        writer.WriteLine($"레이어 수: {document.Layers.Count}");
        writer.WriteLine($"블록 수: {document.BlockRecords.Count}");
        writer.WriteLine($"Model Space 객체 수: {document.Entities.Count()}");
        writer.WriteLine();

        int index = 0;
        foreach (Entity entity in document.Entities)
        {
            writer.WriteLine(GetEntityDescription(index, entity));
            index++;
        }
    }

    private sealed class EntitySizeInfo
    {
        public required Entity Entity { get; init; }
        public required string TypeName { get; init; }
        public short ColorIndex { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double Area { get; init; }
        public double MinX { get; init; }
        public double MinY { get; init; }
        public double MaxX { get; init; }
        public double MaxY { get; init; }
    }

    private static readonly string[] TargetLayers =
    {
        "치구",
        "볼트 구멍"
    };

    private static IEnumerable<EntitySizeInfo> GetTargetLayerEntities(
        CadDocument document,
        string layerName
    )
    {
        return document.Entities
            .Where(entity =>
                string.Equals(
                    entity.Layer?.Name?.Trim(),
                    layerName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(CreateEntitySizeInfo);
    }

    private static EntitySizeInfo CreateEntitySizeInfo(Entity entity)
    {
        try
        {
            var box = entity.GetBoundingBox();

            double minX = box.Min.X;
            double minY = box.Min.Y;
            double maxX = box.Max.X;
            double maxY = box.Max.Y;

            double width = Math.Abs(maxX - minX);
            double height = Math.Abs(maxY - minY);

            return new EntitySizeInfo
            {
                Entity = entity,
                TypeName = GetSimpleEntityType(entity),
                ColorIndex = GetEffectiveColorIndex(entity),
                Width = width,
                Height = height,
                Area = CalculateSortArea(entity, width, height),
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            };
        }
        catch
        {
            return new EntitySizeInfo
            {
                Entity = entity,
                TypeName = GetSimpleEntityType(entity),
                ColorIndex = GetEffectiveColorIndex(entity)
            };
        }
    }

    private static double CalculateSortArea(
        Entity entity,
        double width,
        double height
    )
    {
        return entity switch
        {
            Circle circle => Math.PI * circle.Radius * circle.Radius,
            _ => width * height
        };
    }

    private static string GetSimpleEntityType(Entity entity)
    {
        return entity switch
        {
            Line => "LINE",
            Arc => "ARC",
            Circle => "CIRCLE",
            LwPolyline => "LWPOLYLINE",
            Polyline2D => "POLYLINE2D",
            TextEntity => "TEXT",
            MText => "MTEXT",
            Insert => "INSERT",
            Dimension => "DIMENSION",
            _ => entity.ObjectName
        };
    }

    private static short GetEffectiveColorIndex(Entity entity)
    {
        try
        {
            short entityColor = Convert.ToInt16(entity.Color.Index);

            if (entityColor == 256 && entity.Layer != null)
            {
                return Convert.ToInt16(entity.Layer.Color.Index);
            }

            return entityColor;
        }
        catch
        {
            return 256;
        }
    }

    private static string GetColorName(short colorIndex)
    {
        return colorIndex switch
        {
            0 => "BYBLOCK",
            1 => "RED",
            2 => "YELLOW",
            3 => "GREEN",
            4 => "CYAN",
            5 => "BLUE",
            6 => "MAGENTA",
            7 => "WHITE",
            8 => "GRAY",
            9 => "LIGHT_GRAY",
            256 => "BYLAYER",
            _ => $"COLOR_{colorIndex}"
        };
    }

    private static void PrintTargetLayerClassification(CadDocument document)
    {
        Console.WriteLine();
        Console.WriteLine("===== 대상 레이어 분류 =====");

        foreach (string layerName in TargetLayers)
        {
            List<EntitySizeInfo> entities = GetTargetLayerEntities(
                document,
                layerName
            ).ToList();

            Console.WriteLine();
            Console.WriteLine(
                $"===== Layer: {layerName} / 총 {entities.Count}개 ====="
            );

            if (entities.Count == 0)
            {
                Console.WriteLine("객체가 없습니다.");
                continue;
            }

            foreach (IGrouping<string, EntitySizeInfo> shapeGroup in entities
                         .GroupBy(info => info.TypeName)
                         .OrderBy(group => group.Key))
            {
                Console.WriteLine(
                    $"  ├─ {shapeGroup.Key} ({shapeGroup.Count()}개)"
                );

                foreach (IGrouping<short, EntitySizeInfo> colorGroup in shapeGroup
                             .GroupBy(info => info.ColorIndex)
                             .OrderBy(group => group.Key))
                {
                    Console.WriteLine(
                        $"  │   ├─ {GetColorName(colorGroup.Key)} " +
                        $"({colorGroup.Key}) ({colorGroup.Count()}개)"
                    );

                    int rank = 1;

                    foreach (EntitySizeInfo info in colorGroup
                                 .OrderByDescending(item => item.Area))
                    {
                        Console.WriteLine(
                            $"  │   │   {rank}. " +
                            $"Handle={info.Entity.Handle}, " +
                            $"크기={info.Width:F3} x {info.Height:F3}, " +
                            $"넓이={info.Area:F3}, " +
                            $"범위=({info.MinX:F3}, {info.MinY:F3}) ~ " +
                            $"({info.MaxX:F3}, {info.MaxY:F3})"
                        );

                        rank++;
                    }
                }
            }
        }
    }

    private static void SaveTargetLayerClassificationReport(
        CadDocument document,
        string outputPath
    )
    {
        using StreamWriter writer = new(
            outputPath,
            false,
            new UTF8Encoding(true)
        );

        writer.WriteLine("===== 치구 / 볼트 구멍 레이어 분류 =====");
        writer.WriteLine("정렬 순서: 레이어 → 도형 → 색상 → 넓이 내림차순");
        writer.WriteLine();

        foreach (string layerName in TargetLayers)
        {
            List<EntitySizeInfo> entities = GetTargetLayerEntities(
                document,
                layerName
            ).ToList();

            writer.WriteLine(
                $"===== Layer: {layerName} / 총 {entities.Count}개 ====="
            );

            if (entities.Count == 0)
            {
                writer.WriteLine("객체가 없습니다.");
                writer.WriteLine();
                continue;
            }

            foreach (IGrouping<string, EntitySizeInfo> shapeGroup in entities
                         .GroupBy(info => info.TypeName)
                         .OrderBy(group => group.Key))
            {
                writer.WriteLine(
                    $"  ├─ {shapeGroup.Key} ({shapeGroup.Count()}개)"
                );

                foreach (IGrouping<short, EntitySizeInfo> colorGroup in shapeGroup
                             .GroupBy(info => info.ColorIndex)
                             .OrderBy(group => group.Key))
                {
                    writer.WriteLine(
                        $"  │   ├─ {GetColorName(colorGroup.Key)} " +
                        $"({colorGroup.Key}) ({colorGroup.Count()}개)"
                    );

                    int rank = 1;

                    foreach (EntitySizeInfo info in colorGroup
                                 .OrderByDescending(item => item.Area))
                    {
                        writer.WriteLine(
                            $"  │   │   {rank}. " +
                            $"Handle={info.Entity.Handle}, " +
                            $"Layer={info.Entity.Layer?.Name}, " +
                            $"Color={GetColorName(info.ColorIndex)}" +
                            $"({info.ColorIndex}), " +
                            $"Width={info.Width:F6}, " +
                            $"Height={info.Height:F6}, " +
                            $"Area={info.Area:F6}, " +
                            $"Min=({info.MinX:F6}, {info.MinY:F6}), " +
                            $"Max=({info.MaxX:F6}, {info.MaxY:F6})"
                        );

                        writer.WriteLine(
                            $"  │   │      " +
                            GetEntityDescription(rank - 1, info.Entity)
                        );

                        rank++;
                    }
                }

                writer.WriteLine("  │");
            }

            writer.WriteLine();
        }
    }

}