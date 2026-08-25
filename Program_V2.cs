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
internal static class Program_V2
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

            //도형 위치 출력 txt 파일
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
            // 기준 폴리선의 외곽을 사방 30만큼 확장한 범위에
            // 걸치는 모든 객체를 같은 패널로 묶는다.
            List<PanelGroup> panelGroups = FindPanelGroups(
                drawingData,
                30.0
            );

            // 두께 패널은 도면에 있을 수도 있고 없을 수도 있다.
            // 가로/세로 비율이 충분히 큰 패널만 선택적으로 두께 패널로 표시한다.
            // 해당 조건을 만족하는 패널이 하나도 없어도 예외 없이 계속 진행한다.
            ClassifyThicknessPanels(
                panelGroups
            );

            // 크기 수정 전에 바깥 사각형 안에 닫힌 3~8꼭짓점 도형이 있는
            // 패널과 그 안의
            // 16꼭짓점 패널 관계를 기억한다. 재정렬할 때 둘을 같이 이동시킨다.
            Dictionary<PanelGroup, List<PanelGroup>>
                originalNestedVertex16Bindings =
                    BindNestedVertex16Panels(
                        panelGroups
                            .Where(panel => !panel.IsThicknessPanel)
                            .ToList()
                    );

            //패널안의 객체 출력 txt 파일
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


            // 목표 높이(두께)가 36 이하이면 두께 패널 묶음을 통째로 삭제한다.
            // 기준 외곽뿐 아니라 빨간 박스, 치수 및 패널에 묶인 모든 객체를 제거하고
            // 이후 크기 수정과 50 간격 재배치 대상에서도 제외한다.
            double effectiveTargetThickness =
                resizeInput.TargetThickness ??
                currentValues.Thickness ??
                double.PositiveInfinity;

            bool thicknessPanelsRemoved =
                effectiveTargetThickness <= 36.0;


            if (thicknessPanelsRemoved)
            {
                RemoveThicknessPanelGroups(
                    drawingData,
                    panelGroups
                );
            }
            // 한 패널 안에서 같은 Y축에 볼트 구멍 원 4쌍이 있으면
            // 목표 가로 120 이하: 중심에 가까운 2쌍 유지
            // 목표 가로 120 초과: 중심에 가까운 2쌍 삭제(바깥쪽 2쌍 유지)
            SelectBoltHolePairsByTargetWidth(
                drawingData,
                panelGroups,
                resizeInput.TargetWidth
            );

            // 흰색 치구 원 안에 볼트 구멍 레이어 원이 있는 관계를
            // 도형 수정과 재배치 전에 기억한다.
            List<ChiguCircleBoltBinding> chiguCircleBoltBindings =
                FindChiguCircleBoltBindings(panelGroups);

            ApplyPanelResize(
                drawingData,
                panelGroups,
                currentValues,
                resizeInput
            );

            // 패널 간격 계산에 수정된 치구 원 크기도 반영되게
            // 재배치 전에 먼저 목표 지름과 볼트 중심을 적용한다.
            ResizeChiguCirclesAroundBoltHoles(
                chiguCircleBoltBindings,
                resizeInput.TargetWidth,
                resizeInput.TargetHeight
            );

            // 크기와 치수 수정이 끝난 뒤, 현재 패널 외곽 기준으로
            // 왼쪽부터 패널 사이 간격을 정확히 50으로 재배치한다.
            ArrangePanelGroupsWithGap(
                panelGroups,
                50.0,
                originalNestedVertex16Bindings
            );

            // 재배치가 모두 끝난 최종 볼트 구멍 중심을 기준으로
            // 기존 흰색 치구 원의 중심과 지름을 다시 설정한다.
            ResizeChiguCirclesAroundBoltHoles(
                chiguCircleBoltBindings,
                resizeInput.TargetWidth,
                resizeInput.TargetHeight
            );

            ShowBoltHoleClearanceWarnings(
                panelGroups,
                3.9
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
                (thicknessPanelsRemoved
                    ? "두께: 목표값 36 이하 — 두께 패널 삭제"
                    : resizeInput.TargetThickness.HasValue && currentValues.Thickness.HasValue
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

        List<EntityData> allWhiteChiguPolylines = drawingData.Entities
            .Where(IsWhiteChiguPolyline)
            .Where(x => x.HasBoundingBox)
            .ToList();

        // 흰색 치구 원 안에 볼트 구멍 원이 있으면
        // 사각형/폴리선과 동일하게 독립 패널 기준으로 사용한다.
        List<EntityData> allWhiteChiguCircleBases = drawingData.Entities
            .Where(IsWhiteChiguCircle)
            .Where(circle => drawingData.Entities.Any(entity =>
                entity.Entity is Circle &&
                string.Equals(
                    entity.LayerName,
                    "볼트 구멍",
                    StringComparison.OrdinalIgnoreCase
                ) &&
                IsEntityInsideCircle(entity, circle, 0.001)
            ))
            .ToList();

        // 다른 흰색 치구 폴리선 안에 완전히 들어가는 일반 사각형은
        // 독립 일반 패널 기준이 아니라 내부 사각형으로 취급한다.
        // 단, 닫힌 16꼭짓점 LWPOLYLINE은 사각형 안에 들어 있어도
        // 별도의 독립 패널 기준으로 승격한다.
        List<EntityData> allPanelBases = allWhiteChiguPolylines
            .Where(candidate =>
                IsVertex16PanelBase(candidate) ||
                !allWhiteChiguPolylines.Any(other =>
                    !ReferenceEquals(candidate, other) &&
                    IsCompletelyInside(candidate, other, 0.001)
                )
            )
            .Concat(allWhiteChiguCircleBases)
            .GroupBy(entity => entity.Handle)
            .Select(group => group.First())
            .ToList();

        // 각 두께 패널 후보 자신의 수정 전 크기로 최소 비율을 결정한다.
        // 후보 자체가 100x100 이하이면 1.5 이상,
        // 그 외에는 2.0 이상이며, 모든 경우 4.0 미만이어야 한다.
        List<EntityData> thicknessPanelBases = allWhiteChiguPolylines
            .Where(IsThicknessPanelBase)
            .OrderBy(x => x.MinX)
            .ThenByDescending(x => x.MaxY)
            .ToList();

        // 일반 패널 후보에서는 이미 두께 패널로 판정된 기준 객체를 제외한다.
        HashSet<string> thicknessBaseHandles = thicknessPanelBases
            .Select(x => x.Handle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<EntityData> normalPanelBases = allPanelBases
            .Where(x => !thicknessBaseHandles.Contains(x.Handle))
            // 원형 패널이 내부 볼트 구멍을 먼저 소유하게 하고,
            // 그 다음 중첩된 16꼭짓점 패널을 처리한다.
            .OrderByDescending(IsWhiteChiguCircle)
            .ThenByDescending(IsVertex16PanelBase)
            .ThenBy(x => x.MinX)
            .ThenByDescending(x => x.MaxY)
            .ToList();

        // 모든 기준 폴리선 Handle을 따로 기억한다.
        // 어느 패널이 먼저 처리되더라도 다른 패널의 기준 객체는 사용 완료로 막지 않는다.
        HashSet<string> panelBaseHandles = thicknessPanelBases
            .Concat(normalPanelBases)
            .Select(x => x.Handle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
            bool isVertex16Panel =
                IsVertex16PanelBase(panelBase);

            bool isCirclePanel =
                IsWhiteChiguCircle(panelBase);

            // 다른 사각형 안에 들어 있지 않은 16꼭짓점만 독립 객체로 본다.
            // 바깥 사각형과 내부 3~8꼭짓점 도형을 가진 패널 안의
            // 16꼭짓점은 외부 치수를 가져오지 않는다.
            bool isIndependentVertex16Panel =
                isVertex16Panel &&
                !allWhiteChiguPolylines.Any(other =>
                    !ReferenceEquals(panelBase, other) &&
                    IsClosedFourVertexRectangle(other) &&
                    IsCompletelyInside(panelBase, other, 0.001)
                );

            // 같은 기준 객체를 중복 등록하는 것만 막는다.
            // 다른 패널이 먼저 주변 객체를 잡았더라도 기준 폴리선 자체는 계속 검사한다.
            if (result.Any(panel =>
                    string.Equals(
                        panel.BasePolyline.Handle,
                        panelBase.Handle,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            double searchMinX = panelBase.MinX - searchMargin;
            double searchMinY = panelBase.MinY - searchMargin;
            double searchMaxX = panelBase.MaxX + searchMargin;
            double searchMaxY = panelBase.MaxY + searchMargin;

            List<EntityData> members = drawingData.Entities
                // 기준 폴리선은 assignedHandles 상태와 관계없이 항상 포함한다.
                // 다른 패널 기준 객체 역시 선점 대상에서 제외해 패널 개수 제한이 생기지 않게 한다.
                // 단, 독립 16꼭짓점 패널 기준은 먼저 처리된 뒤 바깥 사각형
                // 패널의 구성원으로 다시 들어가지 않게 한다.
                .Where(x =>
                    ReferenceEquals(x, panelBase) ||
                    (isCirclePanel &&
                     IsEntityInsideCircle(x, panelBase, 0.001)) ||
                    (isIndependentVertex16Panel && x.Entity is Dimension) ||
                    (panelBaseHandles.Contains(x.Handle) &&
                     !IsVertex16PanelBase(x)) ||
                    !assignedHandles.Contains(x.Handle))
                .Where(x => IsEntityInsideOrTouchingBounds(
                    x,
                    searchMinX,
                    searchMinY,
                    searchMaxX,
                    searchMaxY
                ))
                // 16꼭짓점 패널은 기본적으로 자기 외곽 안의 객체만 소유한다.
                // 단, 다른 사각형 안에 들어 있지 않은 독립 16꼭짓점은
                // 앞 단계에서 확인한 외곽 확장 범위 30에 걸리는 치수만 추가로 포함한다.
                // 같은 범위의 선, 원, 문자 등 다른 외부 객체는 포함하지 않는다.
                .Where(x =>
                    !isVertex16Panel ||
                    ReferenceEquals(x, panelBase) ||
                    IsCompletelyInside(x, panelBase, 0.001) ||
                    (isIndependentVertex16Panel && x.Entity is Dimension)
                )
                // 원형 패널의 일반 구성원은 원 내부에 있는 객체만 포함한다.
                // 치수는 다른 패널과 동일하게 외곽 확장 범위에서 별도 재배정한다.
                .Where(x =>
                    !isCirclePanel ||
                    ReferenceEquals(x, panelBase) ||
                    x.Entity is Dimension ||
                    IsEntityInsideCircle(x, panelBase, 0.001)
                )
                // 기준 폴리선 자신은 포함한다.
                // 다른 흰색 치구 폴리선은 기준 패널 안에 완전히 들어간
                // 내부의 닫힌 폴리선일 때만 포함한다.
                .Where(x =>
                    ReferenceEquals(x, panelBase) ||
                    !IsWhiteChiguPolyline(x) ||
                    (isCirclePanel
                        ? IsEntityInsideCircle(x, panelBase, 0.001)
                        : IsCompletelyInside(x, panelBase, 0.001))
                )
                .GroupBy(x => x.Handle)
                .Select(group => group.First())
                .ToList();

            // 16꼭짓점 외곽은 내부 객체가 없어도 그 자체로 독립 패널이다.
            // 그 외 패널은 기존 조건대로 구성원이 2개 이상이어야 한다.
            int minimumMemberCount =
                isVertex16Panel ? 1 : 2;

            if (members.Count < minimumMemberCount)
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

            // 독립 16꼭짓점의 확장 범위 30에 걸린 치수는 이 패널이 소유한다.
            // 앞에서 처리된 다른 패널에 같은 치수가 들어갔다면 중복되지 않게 제거한다.
            if (isIndependentVertex16Panel)
            {
                HashSet<string> dimensionHandles = members
                    .Where(entity => entity.Entity is Dimension)
                    .Select(entity => entity.Handle)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (PanelGroup existingPanel in result)
                {
                    existingPanel.Entities.RemoveAll(entity =>
                        dimensionHandles.Contains(entity.Handle)
                    );
                }
            }

            // 원형 패널 안의 볼트 구멍과 일반 구성원은 원형 패널이 소유한다.
            // 앞서 처리된 두께 패널에 같은 객체가 들어갔다면 중복을 제거한다.
            if (isCirclePanel)
            {
                HashSet<string> circleMemberHandles = members
                    .Where(entity => entity.Entity is not Dimension)
                    .Where(entity =>
                        ReferenceEquals(entity, panelBase) ||
                        IsEntityInsideCircle(
                            entity,
                            panelBase,
                            0.001
                        ))
                    .Select(entity => entity.Handle)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (PanelGroup existingPanel in result)
                {
                    existingPanel.Entities.RemoveAll(entity =>
                        !panelBaseHandles.Contains(entity.Handle) &&
                        circleMemberHandles.Contains(entity.Handle)
                    );
                }
            }

            result.Add(panel);

            // 일반 구성 객체만 이후 패널 자동 탐색에서 제외한다.
            // 다른 패널의 기준 폴리선은 절대 assignedHandles에 넣지 않아
            // 두께 패널이 몇 개든 모두 독립적으로 인식되게 한다.
            foreach (EntityData member in members)
            {
                // 치수는 패널 생성 순서대로 선점하지 않는다.
                // 모든 패널을 찾은 뒤 거리와 방향을 기준으로 다시 배정한다.
                if (member.Entity is Dimension)
                {
                    continue;
                }

                if (!panelBaseHandles.Contains(member.Handle) ||
                    ReferenceEquals(member, panelBase))
                {
                    assignedHandles.Add(member.Handle);
                }
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

        // 모든 패널 탐색이 끝난 뒤 치수 소속을 다시 결정한다.
        // 패널마다 가로 1개와 세로 1개만 허용하므로
        // 각 패널은 치수를 0~2개까지 포함할 수 있다.
        AssignDimensionsToPanels(
            drawingData,
            result,
            allWhiteChiguPolylines
        );

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

    private static void AssignDimensionsToPanels(
        DrawingData drawingData,
        IReadOnlyList<PanelGroup> panels,
        IReadOnlyList<EntityData> allWhiteChiguPolylines
    )
    {
        // 먼저 기존의 처리 순서 기반 치수 소속을 모두 제거한다.
        foreach (PanelGroup panel in panels)
        {
            panel.Entities.RemoveAll(entity =>
                entity.Entity is Dimension
            );
        }

        var candidates = drawingData.Entities
            .Where(entity => entity.Entity is Dimension)
            .SelectMany(dimension => panels
                .Where(panel => IsDimensionCandidateForPanel(
                    dimension,
                    panel,
                    allWhiteChiguPolylines
                ))
                .Select(panel => new
                {
                    Dimension = dimension,
                    Panel = panel,
                    Direction = GetDimensionDirection(dimension),
                    Score = GetDimensionPanelScore(
                        dimension,
                        panel.BasePolyline
                    )
                }))
            .Where(candidate =>
                candidate.Direction != DimensionDirection.Unknown
            )
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Dimension.Handle)
            .ToList();

        HashSet<string> assignedDimensionHandles = new(
            StringComparer.OrdinalIgnoreCase
        );

        HashSet<string> occupiedPanelDirections = new(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var candidate in candidates)
        {
            string directionKey =
                $"{candidate.Panel.BasePolyline.Handle}:" +
                $"{candidate.Direction}";

            // 같은 치수는 하나의 패널에만 포함한다.
            if (assignedDimensionHandles.Contains(
                    candidate.Dimension.Handle) ||
                occupiedPanelDirections.Contains(directionKey))
            {
                continue;
            }

            candidate.Panel.Entities.Add(candidate.Dimension);
            assignedDimensionHandles.Add(candidate.Dimension.Handle);
            occupiedPanelDirections.Add(directionKey);
        }
    }

    private static bool IsDimensionCandidateForPanel(
        EntityData dimension,
        PanelGroup panel,
        IReadOnlyList<EntityData> allWhiteChiguPolylines
    )
    {
        if (!IsEntityInsideOrTouchingBounds(
                dimension,
                panel.SearchMinX,
                panel.SearchMinY,
                panel.SearchMaxX,
                panel.SearchMaxY
            ))
        {
            return false;
        }

        if (!IsVertex16PanelBase(panel.BasePolyline))
        {
            return true;
        }

        bool isIndependentVertex16Panel =
            !allWhiteChiguPolylines.Any(other =>
                !ReferenceEquals(panel.BasePolyline, other) &&
                IsClosedFourVertexRectangle(other) &&
                IsCompletelyInside(
                    panel.BasePolyline,
                    other,
                    0.001
                )
            );

        // 독립 16꼭짓점은 외곽 확장 범위 안의 치수를 사용할 수 있다.
        // 다른 패널 안에 든 16꼭짓점은 자기 외곽 안의 치수만 사용한다.
        return isIndependentVertex16Panel ||
               IsCompletelyInside(
                   dimension,
                   panel.BasePolyline,
                   0.001
               );
    }

    private static double GetDimensionPanelScore(
        EntityData dimension,
        EntityData panelBase
    )
    {
        double firstDistance = DistanceSquaredToPanelBounds(
            dimension.FirstPointX,
            dimension.FirstPointY,
            panelBase
        );

        double secondDistance = DistanceSquaredToPanelBounds(
            dimension.SecondPointX,
            dimension.SecondPointY,
            panelBase
        );

        double definitionDistance = DistanceSquaredToPanelBounds(
            dimension.DefinitionPointX,
            dimension.DefinitionPointY,
            panelBase
        );

        return firstDistance +
               secondDistance +
               definitionDistance * 0.15;
    }

    private static double DistanceSquaredToPanelBounds(
        double x,
        double y,
        EntityData panelBase
    )
    {
        double dx = 0.0;
        double dy = 0.0;

        if (x < panelBase.MinX)
        {
            dx = panelBase.MinX - x;
        }
        else if (x > panelBase.MaxX)
        {
            dx = x - panelBase.MaxX;
        }

        if (y < panelBase.MinY)
        {
            dy = panelBase.MinY - y;
        }
        else if (y > panelBase.MaxY)
        {
            dy = y - panelBase.MaxY;
        }

        // 점이 패널 바깥이면 패널 외곽까지의 최단거리를 사용한다.
        if (dx > 0.0 || dy > 0.0)
        {
            return dx * dx + dy * dy;
        }

        // 점이 패널 안에 있더라도 거리를 0으로 처리하지 않고
        // 네 변 중 가장 가까운 외곽선까지의 거리로 비교한다.
        // 내부 사각형과 외부 사각형이 겹쳐 인식될 때 실제 측정점이
        // 놓인 패널을 선택하기 위한 처리다.
        double edgeDistance = Math.Min(
            Math.Min(
                x - panelBase.MinX,
                panelBase.MaxX - x
            ),
            Math.Min(
                y - panelBase.MinY,
                panelBase.MaxY - y
            )
        );

        edgeDistance = Math.Max(0.0, edgeDistance);

        return edgeDistance * edgeDistance;

    }

    private static double GetPanelAspectRatio(
        EntityData panelBase
    )
    {
        DimensionBounds bounds =
            GetCurrentBounds(panelBase.Entity);

        double width =
            bounds.MaxX - bounds.MinX;

        double height =
            bounds.MaxY - bounds.MinY;

        double longSide =
            Math.Max(width, height);

        double shortSide =
            Math.Min(width, height);

        if (shortSide <= 0.000001)
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
        IEnumerable<PanelGroup> panels
    )
    {
        foreach (PanelGroup panel in panels)
        {
            // 수정 전 수집된 기준 폴리선 크기를 사용한다.
            double width = panel.BasePolyline.Width;
            double height = panel.BasePolyline.Height;
            double longSide = Math.Max(width, height);
            double shortSide = Math.Min(width, height);

            panel.AspectRatio =
                shortSide > 0.000001
                    ? longSide / shortSide
                    : 0.0;

            double minimumAspectRatio =
                width <= 100.0 && height <= 100.0
                    ? 1.5
                    : 2.0;


            // FindPanelGroups에서 먼저 두께 패널로 확정한 결과를 유지한다.
            if (!panel.IsThicknessPanel)
            {
                panel.ThicknessDirection = ThicknessPanelDirection.None;
                continue;
            }

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
            string.Equals(
                entity.LayerName,
                "치구",
                StringComparison.OrdinalIgnoreCase
            ) &&
            isPolyline &&
            (entity.ColorIndex == 0 ||
             entity.ColorIndex == 7);
    }

    private static bool IsWhiteChiguCircle(
        EntityData entity
    )
    {
        return
            entity.Entity is Circle &&
            string.Equals(
                entity.LayerName,
                "치구",
                StringComparison.OrdinalIgnoreCase
            ) &&
            (entity.ColorIndex == 0 ||
             entity.ColorIndex == 7);
    }

    private static bool IsEntityInsideCircle(
        EntityData inner,
        EntityData outerCircleData,
        double tolerance = 0.001
    )
    {
        if (outerCircleData.Entity is not Circle outerCircle)
        {
            return false;
        }

        double centerX;
        double centerY;
        double innerRadius = 0.0;

        if (inner.Entity is Circle innerCircle)
        {
            centerX = innerCircle.Center.X;
            centerY = innerCircle.Center.Y;
            innerRadius = innerCircle.Radius;
        }
        else if (inner.HasBoundingBox)
        {
            centerX = (inner.MinX + inner.MaxX) / 2.0;
            centerY = (inner.MinY + inner.MaxY) / 2.0;

            double halfWidth = (inner.MaxX - inner.MinX) / 2.0;
            double halfHeight = (inner.MaxY - inner.MinY) / 2.0;

            // 사각 바운딩박스의 가장 먼 꼭짓점까지 포함되는지 검사한다.
            innerRadius = Math.Sqrt(
                halfWidth * halfWidth +
                halfHeight * halfHeight
            );
        }
        else
        {
            (double X, double Y)? point =
                GetRepresentativePoint(inner);

            if (!point.HasValue)
            {
                return false;
            }

            centerX = point.Value.X;
            centerY = point.Value.Y;
        }

        double dx = centerX - outerCircle.Center.X;
        double dy = centerY - outerCircle.Center.Y;
        double centerDistance = Math.Sqrt(dx * dx + dy * dy);

        return centerDistance + innerRadius <=
               outerCircle.Radius + tolerance;
    }

    /// <summary>
    /// 사각형 안에 중첩되어 있어도 독립 패널로 분리할 16꼭짓점 외곽인지 확인한다.
    /// 치구 레이어의 ACI 0/7 닫힌 LWPOLYLINE과 POLYLINE2D를 모두 허용한다.
    /// </summary>
    private static bool IsVertex16PanelBase(
        EntityData entity
    )
    {
        return
            IsWhiteChiguPolyline(entity) &&
            entity.IsClosed &&
            entity.Vertices.Count == 16;
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
        writer.WriteLine(
            "기준: 흰색 치구 POLYLINE 또는 내부에 볼트 구멍 원이 있는 흰색 치구 CIRCLE"
        );
        writer.WriteLine("포함 범위: 기준 외곽 바운딩박스 사방 30");
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



    private sealed class BoltHoleCircleGroup
    {
        public required List<EntityData> Circles { get; init; }

        public double CenterX => Circles[0].CenterX;
        public double CenterY => Circles[0].CenterY;
        public int CircleCount => Circles.Count;
    }

    /// <summary>
    /// 한 패널의 같은 Y축 줄에 중심이 같은 볼트 구멍 그룹이 정확히 4개 있을 때 처리한다.
    /// 중심당 원이 2개인 2·2·2·2 형식은 목표 가로 120을 기준으로,
    /// 중심당 원이 3개인 3·3·3·3 형식은 목표 가로 140을 기준으로 처리한다.
    /// 기준 이하이면 중심에 가까운 2그룹을 남기고 바깥 2그룹을 삭제하며,
    /// 기준 초과이면 중심에 가까운 2그룹을 삭제한다.
    /// </summary>
    private static void SelectBoltHolePairsByTargetWidth(
        DrawingData drawingData,
        IReadOnlyList<PanelGroup> panelGroups,
        double targetWidth,
        double centerTolerance = 0.001,
        double yTolerance = 0.001
    )
    {
        int deletedGroupCount = 0;
        int deletedCircleCount = 0;

        foreach (PanelGroup panel in panelGroups)
        {
            List<EntityData> boltCircles = panel.Entities
                .Where(entity =>
                    string.Equals(
                        entity.LayerName,
                        "볼트 구멍",
                        StringComparison.OrdinalIgnoreCase
                    ))
                .Where(entity => entity.Entity is Circle)
                .GroupBy(entity => entity.Handle)
                .Select(group => group.First())
                .ToList();

            if (boltCircles.Count < 8)
            {
                continue;
            }

            List<BoltHoleCircleGroup> centerGroups =
                BuildBoltHoleCenterGroups(
                    boltCircles,
                    centerTolerance
                )
                .Where(group =>
                    group.CircleCount == 2 ||
                    group.CircleCount == 3)
                .ToList();

            if (centerGroups.Count < 4)
            {
                continue;
            }

            List<List<BoltHoleCircleGroup>> yRows = new();

            foreach (BoltHoleCircleGroup group in centerGroups
                .OrderBy(item => item.CenterY)
                .ThenBy(item => item.CenterX))
            {
                List<BoltHoleCircleGroup>? matchingRow =
                    yRows.FirstOrDefault(row =>
                        Math.Abs(row[0].CenterY - group.CenterY)
                            <= yTolerance
                    );

                if (matchingRow == null)
                {
                    matchingRow = new List<BoltHoleCircleGroup>();
                    yRows.Add(matchingRow);
                }

                matchingRow.Add(group);
            }

            DimensionBounds panelBounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            double panelCenterX =
                (panelBounds.MinX + panelBounds.MaxX) / 2.0;

            foreach (List<BoltHoleCircleGroup> row in yRows
                .Where(row => row.Count == 4))
            {
                bool isTwoCirclePattern =
                    row.All(group => group.CircleCount == 2);

                bool isThreeCirclePattern =
                    row.All(group => group.CircleCount == 3);

                if (!isTwoCirclePattern &&
                    !isThreeCirclePattern)
                {
                    continue;
                }

                double widthThreshold =
                    isThreeCirclePattern
                        ? 140.0
                        : 120.0;

                List<BoltHoleCircleGroup> orderedByCenterDistance = row
                    .OrderBy(group =>
                        Math.Abs(group.CenterX - panelCenterX))
                    .ToList();

                List<BoltHoleCircleGroup> groupsToDelete =
                    targetWidth <= widthThreshold
                        ? orderedByCenterDistance
                            .Skip(2)
                            .Take(2)
                            .ToList()
                        : orderedByCenterDistance
                            .Take(2)
                            .ToList();


                foreach (BoltHoleCircleGroup group in groupsToDelete)
                {
                    deletedGroupCount++;

                    foreach (EntityData circleData in group.Circles)
                    {
                        deletedCircleCount++;
                        RemoveEntityFromDrawing(
                            drawingData,
                            circleData
                        );

                        panel.Entities.RemoveAll(entity =>
                            string.Equals(
                                entity.Handle,
                                circleData.Handle,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );
                    }
                }
            }
        }

    }

    private static List<BoltHoleCircleGroup> BuildBoltHoleCenterGroups(
        IReadOnlyList<EntityData> circles,
        double centerTolerance
    )
    {
        List<BoltHoleCircleGroup> result = new();
        HashSet<string> usedHandles = new(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (EntityData seed in circles)
        {
            if (usedHandles.Contains(seed.Handle))
            {
                continue;
            }

            List<EntityData> sameCenter = circles
                .Where(candidate =>
                    !usedHandles.Contains(candidate.Handle))
                .Where(candidate =>
                    Math.Abs(candidate.CenterX - seed.CenterX)
                        <= centerTolerance &&
                    Math.Abs(candidate.CenterY - seed.CenterY)
                        <= centerTolerance)
                .OrderByDescending(candidate => candidate.Radius)
                .ToList();

            List<EntityData> distinctRadiusCircles = new();

            foreach (EntityData candidate in sameCenter)
            {
                bool duplicateRadius =
                    distinctRadiusCircles.Any(existing =>
                        Math.Abs(existing.Radius - candidate.Radius)
                            <= 0.000001
                    );

                if (!duplicateRadius)
                {
                    distinctRadiusCircles.Add(candidate);
                }
            }

            if (distinctRadiusCircles.Count < 2)
            {
                continue;
            }

            foreach (EntityData item in sameCenter)
            {
                usedHandles.Add(item.Handle);
            }

            result.Add(new BoltHoleCircleGroup
            {
                Circles = distinctRadiusCircles
            });
        }

        return result;
    }

    private static void RemoveEntityFromDrawing(
        DrawingData drawingData,
        EntityData entityData
    )
    {
        Entity entity = entityData.Entity;

        if (drawingData.Document.Entities.Contains(entity))
        {
            drawingData.Document.Entities.Remove(entity);
        }
        else
        {
            foreach (var blockRecord in drawingData.Document.BlockRecords)
            {
                if (!blockRecord.Entities.Contains(entity))
                {
                    continue;
                }

                blockRecord.Entities.Remove(entity);
                break;
            }
        }

        drawingData.Entities.RemoveAll(item =>
            string.Equals(
                item.Handle,
                entityData.Handle,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }


    /// <summary>
    /// 목표 높이가 36 이하일 때 모든 두께 패널 묶음을 DWG에서 삭제한다.
    /// 패널 기준 폴리선뿐 아니라 빨간 박스, 치수 및 묶인 모든 객체를 제거하며,
    /// 삭제된 패널은 이후 크기 변경과 재배치 목록에서도 제외한다.
    /// </summary>
    private static void RemoveThicknessPanelGroups(
        DrawingData drawingData,
        List<PanelGroup> panelGroups
    )
    {
        // FindPanelGroups에서 최초 확정된 두께 패널만 삭제한다.
        // 여기서 비율을 다시 계산하면 일반 패널이 두께 패널로 뒤집힐 수 있다.
        List<PanelGroup> thicknessPanels = panelGroups
            .Where(panel => panel.IsThicknessPanel)
            .ToList();

        if (thicknessPanels.Count == 0)
        {
            return;
        }

        HashSet<Entity> entitiesToRemove = thicknessPanels
            .SelectMany(panel => panel.Entities
                .Append(panel.BasePolyline))
            .Select(data => data.Entity)
            .ToHashSet();


        foreach (Entity entity in entitiesToRemove)
        {
            if (drawingData.Document.Entities.Contains(entity))
            {
                drawingData.Document.Entities.Remove(entity);
                continue;
            }

            foreach (var blockRecord in drawingData.Document.BlockRecords)
            {
                if (blockRecord.Entities.Contains(entity))
                {
                    blockRecord.Entities.Remove(entity);
                    break;
                }
            }
        }

        drawingData.Entities.RemoveAll(data =>
            entitiesToRemove.Contains(data.Entity));

        HashSet<PanelGroup> removedPanels =
            thicknessPanels.ToHashSet();

        panelGroups.RemoveAll(panel =>
            removedPanels.Contains(panel));

        // 삭제 후 남은 일반 패널 번호를 왼쪽부터 다시 지정한다.
        List<PanelGroup> ordered = panelGroups
            .OrderBy(panel =>
                GetCurrentBounds(panel.BasePolyline.Entity).MinX)
            .ThenBy(panel =>
                GetCurrentBounds(panel.BasePolyline.Entity).MinY)
            .ToList();

        panelGroups.Clear();
        panelGroups.AddRange(ordered);

        for (int index = 0; index < panelGroups.Count; index++)
        {
            panelGroups[index].Number = index + 1;
        }
    }

    /// <summary>
    /// 모든 패널을 중심 기준으로 수정한다.
    /// 일반 패널은 X/Y 변화량을 사용하고, 두께 패널은 긴 방향과 짧은 방향을 구분한다.
    /// "볼트 구멍" 레이어 객체는 크기와 위치를 모두 유지한다.
    /// </summary>
    private static void ApplyPanelResize(
        DrawingData drawingData,
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

        // 모든 패널의 일반 크기 변경과 모서리 구멍 변경을 먼저 끝낸다.
        foreach (PanelGroup panel in panelGroups.OrderBy(x => x.Number))
        {
            // 원형 패널 기준 원은 아래의 전용 계산식으로 수정한다.
            // 폴리선 전용 ResizeSinglePanel에 넘기면 예외가 발생한다.
            if (IsWhiteChiguCircle(panel.BasePolyline))
            {
                continue;
            }

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

        // 원이 없고 바깥 사각형 안에 닫힌 3~8꼭짓점 도형이 있으면,
        // 두 도형의 가로/세로 크기 차이를 재생성 여유값으로 사용한다.
        // 재생성 크기 = 목표값 - 두 도형 크기 차이 - 4.5 - 4.5 - 0.5
        // 해당 패널이 없으면 재생성 크기 = 목표값 - 21
        (double vertex16TargetWidth, double vertex16TargetHeight) =
            GetVertex16TargetSize(
                panelGroups,
                input.TargetWidth,
                input.TargetHeight
            );


        RebuildVertex16PanelsFromPreviousPanels(
            drawingData,
            panelGroups,
            vertex16TargetWidth,
            vertex16TargetHeight
        );
    }

    /// <summary>
    /// 흰색 치구 원 안에 볼트 구멍 레이어 원이 있는 경우를 찾는다.
    /// 동심 볼트 원이 여러 개면 흰색 원 중심에 가장 가까운 원 하나만 연결한다.
    /// </summary>
    private static List<ChiguCircleBoltBinding>
        FindChiguCircleBoltBindings(
            IReadOnlyList<PanelGroup> panelGroups
        )
    {
        List<PanelGroup> circlePanels = panelGroups
            .Where(panel =>
                IsWhiteChiguCircle(panel.BasePolyline))
            .ToList();

        List<ChiguCircleBoltBinding> result = new();

        foreach (PanelGroup circlePanel in circlePanels)
        {
            EntityData chiguCircleData =
                circlePanel.BasePolyline;

            Circle chiguCircle = (Circle)chiguCircleData.Entity;

            EntityData? boltHoleCircle = circlePanel.Entities
                .Where(entity => entity.Entity is Circle)
                .Where(entity => string.Equals(
                    entity.LayerName,
                    "볼트 구멍",
                    StringComparison.OrdinalIgnoreCase
                ))
                .Where(entity =>
                {
                    Circle boltCircle = (Circle)entity.Entity;

                    double dx =
                        boltCircle.Center.X - chiguCircle.Center.X;

                    double dy =
                        boltCircle.Center.Y - chiguCircle.Center.Y;

                    return dx * dx + dy * dy <=
                           chiguCircle.Radius * chiguCircle.Radius +
                           0.001;
                })
                .OrderBy(entity =>
                {
                    Circle boltCircle = (Circle)entity.Entity;

                    double dx =
                        boltCircle.Center.X - chiguCircle.Center.X;

                    double dy =
                        boltCircle.Center.Y - chiguCircle.Center.Y;

                    return dx * dx + dy * dy;
                })
                .FirstOrDefault();

            if (boltHoleCircle == null)
            {
                continue;
            }

            result.Add(new ChiguCircleBoltBinding
            {
                Panel = circlePanel,
                ChiguCircle = chiguCircleData,
                BoltHoleCircle = boltHoleCircle
            });
        }

        return result;
    }

    /// <summary>
    /// 볼트 구멍 중심으로 기존 흰색 치구 원을 다시 만든다.
    /// 원이 목표 가로·세로 중 작은 쪽을 넘지 않도록 작은 목표값을 사용한다.
    /// 지름 = 목표값 - 18 - 18 - 0.5 - 10 - 10
    /// </summary>
    private static void ResizeChiguCirclesAroundBoltHoles(
        IReadOnlyList<ChiguCircleBoltBinding> bindings,
        double targetWidth,
        double targetHeight
    )
    {
        double targetValue = Math.Min(
            targetWidth,
            targetHeight
        );

        double targetDiameter =
            targetValue -
            18.0 -
            18.0 -
            0.5 -
            10.0 -
            10.0;

        if (bindings.Count > 0 && targetDiameter <= 0.0)
        {
            throw new Exception(
                "볼트 구멍 중심 치구 원의 목표 지름이 0 이하입니다. " +
                $"Target={targetValue:0.###}, " +
                $"Diameter={targetDiameter:0.###}"
            );
        }

        foreach (ChiguCircleBoltBinding binding in bindings)
        {
            if (binding.ChiguCircle.Entity is not Circle chiguCircle ||
                binding.BoltHoleCircle.Entity is not Circle boltCircle)
            {
                continue;
            }

            DimensionBounds oldBounds =
                GetCurrentBounds(chiguCircle);

            chiguCircle.Center = new XYZ(
                boltCircle.Center.X,
                boltCircle.Center.Y,
                chiguCircle.Center.Z
            );

            chiguCircle.Radius = targetDiameter / 2.0;

            DimensionBounds newBounds =
                GetCurrentBounds(chiguCircle);

            // 원형 패널에 포함된 가로/세로 치수도
            // 원의 새 외곽에 맞춰 함께 수정한다.
            foreach (DimensionAligned dimension in binding.Panel.Entities
                .Select(entity => entity.Entity)
                .OfType<DimensionAligned>()
                .Distinct())
            {
                ResizeDimensionByBounds(
                    dimension,
                    oldBounds,
                    newBounds
                );
            }

            // 이후 저장 전 검사와 후속 처리에서 수정된 값을 사용하도록
            // EntityData의 캐시 정보도 함께 갱신한다.
            binding.ChiguCircle.CenterX = chiguCircle.Center.X;
            binding.ChiguCircle.CenterY = chiguCircle.Center.Y;
            binding.ChiguCircle.CenterZ = chiguCircle.Center.Z;
            binding.ChiguCircle.Radius = chiguCircle.Radius;
            binding.ChiguCircle.Diameter = targetDiameter;

            ReadBoundingBox(
                binding.ChiguCircle,
                chiguCircle
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

        HashSet<string> resizableHoleHandles =
            FindResizableCornerHoleHandles(panel);

        int duplicateSkippedCount = 0;
        int fixedBoltHoleCount = 0;
        int dimensionCount = 0;
        int cornerHoleCount = 0;
        int redBlockCount = 0;
        int innerRectangleCount = 0;
        int movedEntityCount = 0;


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
                duplicateSkippedCount++;
                continue;
            }

            if (string.Equals(
                    entity.LayerName,
                    "볼트 구멍",
                    StringComparison.OrdinalIgnoreCase))
            {
                fixedBoltHoleCount++;
                continue;
            }

            if (entity.Entity is DimensionAligned dimension)
            {
                dimensionCount++;
                ResizeDimensionByBounds(
                    dimension,
                    oldBounds,
                    newBounds
                );

                continue;
            }

            if (resizableHoleHandles.Contains(entity.Handle) &&
                entity.Entity is Circle holeCircle)
            {
                cornerHoleCount++;
                ResizeAndMoveCornerHole(
                    entity,
                    holeCircle,
                    basePolyline,
                    widthDelta,
                    heightDelta
                );

                continue;
            }

            // 두께 패널 안의 빨간 사각형은 판 방향에 맞춰 처리한다.
            // 가로형: 빨간 박스의 세로 크기를 두께 변화량만큼 변경하고 X축으로 이동한다.
            // 세로형: 빨간 박스의 가로 크기를 두께 변화량만큼 변경하고 Y축으로 이동한다.
            if (IsThicknessPanelRedBlock(panel, entity))
            {
                redBlockCount++;

                if (panel.ThicknessDirection == ThicknessPanelDirection.Horizontal)
                {
                    ResizePolylineFromCenter(
                        entity,
                        0.0,
                        heightDelta
                    );

                    double redBlockMoveX = GetDirectionalMove(
                        entity.CenterBoxX,
                        basePolyline.CenterBoxX,
                        widthDelta
                    );

                    MoveEntity(
                        entity.Entity,
                        redBlockMoveX,
                        0.0
                    );
                }
                else
                {
                    ResizePolylineFromCenter(
                        entity,
                        widthDelta,
                        0.0
                    );

                    double redBlockMoveY = GetDirectionalMove(
                        entity.CenterBoxY,
                        basePolyline.CenterBoxY,
                        heightDelta
                    );

                    MoveEntity(
                        entity.Entity,
                        0.0,
                        redBlockMoveY
                    );
                }

                continue;
            }

            if (IsInnerWhitePolylineUpTo8Vertices(panel, entity))
            {
                innerRectangleCount++;
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
            movedEntityCount++;
        }

    }

    /// <summary>
    /// 16꼭짓점 패널의 재생성 목표 크기를 계산한다.
    ///
    /// 조건에 맞는 기준 패널:
    /// - 원(CIRCLE)이 하나도 없음
    /// - 치수와 텍스트 개수는 무관
    /// - 바깥 기준은 닫힌 4꼭짓점 사각형
    /// - 그 안에 완전히 포함된 닫힌 3~8꼭짓점 폴리선이 하나 이상 있음
    ///
    /// 기준 패널이 있으면:
    /// X = 목표 X - 바깥/안쪽 도형 가로 크기 차이 - 4.5 - 4.5 - 0.5
    /// Y = 목표 Y - 바깥/안쪽 도형 세로 크기 차이 - 4.5 - 4.5 - 0.5
    ///
    /// 기준 패널이 없으면:
    /// X = 목표 X - 21
    /// Y = 목표 Y - 21
    /// </summary>
    private static (double Width, double Height) GetVertex16TargetSize(
        IReadOnlyList<PanelGroup> panelGroups,
        double targetWidth,
        double targetHeight
    )
    {
        foreach (PanelGroup panel in panelGroups
            .Where(panel => !panel.IsThicknessPanel))
        {
            if (!IsClosedFourVertexRectangle(panel.BasePolyline))
            {
                continue;
            }

            // 16꼭짓점 크기 기준 패널의 일반 원 존재 여부만 확인한다.
            // 볼트 구멍 원은 패널 구성원으로 유지하되 이 검사에서는 제외한다.
            bool hasCircle = panel.Entities
                .Any(entity =>
                    entity.Entity is Circle &&
                    !string.Equals(
                        entity.LayerName,
                        "볼트 구멍",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (hasCircle)
            {
                continue;
            }

            EntityData? innerPolyline = panel.Entities
                .Where(entity =>
                    !ReferenceEquals(entity, panel.BasePolyline))
                .Where(IsClosedPolylineWithAtMost8Vertices)
                .Where(entity => IsCompletelyInside(
                    entity,
                    panel.BasePolyline,
                    0.001
                ))
                .GroupBy(entity => entity.Handle)
                .Select(group => group.First())
                // 안쪽 후보가 여러 개면 바깥 사각형과 대응되는
                // 가장 큰 안쪽 도형을 기준으로 사용한다.
                .OrderByDescending(entity =>
                    entity.Width * entity.Height
                )
                .FirstOrDefault();

            if (innerPolyline == null)
            {
                continue;
            }

            DimensionBounds firstBounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            DimensionBounds secondBounds =
                GetCurrentBounds(innerPolyline.Entity);

            double firstWidth =
                firstBounds.MaxX - firstBounds.MinX;

            double firstHeight =
                firstBounds.MaxY - firstBounds.MinY;

            double secondWidth =
                secondBounds.MaxX - secondBounds.MinX;

            double secondHeight =
                secondBounds.MaxY - secondBounds.MinY;

            double widthDifference =
                Math.Abs(firstWidth - secondWidth);

            double heightDifference =
                Math.Abs(firstHeight - secondHeight);

            double calculatedWidth =
                targetWidth - widthDifference - 4.5-4.5-0.5;

            double calculatedHeight =
                targetHeight - heightDifference - 4.5-4.5-0.5;


            if (calculatedWidth <= 0.0 ||
                calculatedHeight <= 0.0)
            {
                throw new Exception(
                    "16꼭짓점 패널 기준 크기 계산 결과가 0 이하입니다. " +
                    $"Panel={panel.BasePolyline.Handle}, " +
                    $"Target={targetWidth}x{targetHeight}, " +
                    $"ShapeDifference={widthDifference}x{heightDifference}, " +
                    $"Calculated={calculatedWidth}x{calculatedHeight}"
                );
            }

            return (
                calculatedWidth,
                calculatedHeight
            );
        }

        double fallbackWidth = targetWidth - 18-18-4.5-4.5-0.5;
        double fallbackHeight = targetHeight - 18-18-4.5-4.5-0.5;


        if (fallbackWidth <= 0.0 ||
            fallbackHeight <= 0.0)
        {
            throw new Exception(
                "16꼭짓점 패널 기본 기준 크기 계산 결과가 0 이하입니다. " +
                $"Target={targetWidth}x{targetHeight}, " +
                $"Calculated={fallbackWidth}x{fallbackHeight}"
            );
        }

        return (
            fallbackWidth,
            fallbackHeight
        );
    }

    private static bool IsClosedFourVertexRectangle(
        EntityData entity
    )
    {
        return entity.Entity switch
        {
            LwPolyline polyline =>
                polyline.IsClosed &&
                polyline.Vertices.Count == 4,

            Polyline2D polyline =>
                polyline.IsClosed &&
                polyline.Vertices.Count == 4,

            _ => false
        };
    }

    private static bool IsClosedPolylineWithAtMost8Vertices(
        EntityData entity
    )
    {
        return entity.Entity switch
        {
            LwPolyline polyline =>
                polyline.IsClosed &&
                polyline.Vertices.Count >= 3 &&
                polyline.Vertices.Count <= 8,

            Polyline2D polyline =>
                polyline.IsClosed &&
                polyline.Vertices.Count >= 3 &&
                polyline.Vertices.Count <= 8,

            _ => false
        };
    }

    private static void RebuildVertex16PanelsFromPreviousPanels(
        DrawingData drawingData,
        IReadOnlyList<PanelGroup> panelGroups,
        double targetWidth,
        double targetHeight
    )
    {
        List<PanelGroup> normalPanels = panelGroups
            .Where(panel => !panel.IsThicknessPanel)
            .ToList();

        // 기존에는 패널의 기준 외곽선(BasePolyline)이 16꼭짓점인 경우만
        // 재생성했다. 따라서 "바깥 사각형 -> 안쪽 사각형 -> 16꼭짓점 형상"
        // 구조에서는 안쪽 16꼭짓점 형상이 패널 구성원인데도 누락됐다.
        // 이제 각 일반 패널의 모든 구성원을 검사하여 내부에 들어 있는
        // 16꼭짓점 LWPOLYLINE도 같은 재생성 대상으로 포함한다.
        List<(PanelGroup Panel, EntityData Polyline)> specialPolylines = new();
        HashSet<string> specialPolylineHandles = new(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (PanelGroup panel in normalPanels)
        {
            foreach (EntityData entity in panel.Entities)
            {
                if (entity.Entity is not LwPolyline polyline ||
                    !polyline.IsClosed ||
                    polyline.Vertices.Count != 16)
                {
                    continue;
                }

                if (!specialPolylineHandles.Add(entity.Handle))
                {
                    continue;
                }

                specialPolylines.Add((panel, entity));
            }
        }

        if (specialPolylines.Count == 0)
        {
            return;
        }


        // 어느 패널이든 치구 레이어 모서리 구멍이 있으면 반지름 기준으로 사용할 수 있다.
        // 발견된 모서리 구멍 중 가장 큰 현재 반지름을 사용한다.
        Circle? radiusSource = normalPanels
            .SelectMany(panel =>
            {
                HashSet<string> handles =
                    FindResizableCornerHoleHandles(panel);

                return panel.Entities
                    .Where(entity => handles.Contains(entity.Handle))
                    .Select(entity => entity.Entity)
                    .OfType<Circle>();
            })
            .OrderByDescending(circle => circle.Radius)
            .FirstOrDefault();

        if (radiusSource == null)
        {
            return;
        }


        foreach ((PanelGroup specialPanel, EntityData specialPolyline) in
                 specialPolylines)
        {
            string oldHandle = specialPolyline.Handle;
            RebuildVertex16PanelLikeOldFourthPanel(
                drawingData,
                specialPanel,
                specialPolyline,
                targetWidth,
                targetHeight,
                radiusSource.Radius
            );

        }
    }

    private static void RebuildVertex16PanelLikeOldFourthPanel(
        DrawingData drawingData,
        PanelGroup panel,
        EntityData vertex16PanelData,
        double targetWidth,
        double targetHeight,
        double holeRadius
    )
    {
        EntityData oldPanelData = vertex16PanelData;

        if (oldPanelData.Entity is not LwPolyline oldPolyline ||
            oldPolyline.Vertices.Count != 16)
        {
            return;
        }

        double smallRadius = holeRadius - 4.5;
        double cornerLength =
            holeRadius + smallRadius * 2.0;

        if (smallRadius <= 0.0)
        {
            throw new Exception(
                $"16꼭짓점 패널 수정에 사용할 구멍 반지름이 너무 작습니다. " +
                $"Panel={oldPanelData.Handle}, Radius={holeRadius}"
            );
        }

        DimensionBounds oldPanelBounds =
            GetCurrentBounds(oldPolyline);

        // 16꼭짓점 패널의 재생성 외곽은 다른 패널을 참조하지 않는다.
        // GetVertex16TargetSize에서 계산한 목표 X/Y를 그대로 사용한다.
        if (targetWidth <= 0.0 || targetHeight <= 0.0)
        {
            throw new Exception(
                $"16꼭짓점 패널의 목표 크기가 잘못되었습니다. " +
                $"Panel={oldPanelData.Handle}, " +
                $"Target={targetWidth}x{targetHeight}"
            );
        }

        if (targetWidth <= cornerLength * 2.0 ||
            targetHeight <= cornerLength * 2.0)
        {
            MessageBox.Show(
                "16꼭짓점 패널을 수정할 공간이 부족하여\n" +
                "해당 패널은 수정하지 않고 계속 진행합니다.",
                "16꼭짓점 패널 경고",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );

            // 경고 확인 후 해당 패널만 기존 형상으로 유지하고
            // 나머지 수정, 재배치 및 DWG 저장은 계속 진행한다.
            return;
        }

        double centerX =
            (oldPanelBounds.MinX + oldPanelBounds.MaxX) / 2.0;

        double centerY =
            (oldPanelBounds.MinY + oldPanelBounds.MaxY) / 2.0;

        double minX = centerX - targetWidth / 2.0;
        double maxX = centerX + targetWidth / 2.0;
        double minY = centerY - targetHeight / 2.0;
        double maxY = centerY + targetHeight / 2.0;

        const double quarterArcBulge =
            0.4142135623730950488;

        LwPolyline replacement = new()
        {
            IsClosed = true,
            Layer = oldPolyline.Layer,
            Color = oldPolyline.Color,
            LineType = oldPolyline.LineType,
            LineTypeScale = oldPolyline.LineTypeScale
        };

        AddVertex16PanelPoint(replacement, minX, minY + cornerLength, quarterArcBulge);
        AddVertex16PanelPoint(replacement, minX + smallRadius, minY + smallRadius + holeRadius, -quarterArcBulge);
        AddVertex16PanelPoint(replacement, minX + smallRadius + holeRadius, minY + smallRadius, quarterArcBulge);
        AddVertex16PanelPoint(replacement, minX + cornerLength, minY, 0.0);

        AddVertex16PanelPoint(replacement, maxX - cornerLength, minY, quarterArcBulge);
        AddVertex16PanelPoint(replacement, maxX - smallRadius - holeRadius, minY + smallRadius, -quarterArcBulge);
        AddVertex16PanelPoint(replacement, maxX - smallRadius, minY + smallRadius + holeRadius, quarterArcBulge);
        AddVertex16PanelPoint(replacement, maxX, minY + cornerLength, 0.0);

        AddVertex16PanelPoint(replacement, maxX, maxY - cornerLength, quarterArcBulge);
        AddVertex16PanelPoint(replacement, maxX - smallRadius, maxY - smallRadius - holeRadius, -quarterArcBulge);
        AddVertex16PanelPoint(replacement, maxX - smallRadius - holeRadius, maxY - smallRadius, quarterArcBulge);
        AddVertex16PanelPoint(replacement, maxX - cornerLength, maxY, 0.0);

        AddVertex16PanelPoint(replacement, minX + cornerLength, maxY, quarterArcBulge);
        AddVertex16PanelPoint(replacement, minX + smallRadius + holeRadius, maxY - smallRadius, -quarterArcBulge);
        AddVertex16PanelPoint(replacement, minX + smallRadius, maxY - smallRadius - holeRadius, quarterArcBulge);
        AddVertex16PanelPoint(replacement, minX, maxY - cornerLength, 0.0);

        CadDocument document = drawingData.Document;

        document.Entities.Remove(oldPolyline);
        drawingData.Entities.Remove(oldPanelData);
        document.Entities.Add(replacement);

        EntityData replacementData =
            CreateEntityData(replacement);

        drawingData.Entities.Add(replacementData);

        int memberIndex = panel.Entities.FindIndex(entity =>
            ReferenceEquals(entity, oldPanelData) ||
            ReferenceEquals(entity.Entity, oldPolyline)
        );

        if (memberIndex >= 0)
        {
            panel.Entities[memberIndex] = replacementData;
        }
        else
        {
            panel.Entities.Add(replacementData);
        }

        // 16꼭짓점 형상이 패널의 기준 외곽선이었던 경우에만 기준 객체를
        // 교체한다. 사각형 안쪽의 구성원인 경우에는 바깥/안쪽 사각형을
        // 그대로 기준으로 유지하고 구성원만 새 형상으로 교체한다.
        if (ReferenceEquals(panel.BasePolyline, oldPanelData) ||
            ReferenceEquals(panel.BasePolyline.Entity, oldPolyline))
        {
            panel.BasePolyline = replacementData;
        }
    }

    /// <summary>
    /// 일반 패널 내부의 치구 레이어 원 중 패널 중심에서 가장 먼 원 최대 4개를
    /// 크기 변경 대상 모서리 구멍으로 선택한다.
    /// 볼트 구멍 레이어와 두께 패널의 원은 제외한다.
    /// </summary>
    private static HashSet<string> FindResizableCornerHoleHandles(
        PanelGroup panel
    )
    {
        if (panel.IsThicknessPanel)
        {
            return new HashSet<string>();
        }

        List<EntityData> candidates = panel.Entities
            .Where(entity => entity.Entity is Circle)
            .Where(entity =>
                !string.Equals(
                    entity.LayerName,
                    "볼트 구멍",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Where(entity =>
                string.Equals(
                    entity.LayerName,
                    "치구",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Where(entity =>
                entity.CenterX >= panel.BasePolyline.MinX &&
                entity.CenterX <= panel.BasePolyline.MaxX &&
                entity.CenterY >= panel.BasePolyline.MinY &&
                entity.CenterY <= panel.BasePolyline.MaxY
            )
            .OrderByDescending(entity =>
            {
                double dx =
                    entity.CenterX - panel.BasePolyline.CenterBoxX;

                double dy =
                    entity.CenterY - panel.BasePolyline.CenterBoxY;

                return dx * dx + dy * dy;
            })
            .Take(4)
            .ToList();

        // 모서리 구멍은 최소 두 개 이상 발견됐을 때만 크기를 변경한다.
        // 중앙에 원 하나만 있는 도면을 잘못 수정하는 것을 방지한다.
        if (candidates.Count < 2)
        {
            return new HashSet<string>();
        }

        return candidates
            .Select(entity => entity.Handle)
            .ToHashSet();
    }

    /// <summary>
    /// 전 버전의 ResizeAndMoveCornerCircles와 같은 방식으로 처리한다.
    /// 패널 크기 변화율의 평균으로 새 반지름을 구한 뒤 5단위로 반올림하고,
    /// 반지름 변화분만큼 중심을 안쪽으로 보정해 외곽 쪽 간격이 유지되게 한다.
    /// </summary>
    private static bool ResizeAndMoveCornerHole(
        EntityData circleData,
        Circle circle,
        EntityData originalPanel,
        double widthDelta,
        double heightDelta
    )
    {
        double originalWidth = originalPanel.Width;
        double originalHeight = originalPanel.Height;

        double targetWidth = originalWidth + widthDelta;
        double targetHeight = originalHeight + heightDelta;

        if (originalWidth <= 0.0 ||
            originalHeight <= 0.0 ||
            targetWidth <= 0.0 ||
            targetHeight <= 0.0)
        {
            throw new Exception(
                $"구멍 크기 계산에 사용할 패널 크기가 잘못되었습니다. " +
                $"Panel={originalPanel.Handle}, " +
                $"Original={originalWidth}x{originalHeight}, " +
                $"Target={targetWidth}x{targetHeight}"
            );
        }

        double widthScale = targetWidth / originalWidth;
        double heightScale = targetHeight / originalHeight;
        double averageScale = (widthScale + heightScale) / 2.0;

        double oldRadius = circle.Radius;
        const double radiusStep = 2.5;

        // 수정된 모서리 구멍 반지름은 2.5 배수로 반올림한다.
        double newRadius = radiusStep * Math.Round(
            oldRadius * averageScale / radiusStep,
            MidpointRounding.AwayFromZero
        );

        // 너무 작아져 0이 되는 경우를 막는다.
        newRadius = Math.Max(5.0, newRadius);

        double newDiameter = newRadius * 2.0;

        // 평균 배율로 계산하고 반올림한 구멍의 지름(2R)보다
        // 목표 가로 또는 세로가 작으면, 더 짧은 목표 변의 배율만 사용해
        // 구멍 반지름을 다시 계산한다.
        if (targetWidth < newDiameter ||
            targetHeight < newDiameter)
        {
            double shorterSideScale =
                targetWidth <= targetHeight
                    ? widthScale
                    : heightScale;

            newRadius = radiusStep * Math.Round(
                oldRadius * shorterSideScale / radiusStep,
                MidpointRounding.AwayFromZero
            );

            newRadius = Math.Max(5.0, newRadius);
        }

        double radiusDifference = newRadius - oldRadius;
        double moveX = 0.0;
        double moveY = 0.0;

        bool isLeft =
            circleData.CenterX < originalPanel.CenterBoxX;

        bool isRight =
            circleData.CenterX > originalPanel.CenterBoxX;

        bool isTop =
            circleData.CenterY > originalPanel.CenterBoxY;

        bool isBottom =
            circleData.CenterY < originalPanel.CenterBoxY;

        string horizontalSide = isLeft ? "왼쪽" : isRight ? "오른쪽" : "중앙X";
        string verticalSide = isTop ? "위쪽" : isBottom ? "아래쪽" : "중앙Y";

        // 패널 자체의 크기 변화에 따른 이동.
        if (isLeft)
        {
            moveX -= widthDelta / 2.0;
        }
        else if (isRight)
        {
            moveX += widthDelta / 2.0;
        }

        if (isTop)
        {
            moveY += heightDelta / 2.0;
        }
        else if (isBottom)
        {
            moveY -= heightDelta / 2.0;
        }

        // 반지름이 커지면 중심을 패널 안쪽으로 이동시켜
        // 구멍 외곽과 패널 벽 사이의 관계를 유지한다.
        if (isLeft)
        {
            moveX += radiusDifference;
        }
        else if (isRight)
        {
            moveX -= radiusDifference;
        }

        if (isTop)
        {
            moveY -= radiusDifference;
        }
        else if (isBottom)
        {
            moveY += radiusDifference;
        }

        circle.Radius = newRadius;
        circle.Center = new XYZ(
            circle.Center.X + moveX,
            circle.Center.Y + moveY,
            circle.Center.Z
        );


        return Math.Abs(newRadius - oldRadius) > 0.000001;
    }

    /// <summary>
    /// 꼭짓점이 16개인 특수 패널을 변경된 모서리 구멍 반지름에 맞춰 다시 만든다.
    /// 현재 수정된 패널 외곽 크기와 중심은 유지하고, 각 모서리를
    /// (R-4), R, (R-4) 반지름의 90도 호 3개로 구성한다.
    /// </summary>
    private static void AddVertex16PanelPoint(
        LwPolyline polyline,
        double x,
        double y,
        double bulge
    )
    {
        LwPolyline.Vertex vertex = new(
            new XY(x, y)
        )
        {
            Bulge = bulge
        };

        polyline.Vertices.Add(vertex);
    }

    /// <summary>
    /// 가로형 또는 세로형 두께 패널 내부의 빨간 닫힌 사각형인지 확인한다.
    /// 빨간색은 ACI 1 기준이며, 기준 두께 패널 자신과 볼트 구멍은 제외한다.
    /// </summary>
    private static bool IsThicknessPanelRedBlock(
        PanelGroup panel,
        EntityData entity
    )
    {
        if (!panel.IsThicknessPanel ||
            panel.ThicknessDirection == ThicknessPanelDirection.None)
        {
            return false;
        }

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

        // AutoCAD 기본 빨간색 ACI 번호.
        if (entity.ColorIndex != 1)
        {
            return false;
        }

        const double tolerance = 0.001;

        // 가로형은 좌우 끝 박스, 세로형은 상하 끝 박스를 대상으로 한다.
        if (panel.ThicknessDirection == ThicknessPanelDirection.Horizontal)
        {
            return
                entity.CenterBoxX >= panel.BasePolyline.MinX - tolerance &&
                entity.CenterBoxX <= panel.BasePolyline.MaxX + tolerance &&
                entity.CenterBoxY >= panel.BasePolyline.MinY - panel.SearchMargin - tolerance &&
                entity.CenterBoxY <= panel.BasePolyline.MaxY + panel.SearchMargin + tolerance;
        }

        return
            entity.CenterBoxX >= panel.BasePolyline.MinX - panel.SearchMargin - tolerance &&
            entity.CenterBoxX <= panel.BasePolyline.MaxX + panel.SearchMargin + tolerance &&
            entity.CenterBoxY >= panel.BasePolyline.MinY - tolerance &&
            entity.CenterBoxY <= panel.BasePolyline.MaxY + tolerance;
    }

    private static bool IsInnerWhitePolylineUpTo8Vertices(
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

        if (!IsClosedPolylineWithAtMost8Vertices(entity))
        {
            return false;
        }

        if (entity.ColorIndex != 0 &&
            entity.ColorIndex != 7)
        {
            return false;
        }

        // 닫힌 3~8꼭짓점 폴리선의 실제 외곽이
        // 바깥 기준 사각형 안에 완전히 들어간 경우만 내부 도형으로 본다.
        return IsCompletelyInside(
            entity,
            panel.BasePolyline,
            0.001
        );
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
    private sealed class HoleCircleGroup
    {
        public required string RepresentativeHandle { get; init; }
        public required double CenterX { get; init; }
        public required double CenterY { get; init; }
        public required double Radius { get; init; }
    }

    private sealed class HoleClearanceWarning
    {
        public required int PanelNumber { get; init; }
        public required string BoltHandle { get; init; }
        public required string HoleHandle { get; init; }
        public required double Clearance { get; init; }
        public required double CenterDistance { get; init; }
        public required double BoltRadius { get; init; }
        public required double HoleRadius { get; init; }
    }

    private static void ShowBoltHoleClearanceWarnings(
        IReadOnlyList<PanelGroup> panelGroups,
        double maximumClearance,
        double centerTolerance = 0.001
    )
    {
        List<HoleClearanceWarning> warnings = new();

        foreach (PanelGroup panel in panelGroups)
        {
            List<EntityData> circles = panel.Entities
                .Where(entity => entity.Entity is Circle)
                .GroupBy(entity => entity.Handle)
                .Select(group => group.First())
                .ToList();

            List<HoleCircleGroup> boltHoles = BuildHoleCircleGroups(
                circles.Where(entity =>
                    string.Equals(
                        entity.LayerName,
                        "볼트 구멍",
                        StringComparison.OrdinalIgnoreCase
                    )),
                centerTolerance
            );

            List<HoleCircleGroup> normalHoles = BuildHoleCircleGroups(
                circles.Where(entity =>
                    !ReferenceEquals(entity, panel.BasePolyline) &&
                    !string.Equals(
                        entity.LayerName,
                        "볼트 구멍",
                        StringComparison.OrdinalIgnoreCase
                    )),
                centerTolerance
            );

            foreach (HoleCircleGroup boltHole in boltHoles)
            {
                foreach (HoleCircleGroup normalHole in normalHoles)
                {
                    double dx = boltHole.CenterX - normalHole.CenterX;
                    double dy = boltHole.CenterY - normalHole.CenterY;
                    double centerDistance = Math.Sqrt(dx * dx + dy * dy);
                    double clearance =
                        centerDistance - boltHole.Radius - normalHole.Radius;

                    if (clearance > maximumClearance)
                    {
                        continue;
                    }

                    warnings.Add(new HoleClearanceWarning
                    {
                        PanelNumber = panel.Number,
                        BoltHandle = boltHole.RepresentativeHandle,
                        HoleHandle = normalHole.RepresentativeHandle,
                        Clearance = clearance,
                        CenterDistance = centerDistance,
                        BoltRadius = boltHole.Radius,
                        HoleRadius = normalHole.Radius
                    });
                }
            }
        }

        if (warnings.Count == 0)
        {
            return;
        }


        List<HoleClearanceWarning> ordered = warnings
            .OrderBy(item => item.Clearance)
            .ThenBy(item => item.PanelNumber)
            .ToList();

        const int maxDisplay = 30;
        List<string> lines = new()
        {
            $"볼트 구멍과 일반 구멍 사이 간격이 {maximumClearance:0.###} 이하인 위치가 {ordered.Count}개 발견되었습니다.",
            "",
            "간격 = 중심거리 - 볼트 구멍 반지름 - 일반 구멍 반지름",
            ""
        };

        foreach (HoleClearanceWarning warning in ordered.Take(maxDisplay))
        {
            lines.Add(
                $"패널 {warning.PanelNumber} | " +
                $"볼트={warning.BoltHandle} | " +
                $"구멍={warning.HoleHandle} | " +
                $"간격={warning.Clearance:0.###} | " +
                $"중심거리={warning.CenterDistance:0.###} | " +
                $"R={warning.BoltRadius:0.###}+{warning.HoleRadius:0.###}"
            );
        }

        if (ordered.Count > maxDisplay)
        {
            lines.Add("");
            lines.Add($"나머지 {ordered.Count - maxDisplay}개는 생략되었습니다.");
        }

        lines.Add("");
        lines.Add("경고가 있어도 수정된 DWG는 그대로 저장됩니다.");

        MessageBox.Show(
            string.Join(Environment.NewLine, lines),
            "구멍 간격 경고",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning
        );
    }

    private static List<HoleCircleGroup> BuildHoleCircleGroups(
        IEnumerable<EntityData> circleEntities,
        double centerTolerance
    )
    {
        List<EntityData> circles = circleEntities
            .Where(entity => entity.Entity is Circle)
            .GroupBy(entity => entity.Handle)
            .Select(group => group.First())
            .ToList();

        List<HoleCircleGroup> result = new();
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

        foreach (EntityData seed in circles)
        {
            if (used.Contains(seed.Handle))
            {
                continue;
            }

            Circle seedCircle = (Circle)seed.Entity;

            List<EntityData> sameCenter = circles
                .Where(candidate => !used.Contains(candidate.Handle))
                .Where(candidate =>
                {
                    Circle circle = (Circle)candidate.Entity;
                    return
                        Math.Abs(circle.Center.X - seedCircle.Center.X) <= centerTolerance &&
                        Math.Abs(circle.Center.Y - seedCircle.Center.Y) <= centerTolerance;
                })
                .ToList();

            foreach (EntityData item in sameCenter)
            {
                used.Add(item.Handle);
            }

            EntityData outermost = sameCenter
                .OrderByDescending(item => ((Circle)item.Entity).Radius)
                .First();

            Circle outerCircle = (Circle)outermost.Entity;

            result.Add(new HoleCircleGroup
            {
                RepresentativeHandle = outermost.Handle,
                CenterX = outerCircle.Center.X,
                CenterY = outerCircle.Center.Y,
                Radius = outerCircle.Radius
            });
        }

        return result;
    }

    private static void ArrangePanelGroupsWithGap(
        IReadOnlyList<PanelGroup> panelGroups,
        double gap,
        IReadOnlyDictionary<PanelGroup, List<PanelGroup>>
            originalNestedVertex16Bindings
    )
    {
        if (gap < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap));
        }

        List<PanelGroup> normalPanels = panelGroups
            .Where(panel => !panel.IsThicknessPanel)
            .ToList();

        // 수정 전 사각형 안에 있던 16꼭짓점 패널을 원래 바깥 패널에 연결한다.
        Dictionary<PanelGroup, List<PanelGroup>> nestedVertex16Bindings =
            normalPanels.ToDictionary(
                panel => panel,
                panel => originalNestedVertex16Bindings.TryGetValue(
                    panel,
                    out List<PanelGroup>? nestedPanels)
                        ? nestedPanels
                            .Where(normalPanels.Contains)
                            .ToList()
                        : new List<PanelGroup>()
            );

        HashSet<PanelGroup> boundNestedVertex16Panels =
            nestedVertex16Bindings
                .SelectMany(binding => binding.Value)
                .ToHashSet();

        // 중첩 16꼭짓점은 독립 재정렬 대상에서 제외한다.
        // 사각형 패널과 같은 이동량만 받아 계속 내부에 남는다.
        List<PanelGroup> mainPanels = normalPanels
            .Where(panel => !boundNestedVertex16Panels.Contains(panel))
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
                thicknessBindings[mainPanels[0]],
                nestedVertex16Bindings[mainPanels[0]]
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
                    attachedPanels,
                    nestedVertex16Bindings[mainPanel]
                );

            double targetMinX =
                previousAssemblyBounds.MaxX + gap;

            double moveX =
                targetMinX - currentAssemblyBounds.MinX;


            MovePanelAssembly(
                mainPanel,
                attachedPanels,
                nestedVertex16Bindings[mainPanel],
                moveX,
                0.0
            );

            previousAssemblyBounds =
                GetAssemblyBounds(
                    mainPanel,
                    attachedPanels,
                    nestedVertex16Bindings[mainPanel]
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
        IReadOnlyList<PanelGroup> attachedPanels,
        IReadOnlyList<PanelGroup> nestedVertex16Panels
    )
    {
        List<PanelGroup> allPanels = new()
        {
            mainPanel
        };

        allPanels.AddRange(attachedPanels);
        allPanels.AddRange(nestedVertex16Panels);

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
        IReadOnlyList<PanelGroup> nestedVertex16Panels,
        double moveX,
        double moveY
    )
    {
        List<PanelGroup> allPanels = new()
        {
            mainPanel
        };

        allPanels.AddRange(attachedPanels);
        allPanels.AddRange(nestedVertex16Panels);

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
    /// 바깥 사각형과 내부의 닫힌 3~8꼭짓점 폴리선을 함께 가진 패널 안에
    /// 완전히 들어 있는
    /// 독립 16꼭짓점 패널을 찾아 원래 바깥 패널에 연결한다.
    /// 연결된 16꼭짓점 패널은 재정렬 시 독립적으로 밖으로 빠지지 않고
    /// 바깥 패널과 같은 거리만큼 이동한다.
    /// </summary>
    private static Dictionary<PanelGroup, List<PanelGroup>>
        BindNestedVertex16Panels(
            IReadOnlyList<PanelGroup> normalPanels
        )
    {
        Dictionary<PanelGroup, List<PanelGroup>> result =
            normalPanels.ToDictionary(
                panel => panel,
                _ => new List<PanelGroup>()
            );

        List<PanelGroup> hostPanels = normalPanels
            .Where(panel =>
                !IsVertex16PanelBase(panel.BasePolyline) &&
                IsClosedFourVertexRectangle(panel.BasePolyline) &&
                panel.Entities.Any(entity =>
                    !ReferenceEquals(entity, panel.BasePolyline) &&
                    IsClosedPolylineWithAtMost8Vertices(entity) &&
                    IsCompletelyInside(
                        entity,
                        panel.BasePolyline,
                        0.001
                    )
                )
            )
            .ToList();

        List<PanelGroup> vertex16Panels = normalPanels
            .Where(panel => IsVertex16PanelBase(panel.BasePolyline))
            .ToList();

        const double tolerance = 0.001;

        foreach (PanelGroup vertex16Panel in vertex16Panels)
        {
            DimensionBounds vertex16Bounds =
                GetCurrentBounds(vertex16Panel.BasePolyline.Entity);

            PanelGroup? containingPanel = hostPanels
                .Where(hostPanel =>
                {
                    DimensionBounds hostBounds =
                        GetCurrentBounds(hostPanel.BasePolyline.Entity);

                    return
                        vertex16Bounds.MinX >= hostBounds.MinX - tolerance &&
                        vertex16Bounds.MaxX <= hostBounds.MaxX + tolerance &&
                        vertex16Bounds.MinY >= hostBounds.MinY - tolerance &&
                        vertex16Bounds.MaxY <= hostBounds.MaxY + tolerance;
                })
                // 여러 사각형 패널에 포함되면 가장 가까운 작은 외곽에 연결한다.
                .OrderBy(hostPanel =>
                {
                    DimensionBounds hostBounds =
                        GetCurrentBounds(hostPanel.BasePolyline.Entity);

                    return
                        (hostBounds.MaxX - hostBounds.MinX) *
                        (hostBounds.MaxY - hostBounds.MinY);
                })
                .FirstOrDefault();

            if (containingPanel != null)
            {
                result[containingPanel].Add(vertex16Panel);
            }
        }

        return result;
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
        public required EntityData BasePolyline { get; set; }
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

    internal sealed class ChiguCircleBoltBinding
    {
        public required PanelGroup Panel { get; init; }
        public required EntityData ChiguCircle { get; init; }
        public required EntityData BoltHoleCircle { get; init; }
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

    private static bool IsThicknessPanelBase(
        EntityData panelBase
    )
    {
        // 각 후보 폴리선의 DWG 최초 로드 시점 크기를 사용한다.
        double width = panelBase.Width;
        double height = panelBase.Height;

        double longSide = Math.Max(width, height);
        double shortSide = Math.Min(width, height);

        if (shortSide <= 0.000001)
        {
            return false;
        }

        double ratio = longSide / shortSide;

        double minimumAspectRatio =
            width <= 100.0 &&
            height <= 100.0
                ? 1.5
                : 2.0;

        return ratio >= minimumAspectRatio &&
               ratio < 10.0;
    }
}