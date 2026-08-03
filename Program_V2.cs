using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;

namespace DwgAutoResize;

/// <summary>
/// DWG 자동 수정 프로그램 버전 2의 초기 기반 코드.
///
/// 현재 포함된 기능:
/// 1. DWG 파일 선택 UI
/// 2. DWG 파일 읽기
/// 3. 모든 Model Space 객체의 종류, 레이어, Handle, 좌표 및 크기 수집
/// 4. 객체 정보 TXT 보고서 저장
/// 5. DWG 저장 전 기본 객체 검증
/// 6. 수정된 문서를 새 DWG로 저장하는 공통 함수
///
/// 패널 탐색, 크기 변경, 볼트 구멍 처리, 치수 처리 및 재배치는
/// 버전 2 규칙에 맞춰 이후 새로 추가한다.
/// </summary>
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

            CadDocument document = DwgReader.Read(dwgPath);
            DrawingData drawingData = ReadDrawingData(document);

            string directory =
                Path.GetDirectoryName(dwgPath)
                ?? throw new Exception("선택한 DWG 파일의 폴더를 찾지 못했습니다.");

            string fileName =
                Path.GetFileNameWithoutExtension(dwgPath);

            string reportPath = Path.Combine(
                directory,
                $"{fileName}_objects_v2.txt"
            );

            SaveEntityReport(
                drawingData,
                reportPath
            );

            // 패널 기준 객체:
            // Layer="치구", POLYLINE 계열, 실제 표시 색상=흰색(ACI 7)
            // 기준 폴리선의 외곽을 사방 12만큼 확장한 범위에
            // 걸치는 모든 객체를 같은 패널로 묶는다.
            List<PanelGroup> panelGroups = FindPanelGroups(
                drawingData,
                25.0
            );

            // 두께 패널은 도면에 있을 수도 있고 없을 수도 있다.
            // 가로/세로 비율이 충분히 큰 패널만 선택적으로 두께 패널로 표시한다.
            // 해당 조건을 만족하는 패널이 하나도 없어도 예외 없이 계속 진행한다.
            ClassifyThicknessPanels(
                panelGroups,
                2.0
            );

            string panelReportPath = Path.Combine(
                directory,
                $"{fileName}_panels_v2.txt"
            );

            SavePanelGroupReport(
                panelGroups,
                panelReportPath
            );

            // 수정 UI에 표시할 현재값은 폴리선 크기가 아니라 DWG 치수값으로 읽는다.
            // X/Y는 일반 패널 중 면적이 가장 큰 사각형 패널의 가로/세로 치수,
            // 두께는 두께 패널에 포함된 짧은 방향 치수에서 가져온다.
            CurrentDimensionValues currentValues =
                ReadCurrentDimensionValues(panelGroups);

            ResizeInput? resizeInput = ShowResizeInputDialog(
                currentValues
            );

            if (resizeInput == null)
            {
                return;
            }

            ApplyPanelResize(
                panelGroups,
                currentValues,
                resizeInput
            );

            // 크기와 치수 수정이 끝난 뒤, 현재 패널 외곽 기준으로
            // 왼쪽부터 패널 사이 간격을 정확히 50으로 재배치한다.
            ArrangePanelGroupsWithGap(
                panelGroups,
                60.0
            );

            string outputPath = Path.Combine(
                directory,
                $"{fileName}_{resizeInput.TargetWidth:0.###}x" +
                $"{resizeInput.TargetHeight:0.###}" +
                (resizeInput.TargetThickness.HasValue
                    ? $"x{resizeInput.TargetThickness.Value:0.###}"
                    : string.Empty) +
                "_v2.dwg"
            );

            SaveAsNewDwg(
                document,
                outputPath
            );

            MessageBox.Show(
                "크기 및 치수 수정 완료\n\n" +
                $"저장 위치: {outputPath}\n\n" +
                $"X: {currentValues.Width:0.###} → {resizeInput.TargetWidth:0.###}\n" +
                $"Y: {currentValues.Height:0.###} → {resizeInput.TargetHeight:0.###}\n" +
                (resizeInput.TargetThickness.HasValue && currentValues.Thickness.HasValue
                    ? $"두께: {currentValues.Thickness.Value:0.###} → {resizeInput.TargetThickness.Value:0.###}"
                    : "두께 패널 없음"),
                "완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
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

    /// <summary>
    /// 사용자가 처리할 DWG 파일 하나를 선택한다.
    /// </summary>
    private static string? SelectDwgFile()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "수정할 DWG 파일 선택",
            Filter = "AutoCAD DWG 파일 (*.dwg)|*.dwg|모든 파일 (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true,
            RestoreDirectory = true
        };

        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    /// <summary>
    /// DWG 문서의 Model Space 객체를 프로그램에서 사용하기 쉬운 형태로 변환한다.
    /// </summary>
    private static DrawingData ReadDrawingData(
        CadDocument document
    )
    {
        List<EntityData> entities = document.Entities
            .Select(CreateEntityData)
            .ToList();

        return new DrawingData
        {
            Document = document,
            Version = document.Header.Version.ToString(),
            LayerCount = document.Layers.Count,
            BlockCount = document.BlockRecords.Count,
            EntityCount = entities.Count,
            Entities = entities
        };
    }

    /// <summary>
    /// CAD 객체 하나에서 공통 정보와 객체 종류별 좌표를 읽는다.
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
            LayerName = entity.Layer?.Name ?? string.Empty,
            ColorIndex = GetEffectiveColorIndex(entity)
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
                data.Diameter = arc.Radius * 2.0;
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
                        Z = 0.0,
                        Bulge = vertex.Bulge
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
                        Z = vertex.Location.Z,
                        Bulge = 0.0
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
                data.BlockName = insert.Block?.Name ?? string.Empty;
                data.InsertX = insert.InsertPoint.X;
                data.InsertY = insert.InsertPoint.Y;
                data.InsertZ = insert.InsertPoint.Z;
                break;

            case DimensionAligned dimension:
                ReadAlignedDimensionData(
                    data,
                    dimension
                );
                break;

            case Dimension dimension:
                data.TextValue = dimension.Text;
                data.TextPositionX = dimension.TextMiddlePoint.X;
                data.TextPositionY = dimension.TextMiddlePoint.Y;
                data.TextPositionZ = dimension.TextMiddlePoint.Z;
                break;
        }

        ReadBoundingBox(
            data,
            entity
        );

        return data;
    }

    /// <summary>
    /// 정렬 치수의 측정점, 치수선 위치 및 문자 위치를 저장한다.
    /// </summary>
    private static void ReadAlignedDimensionData(
        EntityData data,
        DimensionAligned dimension
    )
    {
        data.TextValue = dimension.Text;

        data.FirstPointX = dimension.FirstPoint.X;
        data.FirstPointY = dimension.FirstPoint.Y;
        data.FirstPointZ = dimension.FirstPoint.Z;

        data.SecondPointX = dimension.SecondPoint.X;
        data.SecondPointY = dimension.SecondPoint.Y;
        data.SecondPointZ = dimension.SecondPoint.Z;

        data.DefinitionPointX = dimension.DefinitionPoint.X;
        data.DefinitionPointY = dimension.DefinitionPoint.Y;
        data.DefinitionPointZ = dimension.DefinitionPoint.Z;

        data.TextPositionX = dimension.TextMiddlePoint.X;
        data.TextPositionY = dimension.TextMiddlePoint.Y;
        data.TextPositionZ = dimension.TextMiddlePoint.Z;
    }

    /// <summary>
    /// 객체가 지원하는 경우 실제 바운딩박스와 중심, 가로 및 세로 크기를 저장한다.
    /// </summary>
    private static void ReadBoundingBox(
        EntityData data,
        Entity entity
    )
    {
        try
        {
            var box = entity.GetBoundingBox();

            data.HasBoundingBox = true;

            data.MinX = box.Min.X;
            data.MinY = box.Min.Y;
            data.MinZ = box.Min.Z;

            data.MaxX = box.Max.X;
            data.MaxY = box.Max.Y;
            data.MaxZ = box.Max.Z;

            data.Width = Math.Abs(box.Max.X - box.Min.X);
            data.Height = Math.Abs(box.Max.Y - box.Min.Y);
            data.Depth = Math.Abs(box.Max.Z - box.Min.Z);

            data.CenterBoxX = (box.Min.X + box.Max.X) / 2.0;
            data.CenterBoxY = (box.Min.Y + box.Max.Y) / 2.0;
            data.CenterBoxZ = (box.Min.Z + box.Max.Z) / 2.0;
        }
        catch
        {
            data.HasBoundingBox = false;
        }
    }

    /// <summary>
    /// 현재 수집된 모든 객체 정보를 TXT 파일로 출력한다.
    /// 이후 객체 탐색 규칙을 만들 때 이 파일의 Handle, 레이어, 좌표 및 크기를 사용한다.
    /// </summary>
    private static void SaveEntityReport(
        DrawingData drawingData,
        string outputPath
    )
    {
        using StreamWriter writer = new(
            outputPath,
            false
        );

        writer.WriteLine($"DWG 버전: {drawingData.Version}");
        writer.WriteLine($"레이어 수: {drawingData.LayerCount}");
        writer.WriteLine($"블록 수: {drawingData.BlockCount}");
        writer.WriteLine($"Model Space 객체 수: {drawingData.EntityCount}");
        writer.WriteLine();

        for (int index = 0;
             index < drawingData.Entities.Count;
             index++)
        {
            EntityData entity = drawingData.Entities[index];

            writer.WriteLine(
                $"[{index}] 종류={entity.ObjectName}, " +
                $"Handle={entity.Handle}, " +
                $"Layer={entity.LayerName}, " +
                $"ColorIndex={entity.ColorIndex}"
            );

            WriteEntityCoordinates(
                writer,
                entity
            );

            if (entity.HasBoundingBox)
            {
                writer.WriteLine(
                    "  Bounds=" +
                    $"Min({entity.MinX:F6}, {entity.MinY:F6}, {entity.MinZ:F6}), " +
                    $"Max({entity.MaxX:F6}, {entity.MaxY:F6}, {entity.MaxZ:F6})"
                );

                writer.WriteLine(
                    "  Size=" +
                    $"Width={entity.Width:F6}, " +
                    $"Height={entity.Height:F6}, " +
                    $"Depth={entity.Depth:F6}, " +
                    "Center=" +
                    $"({entity.CenterBoxX:F6}, {entity.CenterBoxY:F6}, {entity.CenterBoxZ:F6})"
                );
            }
            else
            {
                writer.WriteLine("  Bounds=지원하지 않음");
            }

            writer.WriteLine();
        }
    }

    /// <summary>
    /// 객체 종류별 핵심 좌표를 보고서에 기록한다.
    /// </summary>
    private static void WriteEntityCoordinates(
        StreamWriter writer,
        EntityData entity
    )
    {
        switch (entity.ObjectName)
        {
            case "LINE":
                writer.WriteLine(
                    "  Start=" +
                    $"({entity.StartX:F6}, {entity.StartY:F6}, {entity.StartZ:F6})"
                );
                writer.WriteLine(
                    "  End=" +
                    $"({entity.EndX:F6}, {entity.EndY:F6}, {entity.EndZ:F6})"
                );
                break;

            case "ARC":
                writer.WriteLine(
                    "  Center=" +
                    $"({entity.CenterX:F6}, {entity.CenterY:F6}, {entity.CenterZ:F6})"
                );
                writer.WriteLine(
                    $"  Radius={entity.Radius:F6}, " +
                    $"Diameter={entity.Diameter:F6}, " +
                    $"StartAngle={entity.StartAngle:F6}, " +
                    $"EndAngle={entity.EndAngle:F6}"
                );
                break;

            case "CIRCLE":
                writer.WriteLine(
                    "  Center=" +
                    $"({entity.CenterX:F6}, {entity.CenterY:F6}, {entity.CenterZ:F6})"
                );
                writer.WriteLine(
                    $"  Radius={entity.Radius:F6}, " +
                    $"Diameter={entity.Diameter:F6}"
                );
                break;

            case "LWPOLYLINE":
            case "POLYLINE2D":
                writer.WriteLine(
                    $"  Closed={entity.IsClosed}, " +
                    $"VertexCount={entity.Vertices.Count}"
                );

                for (int i = 0;
                     i < entity.Vertices.Count;
                     i++)
                {
                    PointData point = entity.Vertices[i];

                    writer.WriteLine(
                        $"  Vertex[{i}]=" +
                        $"({point.X:F6}, {point.Y:F6}, {point.Z:F6}), " +
                        $"Bulge={point.Bulge:F12}"
                    );
                }
                break;

            case "TEXT":
            case "MTEXT":
                writer.WriteLine(
                    "  Insert=" +
                    $"({entity.InsertX:F6}, {entity.InsertY:F6}, {entity.InsertZ:F6})"
                );
                writer.WriteLine($"  Text=\"{entity.TextValue}\"");
                break;

            case "INSERT":
                writer.WriteLine($"  Block={entity.BlockName}");
                writer.WriteLine(
                    "  Insert=" +
                    $"({entity.InsertX:F6}, {entity.InsertY:F6}, {entity.InsertZ:F6})"
                );
                break;

            case "DIMENSION":
                writer.WriteLine($"  Text=\"{entity.TextValue}\"");
                writer.WriteLine(
                    "  FirstPoint=" +
                    $"({entity.FirstPointX:F6}, {entity.FirstPointY:F6}, {entity.FirstPointZ:F6})"
                );
                writer.WriteLine(
                    "  SecondPoint=" +
                    $"({entity.SecondPointX:F6}, {entity.SecondPointY:F6}, {entity.SecondPointZ:F6})"
                );
                writer.WriteLine(
                    "  DefinitionPoint=" +
                    $"({entity.DefinitionPointX:F6}, {entity.DefinitionPointY:F6}, {entity.DefinitionPointZ:F6})"
                );
                writer.WriteLine(
                    "  TextPosition=" +
                    $"({entity.TextPositionX:F6}, {entity.TextPositionY:F6}, {entity.TextPositionZ:F6})"
                );
                break;
        }
    }


    /// <summary>
    /// 엔티티의 실제 표시 색상 ACI 번호를 구한다.
    /// 객체 색상이 ByLayer(256)이면 레이어 색상을 사용한다.
    /// ACadSharp 버전별 Color 구조 차이를 피하기 위해 리플렉션으로 읽는다.
    /// </summary>
    private static short GetEffectiveColorIndex(
        Entity entity
    )
    {
        short entityColor = ReadColorIndex(
            entity,
            "Color"
        );

        if (entityColor != 256 && entityColor >= 0)
        {
            return entityColor;
        }

        if (entity.Layer != null)
        {
            short layerColor = ReadColorIndex(
                entity.Layer,
                "Color"
            );

            if (layerColor >= 0)
            {
                return layerColor;
            }
        }

        return entityColor;
    }

    private static short ReadColorIndex(
        object source,
        string propertyName
    )
    {
        try
        {
            object? color = source
                .GetType()
                .GetProperty(propertyName)?
                .GetValue(source);

            if (color == null)
            {
                return -1;
            }

            object? indexValue = color
                .GetType()
                .GetProperty("Index")?
                .GetValue(color);

            if (indexValue == null)
            {
                return -1;
            }

            return Convert.ToInt16(indexValue);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 흰색 치구 폴리선을 각각 패널 기준으로 지정한다.
    /// 기준 외곽에서 사방 searchMargin 이내에 걸치는 모든 객체를 묶는다.
    /// 최종 포함 객체가 3개 이상인 경우에만 패널로 등록한다.
    /// </summary>
    private static List<PanelGroup> FindPanelGroups(
        DrawingData drawingData,
        double searchMargin
    )
    {
        if (searchMargin < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(searchMargin)
            );
        }

        const double minimumThicknessAspectRatio = 2.0;

        List<EntityData> allWhiteChiguPolylines = drawingData.Entities
            .Where(IsWhiteChiguPolyline)
            .Where(x => x.HasBoundingBox)
            .ToList();

        // 다른 흰색 치구 폴리선 안에 완전히 들어가는 사각형은
        // 독립 패널 기준이 아니라 내부 사각형으로 취급한다.
        List<EntityData> allPanelBases = allWhiteChiguPolylines
            .Where(candidate =>
                !allWhiteChiguPolylines.Any(other =>
                    !ReferenceEquals(candidate, other) &&
                    IsCompletelyInside(candidate, other, 0.001)
                )
            )
            .ToList();

        // 가로/세로 비율이 큰 두께 패널 기준을 먼저 처리한다.
        List<EntityData> thicknessPanelBases = allPanelBases
            .Where(x => IsThicknessPanelBase(
                x,
                minimumThicknessAspectRatio
            ))
            .OrderBy(x => x.MinX)
            .ThenByDescending(x => x.MaxY)
            .ToList();

        List<EntityData> normalPanelBases = allPanelBases
            .Where(x => !IsThicknessPanelBase(
                x,
                minimumThicknessAspectRatio
            ))
            .OrderBy(x => x.MinX)
            .ThenByDescending(x => x.MaxY)
            .ToList();

        List<PanelGroup> result = new();

        // 먼저 확정된 패널의 모든 객체 Handle을 기록한다.
        // 이후 패널 검색에서는 이 객체들을 후보와 구성원 모두에서 제외한다.
        HashSet<string> assignedHandles = new(
            StringComparer.OrdinalIgnoreCase
        );

        void TryAddPanel(
            EntityData panelBase,
            bool isThicknessPanel
        )
        {
            // 이전에 확정된 패널에 기준 폴리선이 이미 포함되어 있으면
            // 새로운 패널 기준으로 사용하지 않는다.
            if (assignedHandles.Contains(panelBase.Handle))
            {
                return;
            }

            double searchMinX = panelBase.MinX - searchMargin;
            double searchMinY = panelBase.MinY - searchMargin;
            double searchMaxX = panelBase.MaxX + searchMargin;
            double searchMaxY = panelBase.MaxY + searchMargin;

            List<EntityData> members = drawingData.Entities
                // 이미 다른 확정 패널에 들어간 객체는 자동 검색에서 제외한다.
                .Where(x => !assignedHandles.Contains(x.Handle))
                .Where(x => IsEntityInsideOrTouchingBounds(
                    x,
                    searchMinX,
                    searchMinY,
                    searchMaxX,
                    searchMaxY
                ))
                // 기준 폴리선 자신은 포함한다.
                // 다른 흰색 치구 폴리선은 기준 패널 안에 완전히 들어간
                // 내부 사각형일 때만 포함한다.
                .Where(x =>
                    ReferenceEquals(x, panelBase) ||
                    !IsWhiteChiguPolyline(x) ||
                    IsCompletelyInside(x, panelBase, 0.001)
                )
                .GroupBy(x => x.Handle)
                .Select(group => group.First())
                .ToList();

            // 기준 폴리선을 포함해 객체가 3개 이상인 경우만 패널로 확정한다.
            if (members.Count < 3)
            {
                return;
            }

            double aspectRatio = GetPanelAspectRatio(panelBase);

            PanelGroup panel = new()
            {
                Number = result.Count + 1,
                BasePolyline = panelBase,
                SearchMargin = searchMargin,
                SearchMinX = searchMinX,
                SearchMinY = searchMinY,
                SearchMaxX = searchMaxX,
                SearchMaxY = searchMaxY,
                Entities = members,
                IsThicknessPanel = isThicknessPanel,
                ThicknessDirection = isThicknessPanel
                    ? panelBase.Width > panelBase.Height
                        ? ThicknessPanelDirection.Horizontal
                        : ThicknessPanelDirection.Vertical
                    : ThicknessPanelDirection.None,
                AspectRatio = aspectRatio
            };

            result.Add(panel);

            // 이 패널에 정리된 모든 객체는 이후 패널 자동 탐색에서 제외한다.
            foreach (EntityData member in members)
            {
                assignedHandles.Add(member.Handle);
            }
        }

        // 1. 두께 패널을 먼저 찾고 객체를 선점한다.
        foreach (EntityData thicknessBase in thicknessPanelBases)
        {
            TryAddPanel(
                thicknessBase,
                true
            );
        }

        // 2. 남은 객체만 사용해 일반 패널을 찾는다.
        foreach (EntityData normalBase in normalPanelBases)
        {
            TryAddPanel(
                normalBase,
                false
            );
        }

        // 출력 순서는 도면상 왼쪽부터 다시 정렬하고 번호를 재지정한다.
        result = result
            .OrderBy(x => x.BasePolyline.MinX)
            .ThenByDescending(x => x.BasePolyline.MaxY)
            .ToList();

        for (int i = 0; i < result.Count; i++)
        {
            result[i].Number = i + 1;
        }

        return result;
    }

    private static bool IsThicknessPanelBase(
        EntityData panelBase,
        double minimumAspectRatio
    )
    {
        return GetPanelAspectRatio(panelBase) >= minimumAspectRatio;
    }

    private static double GetPanelAspectRatio(
        EntityData panelBase
    )
    {
        double longSide = Math.Max(
            panelBase.Width,
            panelBase.Height
        );

        double shortSide = Math.Min(
            panelBase.Width,
            panelBase.Height
        );

        if (shortSide <= 0.0)
        {
            return 0.0;
        }

        return longSide / shortSide;
    }


    private static bool IsCompletelyInside(
        EntityData inner,
        EntityData outer,
        double tolerance = 0.001
    )
    {
        if (!inner.HasBoundingBox || !outer.HasBoundingBox)
        {
            return false;
        }

        return
            inner.MinX >= outer.MinX - tolerance &&
            inner.MaxX <= outer.MaxX + tolerance &&
            inner.MinY >= outer.MinY - tolerance &&
            inner.MaxY <= outer.MaxY + tolerance;
    }

    /// <summary>
    /// 패널 기준 폴리선의 가로/세로 비율로 두께 패널을 선택적으로 분류한다.
    /// 두께 패널이 없는 도면에서는 모든 패널이 None으로 유지되며 예외를 발생시키지 않는다.
    /// </summary>
    private static void ClassifyThicknessPanels(
        IEnumerable<PanelGroup> panels,
        double minimumAspectRatio = 2.0
    )
    {
        if (minimumAspectRatio <= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumAspectRatio),
                "두께 패널 판별 비율은 1보다 커야 합니다."
            );
        }

        foreach (PanelGroup panel in panels)
        {
            double width = panel.BasePolyline.Width;
            double height = panel.BasePolyline.Height;

            panel.IsThicknessPanel = false;
            panel.ThicknessDirection = ThicknessPanelDirection.None;
            panel.AspectRatio = 0.0;

            if (width <= 0.0 || height <= 0.0)
            {
                continue;
            }

            double longSide = Math.Max(width, height);
            double shortSide = Math.Min(width, height);

            if (shortSide <= 0.0)
            {
                continue;
            }

            double aspectRatio = longSide / shortSide;
            panel.AspectRatio = aspectRatio;

            // 가로와 세로 차이가 충분하지 않으면 일반 패널이다.
            if (aspectRatio < minimumAspectRatio)
            {
                continue;
            }

            panel.IsThicknessPanel = true;
            panel.ThicknessDirection =
                width > height
                    ? ThicknessPanelDirection.Horizontal
                    : ThicknessPanelDirection.Vertical;
        }
    }

    private static bool IsWhiteChiguPolyline(
        EntityData entity
    )
    {
        bool isPolyline =
            entity.ObjectName == "LWPOLYLINE" ||
            entity.ObjectName == "POLYLINE2D";

        return
            entity.LayerName == "치구" &&
            isPolyline &&
            entity.ColorIndex == 7;
    }

    /// <summary>
    /// 객체의 바운딩박스가 패널 검색 범위와 조금이라도 겹치면 포함한다.
    /// 따라서 기준 폴리선 내부 객체뿐 아니라 외곽에서 12 이내 객체도 포함된다.
    /// </summary>
    private static bool IsEntityInsideOrTouchingBounds(
        EntityData entity,
        double minX,
        double minY,
        double maxX,
        double maxY
    )
    {
        if (entity.HasBoundingBox)
        {
            return
                entity.MaxX >= minX &&
                entity.MinX <= maxX &&
                entity.MaxY >= minY &&
                entity.MinY <= maxY;
        }

        // 바운딩박스가 없는 객체는 대표 좌표가 범위 안인지 확인한다.
        (double X, double Y)? point =
            GetRepresentativePoint(entity);

        if (point == null)
        {
            return false;
        }

        return
            point.Value.X >= minX &&
            point.Value.X <= maxX &&
            point.Value.Y >= minY &&
            point.Value.Y <= maxY;
    }

    private static (double X, double Y)? GetRepresentativePoint(
        EntityData entity
    )
    {
        return entity.ObjectName switch
        {
            "CIRCLE" or "ARC" =>
                (entity.CenterX, entity.CenterY),

            "TEXT" or "MTEXT" or "INSERT" =>
                (entity.InsertX, entity.InsertY),

            "LINE" =>
                ((entity.StartX + entity.EndX) / 2.0,
                 (entity.StartY + entity.EndY) / 2.0),

            "DIMENSION" =>
                (entity.TextPositionX, entity.TextPositionY),

            _ => null
        };
    }

    private static void SavePanelGroupReport(
        IReadOnlyList<PanelGroup> panelGroups,
        string outputPath
    )
    {
        using StreamWriter writer = new(
            outputPath,
            false
        );

        writer.WriteLine($"찾은 패널 수: {panelGroups.Count}");
        writer.WriteLine("기준: Layer=치구, POLYLINE, ColorIndex=7(흰색)");
        writer.WriteLine("포함 범위: 기준 폴리선 바운딩박스 사방 12");
        writer.WriteLine();

        foreach (PanelGroup panel in panelGroups)
        {
            writer.WriteLine($"[패널 {panel.Number}]");
            writer.WriteLine(
                $"기준 Handle={panel.BasePolyline.Handle}, " +
                $"Type={panel.BasePolyline.ObjectName}, " +
                $"Layer={panel.BasePolyline.LayerName}, " +
                $"ColorIndex={panel.BasePolyline.ColorIndex}"
            );
            writer.WriteLine(
                "기준 Bounds=" +
                $"({panel.BasePolyline.MinX:F6}, {panel.BasePolyline.MinY:F6}) ~ " +
                $"({panel.BasePolyline.MaxX:F6}, {panel.BasePolyline.MaxY:F6})"
            );
            writer.WriteLine(
                "검색 Bounds=" +
                $"({panel.SearchMinX:F6}, {panel.SearchMinY:F6}) ~ " +
                $"({panel.SearchMaxX:F6}, {panel.SearchMaxY:F6})"
            );
            writer.WriteLine($"묶인 객체 수={panel.Entities.Count}");
            writer.WriteLine(
                $"두께 패널={panel.IsThicknessPanel}, " +
                $"방향={panel.ThicknessDirection}, " +
                $"가로세로비={panel.AspectRatio:F3}"
            );

            foreach (EntityData entity in panel.Entities)
            {
                writer.WriteLine(
                    $"  Handle={entity.Handle}, " +
                    $"Type={entity.ObjectName}, " +
                    $"Layer={entity.LayerName}, " +
                    $"ColorIndex={entity.ColorIndex}"
                );
            }

            writer.WriteLine();
        }
    }


    /// <summary>
    /// 일반 패널 중 면적이 가장 큰 닫힌 사각형 패널을 기준으로 X/Y 치수를 읽고,
    /// 두께 패널이 존재하면 그 패널의 짧은 방향 치수를 두께값으로 읽는다.
    /// </summary>
    private static CurrentDimensionValues ReadCurrentDimensionValues(
        IReadOnlyList<PanelGroup> panelGroups
    )
    {
        PanelGroup? sourcePanel = panelGroups
            .Where(x => !x.IsThicknessPanel)
            .Where(x => x.BasePolyline.IsClosed)
            .Where(x => x.BasePolyline.Vertices.Count == 4)
            .OrderByDescending(x =>
                x.BasePolyline.Width * x.BasePolyline.Height
            )
            .FirstOrDefault();

        if (sourcePanel == null)
        {
            throw new Exception(
                "가로·세로 현재값을 읽을 가장 큰 사각형 패널을 찾지 못했습니다."
            );
        }

        List<DimensionValueInfo> sourceDimensions =
            GetPanelDimensionValues(sourcePanel);

        DimensionValueInfo? widthDimension = sourceDimensions
            .Where(x => x.Direction == DimensionDirection.Horizontal)
            .OrderByDescending(x => x.Value)
            .FirstOrDefault();

        DimensionValueInfo? heightDimension = sourceDimensions
            .Where(x => x.Direction == DimensionDirection.Vertical)
            .OrderByDescending(x => x.Value)
            .FirstOrDefault();

        if (widthDimension == null)
        {
            throw new Exception(
                $"{sourcePanel.Number}번 기준 패널에서 가로 치수값을 찾지 못했습니다."
            );
        }

        if (heightDimension == null)
        {
            throw new Exception(
                $"{sourcePanel.Number}번 기준 패널에서 세로 치수값을 찾지 못했습니다."
            );
        }

        double? thickness = null;
        PanelGroup? thicknessSourcePanel = null;
        DimensionValueInfo? thicknessDimension = null;

        foreach (PanelGroup panel in panelGroups
            .Where(x => x.IsThicknessPanel))
        {
            DimensionDirection shortDirection =
                panel.ThicknessDirection == ThicknessPanelDirection.Horizontal
                    ? DimensionDirection.Vertical
                    : DimensionDirection.Horizontal;

            DimensionValueInfo? candidate = GetPanelDimensionValues(panel)
                .Where(x => x.Direction == shortDirection)
                .OrderByDescending(x => x.Value)
                .FirstOrDefault();

            if (candidate == null)
            {
                continue;
            }

            // 두께 패널이 여러 개면 짧은 방향 치수 중 가장 큰 값을 사용한다.
            if (!thickness.HasValue || candidate.Value > thickness.Value)
            {
                thickness = candidate.Value;
                thicknessSourcePanel = panel;
                thicknessDimension = candidate;
            }
        }

        return new CurrentDimensionValues
        {
            Width = widthDimension.Value,
            Height = heightDimension.Value,
            Thickness = thickness,
            SourcePanel = sourcePanel,
            WidthDimension = widthDimension,
            HeightDimension = heightDimension,
            ThicknessPanel = thicknessSourcePanel,
            ThicknessDimension = thicknessDimension
        };
    }

    private static List<DimensionValueInfo> GetPanelDimensionValues(
        PanelGroup panel
    )
    {
        List<DimensionValueInfo> result = new();

        foreach (EntityData entity in panel.Entities)
        {
            if (entity.Entity is not Dimension dimension)
            {
                continue;
            }

            if (!TryReadDimensionValue(entity, dimension, out double value))
            {
                continue;
            }

            DimensionDirection direction = GetDimensionDirection(entity);

            if (direction == DimensionDirection.Unknown)
            {
                continue;
            }

            result.Add(new DimensionValueInfo
            {
                Dimension = entity,
                Value = value,
                Direction = direction
            });
        }

        return result;
    }

    /// <summary>
    /// 사용자 치수 문자에 숫자가 직접 들어 있으면 그 값을 우선 사용한다.
    /// 문자가 비어 있거나 &lt;&gt;이면 ACadSharp의 Measurement 속성을 읽고,
    /// 지원되지 않으면 정렬 치수의 두 측정점 거리를 사용한다.
    /// </summary>
    private static bool TryReadDimensionValue(
        EntityData data,
        Dimension dimension,
        out double value
    )
    {
        value = 0.0;

        if (TryParseNumericDimensionText(data.TextValue, out value))
        {
            return value > 0.0 && double.IsFinite(value);
        }

        try
        {
            object? measurement = dimension
                .GetType()
                .GetProperty("Measurement")?
                .GetValue(dimension);

            if (measurement != null)
            {
                double measuredValue = Convert.ToDouble(
                    measurement,
                    CultureInfo.InvariantCulture
                );

                if (measuredValue > 0.0 && double.IsFinite(measuredValue))
                {
                    value = measuredValue;
                    return true;
                }
            }
        }
        catch
        {
            // 현재 ACadSharp 버전에서 Measurement를 지원하지 않으면 아래 거리값 사용
        }

        if (dimension is DimensionAligned)
        {
            double dx = data.SecondPointX - data.FirstPointX;
            double dy = data.SecondPointY - data.FirstPointY;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > 0.0 && double.IsFinite(distance))
            {
                value = distance;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseNumericDimensionText(
        string text,
        out double value
    )
    {
        value = 0.0;

        if (string.IsNullOrWhiteSpace(text) ||
            text.Trim() == "<>")
        {
            return false;
        }

        Match match = Regex.Match(
            text,
            @"[-+]?\d+(?:[.,]\d+)?"
        );

        if (!match.Success)
        {
            return false;
        }

        string normalized = match.Value.Replace(',', '.');

        return double.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    private static DimensionDirection GetDimensionDirection(
        EntityData dimension
    )
    {
        double dx = Math.Abs(
            dimension.SecondPointX - dimension.FirstPointX
        );

        double dy = Math.Abs(
            dimension.SecondPointY - dimension.FirstPointY
        );

        const double tolerance = 0.000001;

        if (dx <= tolerance && dy <= tolerance)
        {
            return DimensionDirection.Unknown;
        }

        return dx >= dy
            ? DimensionDirection.Horizontal
            : DimensionDirection.Vertical;
    }

    private static ResizeInput? ShowResizeInputDialog(
        CurrentDimensionValues current
    )
    {
        using Form form = new()
        {
            Text = "수정할 크기 입력",
            Width = 420,
            Height = 350,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        Label currentLabel = new()
        {
            Left = 25,
            Top = 20,
            Width = 360,
            Height = 42,
            Text = current.Thickness.HasValue
                ? $"현재 치수: {current.Width:0.###} x {current.Height:0.###} x {current.Thickness.Value:0.###}"
                : $"현재 치수: {current.Width:0.###} x {current.Height:0.###} x 두께 패널 없음"
        };

        Label widthLabel = new()
        {
            Left = 25,
            Top = 85,
            Width = 110,
            Text = "목표 X(가로)"
        };

        NumericUpDown widthInput = CreateSizeInput(
            150,
            80,
            current.Width
        );

        Label heightLabel = new()
        {
            Left = 25,
            Top = 130,
            Width = 110,
            Text = "목표 Y(세로)"
        };

        NumericUpDown heightInput = CreateSizeInput(
            150,
            125,
            current.Height
        );

        Label thicknessLabel = new()
        {
            Left = 25,
            Top = 175,
            Width = 110,
            Text = "목표 높이(두께)"
        };

        NumericUpDown thicknessInput = CreateSizeInput(
            150,
            170,
            current.Thickness ?? 0.001
        );

        if (!current.Thickness.HasValue)
        {
            thicknessInput.Enabled = false;
            thicknessLabel.Text = "두께 패널 없음";
        }

        Button okButton = new()
        {
            Left = 170,
            Top = 245,
            Width = 90,
            Height = 34,
            Text = "확인",
            DialogResult = DialogResult.OK
        };

        Button cancelButton = new()
        {
            Left = 270,
            Top = 245,
            Width = 90,
            Height = 34,
            Text = "취소",
            DialogResult = DialogResult.Cancel
        };

        form.Controls.AddRange(
        [
            currentLabel,
            widthLabel,
            widthInput,
            heightLabel,
            heightInput,
            thicknessLabel,
            thicknessInput,
            okButton,
            cancelButton
        ]);

        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        if (form.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        return new ResizeInput
        {
            TargetWidth = (double)widthInput.Value,
            TargetHeight = (double)heightInput.Value,
            TargetThickness = current.Thickness.HasValue
                ? (double)thicknessInput.Value
                : null
        };
    }

    private static NumericUpDown CreateSizeInput(
        int left,
        int top,
        double currentValue
    )
    {
        decimal safeValue = (decimal)Math.Clamp(
            currentValue,
            0.001,
            1000000.0
        );

        return new NumericUpDown
        {
            Left = left,
            Top = top,
            Width = 210,
            DecimalPlaces = 3,
            Minimum = 0.001m,
            Maximum = 1000000m,
            Increment = 1m,
            Value = safeValue
        };
    }


    /// <summary>
    /// 모든 패널을 중심 기준으로 수정한다.
    /// 일반 패널은 X/Y 변화량을 사용하고, 두께 패널은 긴 방향과 짧은 방향을 구분한다.
    /// "볼트 구멍" 레이어 객체는 크기와 위치를 모두 유지한다.
    /// </summary>
    private static void ApplyPanelResize(
        IReadOnlyList<PanelGroup> panelGroups,
        CurrentDimensionValues current,
        ResizeInput input
    )
    {
        double widthDelta = input.TargetWidth - current.Width;
        double heightDelta = input.TargetHeight - current.Height;
        double thicknessDelta =
            input.TargetThickness.HasValue && current.Thickness.HasValue
                ? input.TargetThickness.Value - current.Thickness.Value
                : 0.0;

        HashSet<string> processedHandles = new();

        foreach (PanelGroup panel in panelGroups)
        {
            double panelWidthDelta;
            double panelHeightDelta;

            if (!panel.IsThicknessPanel)
            {
                panelWidthDelta = widthDelta;
                panelHeightDelta = heightDelta;
            }
            else if (panel.ThicknessDirection == ThicknessPanelDirection.Horizontal)
            {
                panelWidthDelta = widthDelta;
                panelHeightDelta = thicknessDelta;
            }
            else if (panel.ThicknessDirection == ThicknessPanelDirection.Vertical)
            {
                panelWidthDelta = thicknessDelta;
                panelHeightDelta = heightDelta;
            }
            else
            {
                continue;
            }

            ResizeSinglePanel(
                panel,
                panelWidthDelta,
                panelHeightDelta,
                processedHandles
            );
        }
    }

    private static void ResizeSinglePanel(
        PanelGroup panel,
        double widthDelta,
        double heightDelta,
        ISet<string> processedHandles
    )
    {
        EntityData basePolyline = panel.BasePolyline;

        DimensionBounds oldBounds = new(
            basePolyline.MinX,
            basePolyline.MinY,
            basePolyline.MaxX,
            basePolyline.MaxY
        );

        DimensionBounds newBounds = new(
            oldBounds.MinX - widthDelta / 2.0,
            oldBounds.MinY - heightDelta / 2.0,
            oldBounds.MaxX + widthDelta / 2.0,
            oldBounds.MaxY + heightDelta / 2.0
        );

        ResizePolylineFromCenter(
            basePolyline,
            widthDelta,
            heightDelta
        );

        processedHandles.Add(basePolyline.Handle);

        foreach (EntityData entity in panel.Entities)
        {
            if (ReferenceEquals(entity, basePolyline))
            {
                continue;
            }

            if (!processedHandles.Add(entity.Handle))
            {
                continue;
            }

            if (string.Equals(
                    entity.LayerName,
                    "볼트 구멍",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entity.Entity is DimensionAligned dimension)
            {
                ResizeDimensionByBounds(
                    dimension,
                    oldBounds,
                    newBounds
                );

                continue;
            }

            // 패널 내부에 있는 흰색 닫힌 사각형은 단순 이동하지 않고
            // 기준 패널과 같은 가로/세로 변화량으로 중심 기준 크기를 변경한다.
            if (IsInnerWhiteRectangle(panel, entity))
            {
                ResizePolylineFromCenter(
                    entity,
                    widthDelta,
                    heightDelta
                );

                continue;
            }

            double moveX = GetDirectionalMove(
                GetEntityCenterX(entity),
                basePolyline.CenterBoxX,
                widthDelta
            );

            double moveY = GetDirectionalMove(
                GetEntityCenterY(entity),
                basePolyline.CenterBoxY,
                heightDelta
            );

            MoveEntity(
                entity.Entity,
                moveX,
                moveY
            );
        }
    }

    /// <summary>
    /// 패널 기준 폴리선 안쪽에 있는 흰색 닫힌 사각형인지 확인한다.
    /// 기준 폴리선 자기 자신과 볼트 구멍 레이어는 제외한다.
    /// </summary>
    private static bool IsInnerWhiteRectangle(
        PanelGroup panel,
        EntityData entity
    )
    {
        if (ReferenceEquals(entity, panel.BasePolyline))
        {
            return false;
        }

        if (string.Equals(
                entity.LayerName,
                "볼트 구멍",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (entity.Entity is not LwPolyline polyline)
        {
            return false;
        }

        if (!polyline.IsClosed || polyline.Vertices.Count != 4)
        {
            return false;
        }

        if (entity.ColorIndex != 7)
        {
            return false;
        }

        // 실제 외곽 범위가 기준 패널 안에 완전히 들어간 경우만 내부 사각형으로 본다.
        const double tolerance = 0.001;

        return
            entity.MinX >= panel.BasePolyline.MinX - tolerance &&
            entity.MaxX <= panel.BasePolyline.MaxX + tolerance &&
            entity.MinY >= panel.BasePolyline.MinY - tolerance &&
            entity.MaxY <= panel.BasePolyline.MaxY + tolerance;
    }

    private static void ResizePolylineFromCenter(
        EntityData data,
        double widthDelta,
        double heightDelta
    )
    {
        double halfWidthDelta = widthDelta / 2.0;
        double halfHeightDelta = heightDelta / 2.0;
        const double tolerance = 0.000001;

        switch (data.Entity)
        {
            case LwPolyline polyline:
                foreach (LwPolyline.Vertex vertex in polyline.Vertices)
                {
                    double x = vertex.Location.X;
                    double y = vertex.Location.Y;

                    if (x < data.CenterBoxX - tolerance)
                    {
                        x -= halfWidthDelta;
                    }
                    else if (x > data.CenterBoxX + tolerance)
                    {
                        x += halfWidthDelta;
                    }

                    if (y < data.CenterBoxY - tolerance)
                    {
                        y -= halfHeightDelta;
                    }
                    else if (y > data.CenterBoxY + tolerance)
                    {
                        y += halfHeightDelta;
                    }

                    vertex.Location = new XY(x, y);
                }
                break;

            case Polyline2D polyline:
                foreach (var vertex in polyline.Vertices)
                {
                    double x = vertex.Location.X;
                    double y = vertex.Location.Y;

                    if (x < data.CenterBoxX - tolerance)
                    {
                        x -= halfWidthDelta;
                    }
                    else if (x > data.CenterBoxX + tolerance)
                    {
                        x += halfWidthDelta;
                    }

                    if (y < data.CenterBoxY - tolerance)
                    {
                        y -= halfHeightDelta;
                    }
                    else if (y > data.CenterBoxY + tolerance)
                    {
                        y += halfHeightDelta;
                    }

                    vertex.Location = new XYZ(x, y, vertex.Location.Z);
                }
                break;

            default:
                throw new Exception(
                    $"패널 기준 객체가 폴리선이 아닙니다. Handle={data.Handle}"
                );
        }
    }

    private static double GetDirectionalMove(
        double value,
        double center,
        double delta
    )
    {
        const double tolerance = 0.000001;

        if (value < center - tolerance)
        {
            return -delta / 2.0;
        }

        if (value > center + tolerance)
        {
            return delta / 2.0;
        }

        return 0.0;
    }

    private static double GetEntityCenterX(EntityData entity)
    {
        if (entity.Entity is Circle or Arc)
        {
            return entity.CenterX;
        }

        if (entity.Entity is TextEntity or MText or Insert)
        {
            return entity.InsertX;
        }

        return entity.CenterBoxX;
    }

    private static double GetEntityCenterY(EntityData entity)
    {
        if (entity.Entity is Circle or Arc)
        {
            return entity.CenterY;
        }

        if (entity.Entity is TextEntity or MText or Insert)
        {
            return entity.InsertY;
        }

        return entity.CenterBoxY;
    }

    private static void ResizeDimensionByBounds(
        DimensionAligned dimension,
        DimensionBounds oldBounds,
        DimensionBounds newBounds
    )
    {
        dimension.FirstPoint = MapPointByBounds(
            dimension.FirstPoint,
            oldBounds,
            newBounds
        );

        dimension.SecondPoint = MapPointByBounds(
            dimension.SecondPoint,
            oldBounds,
            newBounds
        );

        dimension.DefinitionPoint = MapPointByBounds(
            dimension.DefinitionPoint,
            oldBounds,
            newBounds
        );

        dimension.TextMiddlePoint = MapPointByBounds(
            dimension.TextMiddlePoint,
            oldBounds,
            newBounds
        );

        // 숫자가 직접 입력된 치수 문자는 기존 숫자가 남으므로 자동 측정값으로 되돌린다.
        dimension.Text = string.Empty;
        dimension.UpdateBlock();
    }

    private static XYZ MapPointByBounds(
        XYZ point,
        DimensionBounds oldBounds,
        DimensionBounds newBounds
    )
    {
        return new XYZ(
            MapCoordinateByBounds(
                point.X,
                oldBounds.MinX,
                oldBounds.MaxX,
                newBounds.MinX,
                newBounds.MaxX
            ),
            MapCoordinateByBounds(
                point.Y,
                oldBounds.MinY,
                oldBounds.MaxY,
                newBounds.MinY,
                newBounds.MaxY
            ),
            point.Z
        );
    }

    private static double MapCoordinateByBounds(
        double value,
        double oldMin,
        double oldMax,
        double newMin,
        double newMax
    )
    {
        const double tolerance = 0.000001;

        if (Math.Abs(value - oldMin) <= tolerance)
        {
            return newMin;
        }

        if (Math.Abs(value - oldMax) <= tolerance)
        {
            return newMax;
        }

        if (value < oldMin)
        {
            return newMin - (oldMin - value);
        }

        if (value > oldMax)
        {
            return newMax + (value - oldMax);
        }

        double oldLength = oldMax - oldMin;

        if (Math.Abs(oldLength) <= tolerance)
        {
            return (newMin + newMax) / 2.0;
        }

        double ratio = (value - oldMin) / oldLength;
        return newMin + ratio * (newMax - newMin);
    }

    /// <summary>
    /// 일반 패널과 그 패널에 연결된 두께 패널을 하나의 조립 묶음으로 배치한다.
    /// 먼저 두께 패널을 일반 패널의 위/아래/좌/우에 배치한 뒤,
    /// 조립 묶음 전체 외곽의 오른쪽 끝과 다음 조립 묶음 전체 외곽의 왼쪽 끝 사이가
    /// 정확히 gap이 되도록 X축 방향으로 정렬한다.
    /// </summary>
    private static void ArrangePanelGroupsWithGap(
        IReadOnlyList<PanelGroup> panelGroups,
        double gap
    )
    {
        if (gap < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap));
        }

        // 원래 도면에서의 일반 패널 좌우 순서를 먼저 기억한다.
        List<PanelGroup> mainPanels = panelGroups
            .Where(panel => !panel.IsThicknessPanel)
            .OrderBy(panel => GetCurrentBounds(panel.BasePolyline.Entity).MinX)
            .ThenBy(panel => GetCurrentBounds(panel.BasePolyline.Entity).MinY)
            .ToList();

        if (mainPanels.Count == 0)
        {
            return;
        }

        List<PanelGroup> thicknessPanels = panelGroups
            .Where(panel => panel.IsThicknessPanel)
            .ToList();

        // 두께 패널이 원래 어느 일반 패널 주변에 있었는지 먼저 연결한다.
        Dictionary<PanelGroup, List<PanelGroup>> thicknessBindings =
            BindThicknessPanelsToMainPanels(
                mainPanels,
                thicknessPanels
            );

        // 먼저 각 일반 패널 주변에 연결된 두께 패널을
        // 위/아래/좌/우 50 간격 위치로 확정한다.
        foreach (PanelGroup mainPanel in mainPanels)
        {
            if (!thicknessBindings.TryGetValue(
                    mainPanel,
                    out List<PanelGroup>? attachedPanels) ||
                attachedPanels.Count == 0)
            {
                continue;
            }

            ArrangeAttachedThicknessPanels(
                mainPanel,
                attachedPanels,
                gap
            );
        }

        // 첫 번째 조립 묶음은 현재 위치에 고정한다.
        DimensionBounds previousAssemblyBounds =
            GetAssemblyBounds(
                mainPanels[0],
                thicknessBindings[mainPanels[0]]
            );

        // 두 번째 조립 묶음부터 이전 조립 묶음 전체 오른쪽 끝에서 gap만큼 띄운다.
        for (int index = 1; index < mainPanels.Count; index++)
        {
            PanelGroup mainPanel = mainPanels[index];
            List<PanelGroup> attachedPanels =
                thicknessBindings[mainPanel];

            DimensionBounds currentAssemblyBounds =
                GetAssemblyBounds(
                    mainPanel,
                    attachedPanels
                );

            double targetMinX =
                previousAssemblyBounds.MaxX + gap;

            double moveX =
                targetMinX - currentAssemblyBounds.MinX;

            MovePanelAssembly(
                mainPanel,
                attachedPanels,
                moveX,
                0.0
            );

            previousAssemblyBounds =
                GetAssemblyBounds(
                    mainPanel,
                    attachedPanels
                );
        }
    }

    /// <summary>
    /// 일반 패널과 연결된 모든 두께 패널의 실제 현재 외곽을 합쳐
    /// 하나의 조립 묶음 바운딩박스로 계산한다.
    /// 치수는 패널과 함께 이동하지만 간격 계산 외곽에서는 제외한다.
    /// </summary>
    private static DimensionBounds GetAssemblyBounds(
        PanelGroup mainPanel,
        IReadOnlyList<PanelGroup> attachedPanels
    )
    {
        List<PanelGroup> allPanels = new()
        {
            mainPanel
        };

        allPanels.AddRange(attachedPanels);

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        bool found = false;

        foreach (EntityData entity in allPanels
            .SelectMany(panel => panel.Entities)
            .GroupBy(entity => entity.Handle)
            .Select(group => group.First()))
        {
            if (entity.Entity is Dimension)
            {
                continue;
            }

            try
            {
                var box = entity.Entity.GetBoundingBox();

                minX = Math.Min(minX, box.Min.X);
                minY = Math.Min(minY, box.Min.Y);
                maxX = Math.Max(maxX, box.Max.X);
                maxY = Math.Max(maxY, box.Max.Y);
                found = true;
            }
            catch
            {
                // 외곽을 계산할 수 없는 객체는 간격 계산에서 제외한다.
            }
        }

        if (!found)
        {
            throw new Exception(
                $"{mainPanel.Number}번 조립 패널의 현재 외곽을 계산하지 못했습니다."
            );
        }

        return new DimensionBounds(
            minX,
            minY,
            maxX,
            maxY
        );
    }

    /// <summary>
    /// 일반 패널과 그 패널에 연결된 모든 두께 패널을 같은 거리만큼 이동한다.
    /// 같은 Handle이 중복으로 들어 있어도 한 번만 이동한다.
    /// </summary>
    private static void MovePanelAssembly(
        PanelGroup mainPanel,
        IReadOnlyList<PanelGroup> attachedPanels,
        double moveX,
        double moveY
    )
    {
        List<PanelGroup> allPanels = new()
        {
            mainPanel
        };

        allPanels.AddRange(attachedPanels);

        foreach (EntityData entity in allPanels
            .SelectMany(panel => panel.Entities)
            .GroupBy(entity => entity.Handle)
            .Select(group => group.First()))
        {
            MoveEntity(
                entity.Entity,
                moveX,
                moveY
            );
        }
    }

    /// <summary>
    /// 각 두께 패널을 수정 전 위치에서 가장 가까운 일반 패널에 연결한다.
    /// 단순 중심 거리 대신 두 외곽 사이의 실제 최소 거리를 사용한다.
    /// </summary>
    private static Dictionary<PanelGroup, List<PanelGroup>>
        BindThicknessPanelsToMainPanels(
            IReadOnlyList<PanelGroup> mainPanels,
            IReadOnlyList<PanelGroup> thicknessPanels
        )
    {
        Dictionary<PanelGroup, List<PanelGroup>> result =
            mainPanels.ToDictionary(
                panel => panel,
                _ => new List<PanelGroup>()
            );

        foreach (PanelGroup thicknessPanel in thicknessPanels)
        {
            DimensionBounds thicknessBounds =
                GetCurrentBounds(thicknessPanel.BasePolyline.Entity);

            PanelGroup nearestMainPanel = mainPanels
                .OrderBy(mainPanel =>
                    GetBoundsDistanceSquared(
                        thicknessBounds,
                        GetCurrentBounds(mainPanel.BasePolyline.Entity)
                    ))
                .ThenBy(mainPanel =>
                {
                    DimensionBounds mainBounds =
                        GetCurrentBounds(mainPanel.BasePolyline.Entity);

                    double thicknessCenterX =
                        (thicknessBounds.MinX + thicknessBounds.MaxX) / 2.0;

                    double thicknessCenterY =
                        (thicknessBounds.MinY + thicknessBounds.MaxY) / 2.0;

                    double mainCenterX =
                        (mainBounds.MinX + mainBounds.MaxX) / 2.0;

                    double mainCenterY =
                        (mainBounds.MinY + mainBounds.MaxY) / 2.0;

                    double dx = thicknessCenterX - mainCenterX;
                    double dy = thicknessCenterY - mainCenterY;

                    return dx * dx + dy * dy;
                })
                .First();

            result[nearestMainPanel].Add(thicknessPanel);
        }

        return result;
    }

    private static double GetBoundsDistanceSquared(
        DimensionBounds first,
        DimensionBounds second
    )
    {
        double dx = 0.0;
        double dy = 0.0;

        if (first.MaxX < second.MinX)
        {
            dx = second.MinX - first.MaxX;
        }
        else if (second.MaxX < first.MinX)
        {
            dx = first.MinX - second.MaxX;
        }

        if (first.MaxY < second.MinY)
        {
            dy = second.MinY - first.MaxY;
        }
        else if (second.MaxY < first.MinY)
        {
            dy = first.MinY - second.MaxY;
        }

        return dx * dx + dy * dy;
    }

    /// <summary>
    /// 가로형 두께 패널은 일반 패널 중심 X에 맞춘 뒤 각각 위/아래로 배치한다.
    /// 세로형 두께 패널은 일반 패널 중심 Y에 맞춘 뒤 각각 왼쪽/오른쪽으로 배치한다.
    /// 같은 방향에 여러 개가 있으면 바깥쪽으로 gap 간격을 두고 연속 배치한다.
    /// </summary>
    private static void ArrangeAttachedThicknessPanels(
        PanelGroup mainPanel,
        IReadOnlyList<PanelGroup> attachedPanels,
        double gap
    )
    {
        DimensionBounds mainBounds =
            GetCurrentBounds(mainPanel.BasePolyline.Entity);

        double mainCenterX =
            (mainBounds.MinX + mainBounds.MaxX) / 2.0;

        double mainCenterY =
            (mainBounds.MinY + mainBounds.MaxY) / 2.0;

        List<PanelGroup> horizontalPanels = attachedPanels
            .Where(panel =>
                panel.ThicknessDirection == ThicknessPanelDirection.Horizontal)
            .OrderBy(panel =>
            {
                DimensionBounds bounds =
                    GetCurrentBounds(panel.BasePolyline.Entity);

                return (bounds.MinY + bounds.MaxY) / 2.0;
            })
            .ToList();

        List<PanelGroup> verticalPanels = attachedPanels
            .Where(panel =>
                panel.ThicknessDirection == ThicknessPanelDirection.Vertical)
            .OrderBy(panel =>
            {
                DimensionBounds bounds =
                    GetCurrentBounds(panel.BasePolyline.Entity);

                return (bounds.MinX + bounds.MaxX) / 2.0;
            })
            .ToList();

        // 가로형 두께 패널은 원래 일반 패널보다 아래에 있던 것은 아래,
        // 위에 있던 것은 위에 둔다. 둘이 있으면 자연스럽게 하나씩 위/아래로 간다.
        List<PanelGroup> bottomPanels = horizontalPanels
            .Where(panel =>
            {
                DimensionBounds bounds =
                    GetCurrentBounds(panel.BasePolyline.Entity);

                double centerY =
                    (bounds.MinY + bounds.MaxY) / 2.0;

                return centerY < mainCenterY;
            })
            .OrderByDescending(panel =>
                GetCurrentBounds(panel.BasePolyline.Entity).MaxY)
            .ToList();

        List<PanelGroup> topPanels = horizontalPanels
            .Where(panel => !bottomPanels.Contains(panel))
            .OrderBy(panel =>
                GetCurrentBounds(panel.BasePolyline.Entity).MinY)
            .ToList();

        double nextTopMinY = mainBounds.MaxY + gap;

        foreach (PanelGroup panel in topPanels)
        {
            DimensionBounds bounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            double panelCenterX =
                (bounds.MinX + bounds.MaxX) / 2.0;

            double moveX = mainCenterX - panelCenterX;
            double moveY = nextTopMinY - bounds.MinY;

            MovePanelGroup(panel, moveX, moveY);

            DimensionBounds movedBounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            nextTopMinY = movedBounds.MaxY + gap;
        }

        double nextBottomMaxY = mainBounds.MinY - gap;

        foreach (PanelGroup panel in bottomPanels)
        {
            DimensionBounds bounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            double panelCenterX =
                (bounds.MinX + bounds.MaxX) / 2.0;

            double moveX = mainCenterX - panelCenterX;
            double moveY = nextBottomMaxY - bounds.MaxY;

            MovePanelGroup(panel, moveX, moveY);

            DimensionBounds movedBounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            nextBottomMaxY = movedBounds.MinY - gap;
        }

        // 세로형 두께 패널도 원래 좌우 방향을 유지한다.
        List<PanelGroup> leftPanels = verticalPanels
            .Where(panel =>
            {
                DimensionBounds bounds =
                    GetCurrentBounds(panel.BasePolyline.Entity);

                double centerX =
                    (bounds.MinX + bounds.MaxX) / 2.0;

                return centerX < mainCenterX;
            })
            .OrderByDescending(panel =>
                GetCurrentBounds(panel.BasePolyline.Entity).MaxX)
            .ToList();

        List<PanelGroup> rightPanels = verticalPanels
            .Where(panel => !leftPanels.Contains(panel))
            .OrderBy(panel =>
                GetCurrentBounds(panel.BasePolyline.Entity).MinX)
            .ToList();

        double nextLeftMaxX = mainBounds.MinX - gap;

        foreach (PanelGroup panel in leftPanels)
        {
            DimensionBounds bounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            double panelCenterY =
                (bounds.MinY + bounds.MaxY) / 2.0;

            double moveX = nextLeftMaxX - bounds.MaxX;
            double moveY = mainCenterY - panelCenterY;

            MovePanelGroup(panel, moveX, moveY);

            DimensionBounds movedBounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            nextLeftMaxX = movedBounds.MinX - gap;
        }

        double nextRightMinX = mainBounds.MaxX + gap;

        foreach (PanelGroup panel in rightPanels)
        {
            DimensionBounds bounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            double panelCenterY =
                (bounds.MinY + bounds.MaxY) / 2.0;

            double moveX = nextRightMinX - bounds.MinX;
            double moveY = mainCenterY - panelCenterY;

            MovePanelGroup(panel, moveX, moveY);

            DimensionBounds movedBounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            nextRightMinX = movedBounds.MaxX + gap;
        }
    }

    private static void MovePanelGroup(
        PanelGroup panel,
        double moveX,
        double moveY
    )
    {
        foreach (EntityData entity in panel.Entities
            .GroupBy(x => x.Handle)
            .Select(group => group.First()))
        {
            MoveEntity(entity.Entity, moveX, moveY);
        }

        if (!panel.Entities.Any(x => x.Handle == panel.BasePolyline.Handle))
        {
            MoveEntity(panel.BasePolyline.Entity, moveX, moveY);
        }
    }

    private static DimensionBounds GetCurrentBounds(Entity entity)
    {
        var box = entity.GetBoundingBox();

        return new DimensionBounds(
            box.Min.X,
            box.Min.Y,
            box.Max.X,
            box.Max.Y
        );
    }

    private static void MoveEntity(
        Entity entity,
        double moveX,
        double moveY
    )
    {
        switch (entity)
        {
            case DimensionAligned dimension:
                dimension.FirstPoint = new XYZ(
                    dimension.FirstPoint.X + moveX,
                    dimension.FirstPoint.Y + moveY,
                    dimension.FirstPoint.Z
                );

                dimension.SecondPoint = new XYZ(
                    dimension.SecondPoint.X + moveX,
                    dimension.SecondPoint.Y + moveY,
                    dimension.SecondPoint.Z
                );

                dimension.DefinitionPoint = new XYZ(
                    dimension.DefinitionPoint.X + moveX,
                    dimension.DefinitionPoint.Y + moveY,
                    dimension.DefinitionPoint.Z
                );

                dimension.TextMiddlePoint = new XYZ(
                    dimension.TextMiddlePoint.X + moveX,
                    dimension.TextMiddlePoint.Y + moveY,
                    dimension.TextMiddlePoint.Z
                );

                dimension.UpdateBlock();
                break;

            case LwPolyline polyline:
                foreach (LwPolyline.Vertex vertex in polyline.Vertices)
                {
                    vertex.Location = new XY(
                        vertex.Location.X + moveX,
                        vertex.Location.Y + moveY
                    );
                }
                break;

            case Polyline2D polyline:
                foreach (var vertex in polyline.Vertices)
                {
                    vertex.Location = new XYZ(
                        vertex.Location.X + moveX,
                        vertex.Location.Y + moveY,
                        vertex.Location.Z
                    );
                }
                break;

            case Line line:
                line.StartPoint = new XYZ(
                    line.StartPoint.X + moveX,
                    line.StartPoint.Y + moveY,
                    line.StartPoint.Z
                );
                line.EndPoint = new XYZ(
                    line.EndPoint.X + moveX,
                    line.EndPoint.Y + moveY,
                    line.EndPoint.Z
                );
                break;

            // ACadSharp에서 Arc는 Circle 계열이므로
            // 반드시 Arc를 Circle보다 먼저 검사해야 한다.
            case Arc arc:
                arc.Center = new XYZ(
                    arc.Center.X + moveX,
                    arc.Center.Y + moveY,
                    arc.Center.Z
                );
                break;

            case Circle circle:
                circle.Center = new XYZ(
                    circle.Center.X + moveX,
                    circle.Center.Y + moveY,
                    circle.Center.Z
                );
                break;

            case TextEntity text:
                text.InsertPoint = new XYZ(
                    text.InsertPoint.X + moveX,
                    text.InsertPoint.Y + moveY,
                    text.InsertPoint.Z
                );
                break;

            case MText mtext:
                mtext.InsertPoint = new XYZ(
                    mtext.InsertPoint.X + moveX,
                    mtext.InsertPoint.Y + moveY,
                    mtext.InsertPoint.Z
                );
                break;

            case Insert insert:
                insert.InsertPoint = new XYZ(
                    insert.InsertPoint.X + moveX,
                    insert.InsertPoint.Y + moveY,
                    insert.InsertPoint.Z
                );
                break;
        }
    }

    internal readonly record struct DimensionBounds(
        double MinX,
        double MinY,
        double MaxX,
        double MaxY
    );

    /// <summary>
    /// 수정된 DWG를 원본과 다른 경로에 저장한다.
    /// 이후 버전 2 수정 작업이 추가되면 마지막 단계에서 호출한다.
    /// </summary>
    private static void SaveAsNewDwg(
        CadDocument document,
        string outputPath
    )
    {
        ValidateEntitiesBeforeSave(document);

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

    /// <summary>
    /// 저장 전에 중복 객체와 대표적인 잘못된 좌표를 검사한다.
    /// </summary>
    private static void ValidateEntitiesBeforeSave(
        CadDocument document
    )
    {
        HashSet<Entity> seen = new();

        foreach (Entity entity in document.Entities)
        {
            if (!seen.Add(entity))
            {
                throw new Exception(
                    "같은 Entity 객체가 중복 등록되어 있습니다. " +
                    $"Handle={entity.Handle}, Type={entity.ObjectName}"
                );
            }

            switch (entity)
            {
                case Line line:
                    ValidateFinitePoint(
                        line.StartPoint.X,
                        line.StartPoint.Y,
                        line.StartPoint.Z,
                        entity,
                        "LINE 시작점"
                    );
                    ValidateFinitePoint(
                        line.EndPoint.X,
                        line.EndPoint.Y,
                        line.EndPoint.Z,
                        entity,
                        "LINE 끝점"
                    );
                    break;

                case Arc arc:
                    ValidateFinitePoint(
                        arc.Center.X,
                        arc.Center.Y,
                        arc.Center.Z,
                        entity,
                        "ARC 중심"
                    );

                    if (!double.IsFinite(arc.Radius) ||
                        arc.Radius <= 0.0)
                    {
                        throw new Exception(
                            "잘못된 ARC 반지름입니다. " +
                            $"Handle={arc.Handle}, Radius={arc.Radius}"
                        );
                    }
                    break;

                case Circle circle:
                    ValidateFinitePoint(
                        circle.Center.X,
                        circle.Center.Y,
                        circle.Center.Z,
                        entity,
                        "CIRCLE 중심"
                    );

                    if (!double.IsFinite(circle.Radius) ||
                        circle.Radius <= 0.0)
                    {
                        throw new Exception(
                            "잘못된 CIRCLE 반지름입니다. " +
                            $"Handle={circle.Handle}, Radius={circle.Radius}"
                        );
                    }
                    break;

                case LwPolyline polyline:
                    if (polyline.Vertices.Count < 2)
                    {
                        throw new Exception(
                            "꼭짓점이 부족한 LWPOLYLINE입니다. " +
                            $"Handle={polyline.Handle}, " +
                            $"Vertices={polyline.Vertices.Count}"
                        );
                    }

                    foreach (var vertex in polyline.Vertices)
                    {
                        if (!double.IsFinite(vertex.Location.X) ||
                            !double.IsFinite(vertex.Location.Y) ||
                            !double.IsFinite(vertex.Bulge))
                        {
                            throw new Exception(
                                "잘못된 LWPOLYLINE 좌표입니다. " +
                                $"Handle={polyline.Handle}"
                            );
                        }
                    }
                    break;
            }
        }
    }

    private static void ValidateFinitePoint(
        double x,
        double y,
        double z,
        Entity entity,
        string pointName
    )
    {
        if (double.IsFinite(x) &&
            double.IsFinite(y) &&
            double.IsFinite(z))
        {
            return;
        }

        throw new Exception(
            $"{pointName} 좌표가 잘못되었습니다. " +
            $"Handle={entity.Handle}, " +
            $"Point=({x}, {y}, {z})"
        );
    }

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
    /// 원본 CAD 객체와 탐색에 사용할 좌표 정보를 함께 보관한다.
    /// 좌표 값은 DWG를 처음 읽은 시점의 값이다.
    /// 도형을 수정한 후 최신 값이 필요하면 CreateEntityData를 다시 호출하거나
    /// 실제 Entity의 GetBoundingBox를 사용한다.
    /// </summary>
    internal sealed class EntityData
    {
        public required Entity Entity { get; init; }
        public required string ObjectName { get; init; }
        public required string Handle { get; init; }
        public required string LayerName { get; init; }
        public short ColorIndex { get; init; }

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

        public string TextValue { get; set; } = string.Empty;
        public string BlockName { get; set; } = string.Empty;

        public double InsertX { get; set; }
        public double InsertY { get; set; }
        public double InsertZ { get; set; }

        public double FirstPointX { get; set; }
        public double FirstPointY { get; set; }
        public double FirstPointZ { get; set; }

        public double SecondPointX { get; set; }
        public double SecondPointY { get; set; }
        public double SecondPointZ { get; set; }

        public double DefinitionPointX { get; set; }
        public double DefinitionPointY { get; set; }
        public double DefinitionPointZ { get; set; }

        public double TextPositionX { get; set; }
        public double TextPositionY { get; set; }
        public double TextPositionZ { get; set; }

        public bool HasBoundingBox { get; set; }

        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }

        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }

        public double Width { get; set; }
        public double Height { get; set; }
        public double Depth { get; set; }

        public double CenterBoxX { get; set; }
        public double CenterBoxY { get; set; }
        public double CenterBoxZ { get; set; }
    }

    internal enum ThicknessPanelDirection
    {
        None,
        Horizontal,
        Vertical
    }

    internal sealed class PanelGroup
    {
        public int Number { get; set; }
        public required EntityData BasePolyline { get; init; }
        public double SearchMargin { get; init; }
        public double SearchMinX { get; init; }
        public double SearchMinY { get; init; }
        public double SearchMaxX { get; init; }
        public double SearchMaxY { get; init; }
        public required List<EntityData> Entities { get; init; }

        // 두께 패널은 선택 사항이다. 조건에 맞지 않으면 false/None 상태를 유지한다.
        public bool IsThicknessPanel { get; set; }
        public ThicknessPanelDirection ThicknessDirection { get; set; }
        public double AspectRatio { get; set; }
    }

    internal enum DimensionDirection
    {
        Unknown,
        Horizontal,
        Vertical
    }

    internal sealed class DimensionValueInfo
    {
        public required EntityData Dimension { get; init; }
        public double Value { get; init; }
        public DimensionDirection Direction { get; init; }
    }

    internal sealed class CurrentDimensionValues
    {
        public double Width { get; init; }
        public double Height { get; init; }
        public double? Thickness { get; init; }
        public required PanelGroup SourcePanel { get; init; }
        public required DimensionValueInfo WidthDimension { get; init; }
        public required DimensionValueInfo HeightDimension { get; init; }
        public PanelGroup? ThicknessPanel { get; init; }
        public DimensionValueInfo? ThicknessDimension { get; init; }
    }

    internal sealed class ResizeInput
    {
        public double TargetWidth { get; init; }
        public double TargetHeight { get; init; }
        public double? TargetThickness { get; init; }
    }

    internal sealed class PointData
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
        public double Bulge { get; init; }
    }
}