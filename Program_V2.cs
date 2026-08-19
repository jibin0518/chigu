using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;

namespace DwgAutoResize;

/// <summary>
/// DWG 자동 수정 프로그램 버전 2.
///
/// 현재 포함된 기능:
/// 1. DWG 파일 선택 UI
/// 2. DWG 파일 읽기
/// 3. 일반/두께/중첩 16꼭짓점 패널 분류
/// 4. 입력한 X/Y/두께에 맞춘 패널 및 치수 수정
/// 5. 볼트 구멍 삭제 확인과 구멍 간격 경고
/// 6. 중첩 관계를 유지한 50 간격 재배치
/// 7. 객체 검증 후 새 DWG 저장
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

            // 크기 수정 전에 원래 중첩 관계를 기억한다.
            // 이후 16꼭짓점 형상이 재생성되어 외곽 크기가 달라져도
            // 처음 들어 있던 사각형 패널과 함께 이동한다.
            Dictionary<PanelGroup, List<PanelGroup>>
                originalNestedVertex16Bindings =
                    BindNestedVertex16Panels(
                        panelGroups
                            .Where(panel => !panel.IsThicknessPanel)
                            .ToList()
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
            // 기존 크기 규칙으로 삭제 후보를 찾은 뒤 사용자에게 삭제 여부를 묻는다.
            // '예'를 선택한 경우에만 삭제하고 '아니요'이면 모두 그대로 유지한다.
            SelectBoltHolePairsByTargetWidth(
                drawingData,
                panelGroups,
                resizeInput.TargetWidth
            );

            ApplyPanelResize(
                drawingData,
                panelGroups,
                currentValues,
                resizeInput
            );

            // 크기와 치수 수정이 끝난 뒤, 현재 패널 외곽 기준으로
            // 왼쪽부터 패널 사이 간격을 정확히 50으로 재배치한다.
            ArrangePanelGroupsWithGap(
                panelGroups,
                50.0,
                originalNestedVertex16Bindings
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
    /// 일반 패널은 구성원이 2개 이상일 때 등록하고,
    /// 16꼭짓점 패널은 외곽 하나만 있어도 독립 패널로 등록한다.
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
            .ToList();

        // 각 두께 패널 후보 자신의 수정 전 크기로 최소 비율을 결정한다.
        // 후보 자체가 100x100 이하이면 1.5 이상,
        // 그 외에는 2.0 이상이며, 모든 경우 10.0 미만이어야 한다.
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
            // 중첩된 16꼭짓점 패널이 내부 객체를 먼저 소유하도록 먼저 처리한다.
            .OrderByDescending(IsVertex16PanelBase)
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
                // 16꼭짓점 독립 패널은 사방 30 검색 범위 전체를 사용하지 않고,
                // 자신의 외곽 안에 완전히 들어가는 객체만 소유한다.
                // 따라서 자신을 둘러싼 안쪽/바깥쪽 사각형은 포함하지 않는다.
                .Where(x =>
                    !isVertex16Panel ||
                    ReferenceEquals(x, panelBase) ||
                    IsCompletelyInside(x, panelBase, 0.001)
                )
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

            result.Add(panel);

            // 일반 구성 객체만 이후 패널 자동 탐색에서 제외한다.
            // 다른 패널의 기준 폴리선은 절대 assignedHandles에 넣지 않아
            // 두께 패널이 몇 개든 모두 독립적으로 인식되게 한다.
            foreach (EntityData member in members)
            {
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
            entity.LayerName == "치구" &&
            isPolyline &&
            entity.ColorIndex == 7;
    }

    /// <summary>
    /// 사각형 안에 중첩되어 있어도 독립 패널로 분리할 16꼭짓점 외곽인지 확인한다.
    /// </summary>
    private static bool IsVertex16PanelBase(
        EntityData entity
    )
    {
        return
            IsWhiteChiguPolyline(entity) &&
            entity.ObjectName == "LWPOLYLINE" &&
            entity.IsClosed &&
            entity.Vertices.Count == 16;
    }

    /// <summary>
    /// 객체의 바운딩박스가 패널 검색 범위와 조금이라도 겹치면 포함한다.
    /// 따라서 기준 폴리선 내부 객체뿐 아니라 호출 시 지정한 검색 여유 범위와
    /// 겹치는 객체도 포함된다. 현재 패널 탐색에서는 사방 30을 사용한다.
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
    /// 기준 이하이면 바깥 2그룹, 기준 초과이면 중심에 가까운 2그룹을
    /// 삭제 후보로 모은다. 후보가 하나 이상이면 확인창을 한 번 표시하고,
    /// 사용자가 '예'를 선택한 경우에만 실제 삭제한다.
    /// </summary>
    private static void SelectBoltHolePairsByTargetWidth(
        DrawingData drawingData,
        IReadOnlyList<PanelGroup> panelGroups,
        double targetWidth,
        double centerTolerance = 0.001,
        double yTolerance = 0.001
    )
    {
        List<(PanelGroup Panel, BoltHoleCircleGroup Group)>
            deletionCandidates = new();

        HashSet<string> candidateGroupKeys = new(
            StringComparer.OrdinalIgnoreCase
        );

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
                    string groupKey = string.Join(
                        "|",
                        group.Circles
                            .Select(circle => circle.Handle)
                            .OrderBy(handle => handle,
                                StringComparer.OrdinalIgnoreCase)
                    );

                    if (candidateGroupKeys.Add(groupKey))
                    {
                        deletionCandidates.Add((panel, group));
                    }
                }
            }
        }

        if (deletionCandidates.Count == 0)
        {
            return;
        }

        int candidateCircleCount = deletionCandidates
            .SelectMany(candidate => candidate.Group.Circles)
            .Select(circle => circle.Handle)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        string panelNumbers = string.Join(
            ", ",
            deletionCandidates
                .Select(candidate => candidate.Panel.Number)
                .Distinct()
                .OrderBy(number => number)
        );

        DialogResult deleteResult = MessageBox.Show(
            "일자로 배치된 볼트 구멍 중 기존 크기 규칙에 따른 " +
            "삭제 후보가 발견되었습니다.\n\n" +
            $"대상 패널: {panelNumbers}\n" +
            $"삭제 후보: {deletionCandidates.Count}쌍 " +
            $"(원 {candidateCircleCount}개)\n" +
            $"목표 가로: {targetWidth:0.###}\n\n" +
            "삭제하시겠습니까?\n\n" +
            "예: 후보 볼트 구멍 삭제\n" +
            "아니요: 삭제하지 않고 그대로 유지",
            "볼트 구멍 삭제 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2
        );

        if (deleteResult != DialogResult.Yes)
        {
            return;
        }

        HashSet<string> deletedCircleHandles = new(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var candidate in deletionCandidates)
        {
            foreach (EntityData circleData in candidate.Group.Circles)
            {
                if (!deletedCircleHandles.Add(circleData.Handle))
                {
                    continue;
                }

                RemoveEntityFromDrawing(
                    drawingData,
                    circleData
                );

                // 혹시 같은 원이 다른 패널 구성원에도 들어 있으면
                // 모든 패널 목록에서 함께 제거한다.
                foreach (PanelGroup targetPanel in panelGroups)
                {
                    targetPanel.Entities.RemoveAll(entity =>
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


        HashSet<string> processedHandles = new();

        // 모든 패널의 일반 크기 변경과 모서리 구멍 변경을 먼저 끝낸다.
        foreach (PanelGroup panel in panelGroups.OrderBy(x => x.Number))
        {
            double panelWidthDelta;
            double panelHeightDelta;

            DimensionBounds currentPanelBounds =
                GetCurrentBounds(panel.BasePolyline.Entity);

            double currentPanelWidth =
                currentPanelBounds.MaxX - currentPanelBounds.MinX;

            double currentPanelHeight =
                currentPanelBounds.MaxY - currentPanelBounds.MinY;

            if (!panel.IsThicknessPanel)
            {
                panelWidthDelta = widthDelta;
                panelHeightDelta = heightDelta;
            }
            else if (panel.ThicknessDirection == ThicknessPanelDirection.Horizontal)
            {
                // 상·하 두께 패널은 자신의 현재 크기를 기준으로
                // 최종 가로=목표 X, 최종 세로=목표 두께가 되게 한다.
                panelWidthDelta =
                    input.TargetWidth - currentPanelWidth;

                panelHeightDelta = input.TargetThickness.HasValue
                    ? input.TargetThickness.Value - currentPanelHeight
                    : 0.0;
            }
            else if (panel.ThicknessDirection == ThicknessPanelDirection.Vertical)
            {
                // 좌·우 두께 패널은 공통 HeightDelta를 더하는 방식이 아니라
                // 자신의 실제 현재 높이에서 목표 Y까지 직접 계산한다.
                // 따라서 원본 좌우 패널 높이가 기준 패널과 달라도
                // 최종 높이는 반드시 입력한 목표 Y와 같아진다.
                panelWidthDelta = input.TargetThickness.HasValue
                    ? input.TargetThickness.Value - currentPanelWidth
                    : 0.0;

                panelHeightDelta =
                    input.TargetHeight - currentPanelHeight;
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

        // 원이 없고 사각형이 정확히 2개인 패널이 있으면,
        // 두 사각형의 가로/세로 크기 차이를 재생성 여유값으로 사용한다.
        // 재생성 크기 = 목표값 - 두 사각형 크기 차이 - 9.5
        // 9.5 = 좌우(또는 상하) 여유 4.5 + 4.5 + 추가 보정 0.5
        // 해당 패널이 없으면 재생성 크기 = 목표값 - 45.5
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

            if (resizableHoleHandles.Contains(entity.Handle) &&
                entity.Entity is Circle holeCircle)
            {
                ResizeAndMoveCornerHole(
                    entity,
                    holeCircle,
                    basePolyline,
                    widthDelta,
                    heightDelta
                );

                continue;
            }

            // 상·하 가로형 두께 패널 안의 빨간 사각형은
            // 단순 이동만 하지 않고 두께 변화량만큼 세로 크기도 변경한다.
            // 가로 방향은 패널 폭 변화에 맞춰 좌우로 이동한다.
            if (IsHorizontalThicknessPanelRedBlock(panel, entity))
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

                continue;
            }

            // 좌·우 세로형 두께 패널 안의 빨간 사각형은
            // 두께 변화량만큼 가로 크기를 변경하고,
            // 패널 높이 변화에 맞춰 위/아래 위치를 이동한다.
            if (IsVerticalThicknessPanelRedBlock(panel, entity))
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

                continue;
            }

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
    /// 16꼭짓점 패널의 재생성 목표 크기를 계산한다.
    ///
    /// 조건에 맞는 기준 패널:
    /// - 원(CIRCLE)이 하나도 없음
    /// - 치수와 텍스트 개수는 무관
    /// - 닫힌 4꼭짓점 사각형이 정확히 2개
    ///
    /// 기준 패널이 있으면:
    /// X = 목표 X - 두 사각형 가로 크기 차이 - 9.5
    /// Y = 목표 Y - 두 사각형 세로 크기 차이 - 9.5
    /// 9.5 = 4.5 + 4.5 + 추가 보정 0.5
    ///
    /// 기준 패널이 없으면:
    /// X = 목표 X - 45.5
    /// Y = 목표 Y - 45.5
    /// 45.5 = 18 + 18 + 4.5 + 4.5 + 추가 보정 0.5
    /// </summary>
    private static (double Width, double Height) GetVertex16TargetSize(
        IReadOnlyList<PanelGroup> panelGroups,
        double targetWidth,
        double targetHeight
    )
    {
        const double curveMarginPerSide = 4.5;
        const double additionalCorrection = 0.5;
        const double fallbackBaseMarginPerSide = 18.0;

        const double rectangleReferenceMargin =
            curveMarginPerSide * 2.0 +
            additionalCorrection;

        const double fallbackMargin =
            fallbackBaseMarginPerSide * 2.0 +
            curveMarginPerSide * 2.0 +
            additionalCorrection;

        foreach (PanelGroup panel in panelGroups
            .Where(panel => !panel.IsThicknessPanel))
        {
            bool hasCircle = panel.Entities
                .Any(entity => entity.Entity is Circle);

            if (hasCircle)
            {
                continue;
            }

            List<EntityData> rectangles = panel.Entities
                .Where(IsClosedFourVertexRectangle)
                .GroupBy(entity => entity.Handle)
                .Select(group => group.First())
                .ToList();

            if (rectangles.Count != 2)
            {
                continue;
            }


            DimensionBounds firstBounds =
                GetCurrentBounds(rectangles[0].Entity);

            DimensionBounds secondBounds =
                GetCurrentBounds(rectangles[1].Entity);

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
                targetWidth -
                widthDifference -
                rectangleReferenceMargin;

            double calculatedHeight =
                targetHeight -
                heightDifference -
                rectangleReferenceMargin;


            if (calculatedWidth <= 0.0 ||
                calculatedHeight <= 0.0)
            {
                throw new Exception(
                    "16꼭짓점 패널 기준 크기 계산 결과가 0 이하입니다. " +
                    $"Panel={panel.BasePolyline.Handle}, " +
                    $"Target={targetWidth}x{targetHeight}, " +
                    $"RectangleDifference={widthDifference}x{heightDifference}, " +
                    $"Calculated={calculatedWidth}x{calculatedHeight}"
                );
            }

            return (
                calculatedWidth,
                calculatedHeight
            );
        }

        double fallbackWidth =
            targetWidth - fallbackMargin;

        double fallbackHeight =
            targetHeight - fallbackMargin;


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

    /// <summary>
    /// 16꼭짓점 패널을 변경된 모서리 구멍 반지름에 맞춰 다시 만든다.
    /// 기존 패널의 중심을 유지하며, 각 모서리는
    /// (R-4.5), R, (R-4.5) 반지름의 90도 호 3개로 구성한다.
    /// </summary>
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
            throw new Exception(
                $"16꼭짓점 패널을 다시 만들 공간이 부족합니다. " +
                $"Panel={oldPanelData.Handle}, " +
                $"Target={targetWidth}x{targetHeight}, " +
                $"CornerLength={cornerLength}"
            );
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
        double newRadius = 5.0 * Math.Round(
            oldRadius * averageScale / 5.0,
            MidpointRounding.AwayFromZero
        );

        // 너무 작아져 0이 되는 경우를 막는다.
        newRadius = Math.Max(5.0, newRadius);

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
    /// 16꼭짓점 패널 폴리선에 좌표와 곡률값을 가진 꼭짓점 하나를 추가한다.
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
    /// 상·하 가로형 두께 패널 내부의 빨간 닫힌 사각형인지 확인한다.
    /// 빨간색은 ACI 1 기준이며, 기준 두께 패널 자신과 볼트 구멍은 제외한다.
    /// </summary>
    private static bool IsHorizontalThicknessPanelRedBlock(
        PanelGroup panel,
        EntityData entity
    )
    {
        if (!panel.IsThicknessPanel ||
            panel.ThicknessDirection != ThicknessPanelDirection.Horizontal)
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

        // 빨간 박스 중심이 두께 패널의 기존 검색 범위 안에 있어야 한다.
        return
            entity.CenterBoxX >= panel.BasePolyline.MinX - tolerance &&
            entity.CenterBoxX <= panel.BasePolyline.MaxX + tolerance &&
            entity.CenterBoxY >= panel.BasePolyline.MinY - panel.SearchMargin - tolerance &&
            entity.CenterBoxY <= panel.BasePolyline.MaxY + panel.SearchMargin + tolerance;
    }

    /// <summary>
    /// 좌·우 세로형 두께 패널 내부의 빨간 닫힌 사각형인지 확인한다.
    /// 빨간색은 ACI 1 기준이며, 기준 두께 패널 자신과 볼트 구멍은 제외한다.
    /// </summary>
    private static bool IsVerticalThicknessPanelRedBlock(
        PanelGroup panel,
        EntityData entity
    )
    {
        if (!panel.IsThicknessPanel ||
            panel.ThicknessDirection != ThicknessPanelDirection.Vertical)
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

        if (entity.ColorIndex != 1)
        {
            return false;
        }

        const double tolerance = 0.001;

        // 빨간 박스 중심이 세로형 두께 패널의 기존 검색 범위 안에 있어야 한다.
        return
            entity.CenterBoxX >= panel.BasePolyline.MinX - panel.SearchMargin - tolerance &&
            entity.CenterBoxX <= panel.BasePolyline.MaxX + panel.SearchMargin + tolerance &&
            entity.CenterBoxY >= panel.BasePolyline.MinY - tolerance &&
            entity.CenterBoxY <= panel.BasePolyline.MaxY + tolerance;
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

        // 사각형 패널 안에 들어 있던 16꼭짓점 패널은 크기 수정에서는
        // 독립 패널로 유지하지만 재배치에서는 원래 바깥 사각형에 연결한다.
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

        // 바깥 사각형에 연결된 중첩 16꼭짓점은 독립적인 50 간격
        // 재배치 대상에서 제외한다. 단독 16꼭짓점은 그대로 포함한다.
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
    /// 사각형 패널 안에 완전히 들어 있는 16꼭짓점 독립 패널을 찾아
    /// 가장 작은 바깥 패널에 연결한다. 연결된 16꼭짓점 패널은
    /// 독립 재배치하지 않고 바깥 패널과 같은 이동량을 사용한다.
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
                IsClosedFourVertexRectangle(panel.BasePolyline))
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
                // 여러 외곽에 포함되는 경우 가장 가까운 안쪽 외곽을 선택한다.
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
    /// 저장 직전에 전체 객체를 검증한 뒤 DWG 파일을 기록한다.
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
