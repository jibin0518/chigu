using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;

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

            string reportDirectory =
                Path.GetDirectoryName(dwgPath)!;

            string reportName =
                Path.GetFileNameWithoutExtension(dwgPath);

            string reportPath =
                Path.Combine(
                    reportDirectory,
                    $"{reportName}_objects.txt"
                );

            SaveEntityReport(
                drawingData,
                reportPath
            );

            //--------------------------------압축치구--------------------------------

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

            // foreach (BoltHolePair pair in centerBoltHoles)
            // {
            //     Console.WriteLine(
            //         $"중심=({pair.CenterX}, {pair.CenterY})"
            //     );

            //     Console.WriteLine(
            //         $"바깥 원 Handle={pair.OuterCircle.Handle}, " +
            //         $"Radius={pair.OuterCircle.Radius}"
            //     );

            //     Console.WriteLine(
            //         $"안쪽 원 Handle={pair.InnerCircle.Handle}, " +
            //         $"Radius={pair.InnerCircle.Radius}"
            //     );

            //     Console.WriteLine();
            // }

            // Console.WriteLine(
            //     $"Handle={topLeftCircle.Handle}, " +
            //     $"X={topLeftCircle.CenterX}, " +
            //     $"Y={topLeftCircle.CenterY}"
            // );

            // Console.WriteLine(
            //     $"Handle={topRightCircle.Handle}, " +
            //     $"X={topRightCircle.CenterX}, " +
            //     $"Y={topRightCircle.CenterY}"
            // );

            // Console.WriteLine(
            //     $"Handle={bottomLeftCircle.Handle}, " +
            //     $"X={bottomLeftCircle.CenterX}, " +
            //     $"Y={bottomLeftCircle.CenterY}"
            // );

            // Console.WriteLine(
            //     $"Handle={bottomRightCircle.Handle}, " +
            //     $"X={bottomRightCircle.CenterX}, " +
            //     $"Y={bottomRightCircle.CenterY}"
            // );
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

            //--------------------------------압축치구--------------------------------
            //--------------------------------중간치구판--------------------------------
            EntityData secondOuterPanel =
                FindSecondOuterPanel(
                    drawingData,
                    mainChigu,
                    3.0
                );

            List<EntityData> secondPanelCircles =
                FindSecondPanelLargeCircles(
                    drawingData,
                    secondOuterPanel,
                    12.0,
                    2.0
                );

            EntityData secondTopLeftCircle =
                secondPanelCircles[0];

            EntityData secondTopRightCircle =
                secondPanelCircles[1];

            EntityData secondBottomLeftCircle =
                secondPanelCircles[2];

            EntityData secondBottomRightCircle =
                secondPanelCircles[3];

            List<BoltHolePair> boltHoles =
                FindSecondPanelBoltHoles(
                    drawingData,
                    secondOuterPanel
                );

            EntityData centerBox =
                FindSecondPanelCenterBox(
                    drawingData,
                    secondOuterPanel
                );

            List<BoltHolePair> secondPanelBoltHoles =
                FindBoltHolesInsidePanel(
                    drawingData,
                    secondOuterPanel
                );
                     

            // Console.WriteLine("두 번째 겉면");

            // Console.WriteLine(
            //     $"Handle={secondOuterPanel.Handle}"
            // );

            // Console.WriteLine(
            //     $"크기={secondOuterPanel.Width} x " +
            //     $"{secondOuterPanel.Height}"
            // );

            // Console.WriteLine(
            //     $"중심=({secondOuterPanel.CenterBoxX}, " +
            //     $"{secondOuterPanel.CenterBoxY})"
            // );

            // Console.WriteLine("사이드 바람 구멍");

            // foreach (EntityData circle in secondPanelCircles)
            // {
            //     Console.WriteLine(
            //         $"Handle={circle.Handle}, " +
            //         $"Center=({circle.CenterX}, {circle.CenterY}), " +
            //         $"Radius={circle.Radius}"
            //     );
            // }

            // Console.WriteLine("볼트 구멍 원");

            // foreach (BoltHolePair pair in boltHoles)
            // {
            //     Console.WriteLine(
            //         $"Outer={pair.OuterCircle.Handle}, " +
            //         $"Inner={pair.InnerCircle.Handle}, " +
            //         $"Center=({pair.CenterX}, {pair.CenterY})"
            //     );
            // }

            // Console.WriteLine("볼트 구멍 박스");
            // Console.WriteLine($"Handle={centerBox.Handle}");
            // Console.WriteLine(
            //     $"Center=({centerBox.CenterBoxX}, {centerBox.CenterBoxY})"
            // );
            // Console.WriteLine(
            //     $"Size={centerBox.Width} x {centerBox.Height}"
            // );
            //--------------------------------중간치구판--------------------------------
            //--------------------------------중간치구벽--------------------------------
            EntityData thirdOuterPanel =
                FindThirdOuterPanel(
                    drawingData,
                    mainChigu,
                    secondOuterPanel,
                    3.0
                );

            EntityData thirdInnerBox =
                FindThirdPanelInnerBox(
                    drawingData,
                    thirdOuterPanel
                );   
            
            // Console.WriteLine("세 번째 겉면");
            // Console.WriteLine($"Handle={thirdOuterPanel.Handle}");
            // Console.WriteLine(
            //     $"크기={thirdOuterPanel.Width} x " +
            //     $"{thirdOuterPanel.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({thirdOuterPanel.CenterBoxX}, " +
            //     $"{thirdOuterPanel.CenterBoxY})"
            // );

            // Console.WriteLine("세 번째 내부 박스");
            // Console.WriteLine($"Handle={thirdInnerBox.Handle}");
            // Console.WriteLine(
            //     $"크기={thirdInnerBox.Width} x " +
            //     $"{thirdInnerBox.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({thirdInnerBox.CenterBoxX}, " +
            //     $"{thirdInnerBox.CenterBoxY})"
            // );

            //--------------------------------중간치구벽--------------------------------
            //--------------------------------안쪽 중판--------------------------------

            EntityData? fourthPanel =
                FindFourthPanelOptional(
                    drawingData,
                    thirdOuterPanel
                );

            List<BoltHolePair> fourthBoltHoles = new();

            if (fourthPanel != null)
            {
                fourthBoltHoles =
                    FindBoltHolesInsidePanel(
                        drawingData,
                        fourthPanel
                    );
            }


            // Console.WriteLine("4번째 패널");
            // Console.WriteLine($"Handle={fourthPanel.Handle}");
            // Console.WriteLine(
            //     $"Vertices={fourthPanel.Vertices.Count}"
            // );
            // Console.WriteLine(
            //     $"크기={fourthPanel.Width} x {fourthPanel.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({fourthPanel.CenterBoxX}, " +
            //     $"{fourthPanel.CenterBoxY})"
            // );
            
            // Console.WriteLine(
            //     $"4번째 패널 볼트 구멍 쌍: {fourthBoltHoles.Count}개"
            // );

            // foreach (BoltHolePair pair in fourthBoltHoles)
            // {
            //     Console.WriteLine(
            //         $"Center=({pair.CenterX}, {pair.CenterY}), " +
            //         $"바깥 Handle={pair.OuterCircle.Handle}, " +
            //         $"바깥 지름={pair.OuterCircle.Diameter}, " +
            //         $"안쪽 Handle={pair.InnerCircle.Handle}, " +
            //         $"안쪽 지름={pair.InnerCircle.Diameter}"
            //     );
            // }
            //--------------------------------안쪽 중판--------------------------------
            //--------------------------------밖쪽 중판--------------------------------
            
            EntityData panelBeforeFifth =
                fourthPanel ?? thirdOuterPanel;

            EntityData fifthOuterPanel =
                FindFifthOuterPanel(
                    drawingData,
                    mainChigu,
                    panelBeforeFifth,
                    2.0
                );
            List<EntityData> fifthPanelCircles =
                FindFifthPanelLargeCircles(
                    drawingData,
                    fifthOuterPanel,
                    10.0,
                    2.0
                );
            // EntityData fifthTopLeft =
            //     fifthPanelCircles[0];

            // EntityData fifthTopRight =
            //     fifthPanelCircles[1];

            // EntityData fifthBottomLeft =
            //     fifthPanelCircles[2];

            // EntityData fifthBottomRight =
            //     fifthPanelCircles[3];
            // Console.WriteLine("21");
            
            List<BoltHolePair> fifthPanelBoltHoles =
                FindBoltHolesInsidePanel(
                    drawingData,
                    fifthOuterPanel
                );

            

            // Console.WriteLine("5번째 겉면");
            // Console.WriteLine(
            //     $"Handle={fifthOuterPanel.Handle}"
            // );
            // Console.WriteLine(
            //     $"크기={fifthOuterPanel.Width} x " +
            //     $"{fifthOuterPanel.Height}"
            // );
            // Console.WriteLine(
            //     $"중심=({fifthOuterPanel.CenterBoxX}, " +
            //     $"{fifthOuterPanel.CenterBoxY})"
            // );
            // foreach (EntityData circle in fifthPanelCircles)
            // {
            //     Console.WriteLine(
            //         $"Handle={circle.Handle}, " +
            //         $"Center=({circle.CenterX}, {circle.CenterY}), " +
            //         $"Radius={circle.Radius}"
            //     );
            // }

            // Console.WriteLine(
            //     $"5번째 패널 볼트 구멍 쌍: {fifthPanelBoltHoles.Count}개"
            // );

            // foreach (BoltHolePair pair in fifthPanelBoltHoles)
            // {
            //     Console.WriteLine(
            //         $"Center=({pair.CenterX}, {pair.CenterY}), " +
            //         $"바깥 Handle={pair.OuterCircle.Handle}, " +
            //         $"바깥 지름={pair.OuterCircle.Diameter}, " +
            //         $"안쪽 Handle={pair.InnerCircle.Handle}, " +
            //         $"안쪽 지름={pair.InnerCircle.Diameter}"
            //     );
            // }

            //--------------------------------바닥판-------------------------------
            EntityData sixthOuterPanel =
                FindSixthOuterPanel(
                    drawingData,
                    mainChigu,
                    fifthOuterPanel
                );
            
            List<BoltHolePair> sixthPanelBoltHoles =
                FindBoltHolesInsidePanel(
                    drawingData,
                    sixthOuterPanel
                );

            // Console.WriteLine("6번째 패널");
            // Console.WriteLine($"Handle={sixthOuterPanel.Handle}");
            // Console.WriteLine($"Center=({sixthOuterPanel.CenterBoxX}, {sixthOuterPanel.CenterBoxY})");
            // Console.WriteLine($"Size={sixthOuterPanel.Width} x {sixthOuterPanel.Height}");

            // Console.WriteLine(
            //     $"6번째 패널 볼트 구멍 쌍: {sixthPanelBoltHoles.Count}개"
            // );

            // foreach (BoltHolePair pair in sixthPanelBoltHoles)
            // {
            //     Console.WriteLine(
            //         $"Center=({pair.CenterX}, {pair.CenterY}), " +
            //         $"바깥 Handle={pair.OuterCircle.Handle}, " +
            //         $"바깥 지름={pair.OuterCircle.Diameter}, " +
            //         $"안쪽 Handle={pair.InnerCircle.Handle}, " +
            //         $"안쪽 지름={pair.InnerCircle.Diameter}"
            //     );
            // }
        
        double currentDepth =
            topPlate.Height;

        ResizeInput? resizeInput =
            ShowResizeInputDialog(
                mainChigu,
                currentDepth
            );

        if (resizeInput == null)
        {
            return;
        }

        double widthDelta = resizeInput.WidthDelta;
        double heightDelta = resizeInput.HeightDelta;
        double depthDelta = resizeInput.DepthDelta;
        // 중앙 볼트 구멍이 4쌍이면
        // 목표 가로에 따라 안쪽 또는 바깥쪽 2쌍만 유지
        centerBoltHoles =
            SelectCenterBoltHolesByWidth(
                document,
                centerBoltHoles,
                mainChigu,
                resizeInput.TargetWidth
            );

        bool removeThicknessPlates =
            resizeInput.TargetDepth <= 36.0;
        double originalWidth =
            mainChigu.Width + 0.7;

        double originalHeight =
            mainChigu.Height + 0.7;

        double targetWidth =
            resizeInput.TargetWidth;

        double targetHeight =
            resizeInput.TargetHeight;

        // 크기 수정 후에도 각 판의 내부 객체를 함께 이동할 수 있도록
        // 수정 전 위치를 기준으로 패널별 객체 묶음을 먼저 만든다.
        List<EntityData> firstPanelGroup = new()
        {
            mainChigu
        };

        if (!removeThicknessPlates)
        {
            firstPanelGroup.Add(topPlate);
            firstPanelGroup.Add(bottomPlate);
            firstPanelGroup.Add(leftPlate);
            firstPanelGroup.Add(rightPlate);

            firstPanelGroup.AddRange(topRedBlocks);
            firstPanelGroup.AddRange(bottomRedBlocks);
        }

        firstPanelGroup.AddRange(largeCircles);
        firstPanelGroup.AddRange(
            GetBoltHoleEntities(centerBoltHoles)
        );
        firstPanelGroup.AddRange(topRedBlocks);
        firstPanelGroup.AddRange(bottomRedBlocks);
        firstPanelGroup.AddRange(largeCircles);
        firstPanelGroup.AddRange(GetBoltHoleEntities(centerBoltHoles));

        List<EntityData> secondPanelGroup =
            GetEntitiesInsidePanel(drawingData, secondOuterPanel);

        List<EntityData> thirdPanelGroup =
            GetEntitiesInsidePanel(drawingData, thirdOuterPanel);

        List<EntityData>? fourthPanelGroup = null;

        if (fourthPanel != null)
        {
            fourthPanelGroup =
                GetEntitiesInsidePanel(drawingData, fourthPanel);
        }

        List<EntityData> fifthPanelGroup =
            GetEntitiesInsidePanel(drawingData, fifthOuterPanel);

        List<EntityData> sixthPanelGroup =
            GetEntitiesInsidePanel(drawingData, sixthOuterPanel);

        // 실제 도형 크기 수정
        // "볼트 구멍" 레이어 객체는 수정 함수에 넘기지 않으므로
        // 크기와 위치가 모두 그대로 유지된다.
        //ResizePolylineFromCenter(mainChigu, widthDelta, heightDelta);

        

        ResizePolylineFromCenter(
            mainChigu,
            widthDelta,
            heightDelta
        );

        if (!removeThicknessPlates)
        {
            ResizePolylineFromCenter(
                topPlate,
                widthDelta,
                depthDelta
            );

            ResizePolylineFromCenter(
                bottomPlate,
                widthDelta,
                depthDelta
            );

            ResizePolylineFromCenter(
                leftPlate,
                depthDelta,
                heightDelta
            );

            ResizePolylineFromCenter(
                rightPlate,
                depthDelta,
                heightDelta
            );

            ResizePlateBlocks(
                topRedBlocks,
                mainChigu.CenterBoxX,
                widthDelta,
                depthDelta
            );

            ResizePlateBlocks(
                bottomRedBlocks,
                mainChigu.CenterBoxX,
                widthDelta,
                depthDelta
            );
        }
        else
        {
            RemoveEntity(
                document,
                topPlate
            );

            RemoveEntity(
                document,
                bottomPlate
            );

            RemoveEntity(
                document,
                leftPlate
            );

            RemoveEntity(
                document,
                rightPlate
            );

            foreach (EntityData block in topRedBlocks)
            {
                RemoveEntity(
                    document,
                    block
                );
            }

            foreach (EntityData block in bottomRedBlocks)
            {
                RemoveEntity(
                    document,
                    block
                );
            }
        }

        ResizeAndMoveCornerCircles(
            largeCircles,
            mainChigu,
            originalWidth,
            originalHeight,
            resizeInput.TargetWidth,
            resizeInput.TargetHeight
        );

        ResizePolylineFromCenter(
            secondOuterPanel,
            widthDelta,
            heightDelta
        );

        ResizeAndMoveCornerCircles(
            secondPanelCircles,
            secondOuterPanel,
            originalWidth,
            originalHeight,
            resizeInput.TargetWidth,
            resizeInput.TargetHeight
        );

        ResizePolylineFromCenter(
            thirdOuterPanel,
            widthDelta,
            heightDelta
        );

        ResizePolylineFromCenter(
            thirdInnerBox,
            widthDelta,
            heightDelta
        );

        if (fourthPanel != null)
        {
            ResizePolylineFromCenter(
                fourthPanel,
                widthDelta,
                heightDelta
            );
        }

        ResizePolylineFromCenter(
            fifthOuterPanel,
            widthDelta,
            heightDelta
        );

        ResizeAndMoveCornerCircles(
            fifthPanelCircles,
            fifthOuterPanel,
            originalWidth,
            originalHeight,
            resizeInput.TargetWidth,
            resizeInput.TargetHeight
        );

        ResizePolylineFromCenter(
            sixthOuterPanel,
            widthDelta,
            heightDelta
        );

        // 첫 번째 조립 치구는 현재 위치에 고정하고,
        // 2~6번째 패널을 순서대로 재배치하여 서로 20 간격을 유지한다.
        List<List<EntityData>> panelGroups = new()
        {
            firstPanelGroup,
            secondPanelGroup,
            thirdPanelGroup
        };

        if (fourthPanelGroup != null)
        {
            panelGroups.Add(fourthPanelGroup);
        }

        panelGroups.Add(fifthPanelGroup);
        panelGroups.Add(sixthPanelGroup);

        ArrangePanelGroupsWithGap(
            panelGroups,
            20.0
        );

        foreach (var dim in document.Entities
            .OfType<Dimension>()
            .ToList())
        {
            document.Entities.Remove(dim);
        }

        // 수정 결과를 새 DWG로 저장
        string directory =
            Path.GetDirectoryName(dwgPath)!;

        string fileName =
            Path.GetFileNameWithoutExtension(dwgPath);

        string outputPath = Path.Combine(
            directory,
            $"{fileName}_{resizeInput.TargetWidth:0.###}x" +
            $"{resizeInput.TargetHeight:0.###}x" +
            $"{resizeInput.TargetDepth:0.###}.dwg"
        );

        SaveAsNewDwg(
            document,
            outputPath
        );

        MessageBox.Show(
            $"수정 완료\n\n{outputPath}",
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

    internal sealed class ResizeInput
    {
        public double TargetWidth { get; init; }
        public double TargetHeight { get; init; }
        public double TargetDepth { get; init; }

        public double WidthDelta { get; init; }
        public double HeightDelta { get; init; }
        public double DepthDelta { get; init; }
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
            if (x.Height < mainChigu.Height * 0.6)
                continue;

            if (x.Height > mainChigu.Height * 1.4)
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
            if (x.Height < mainChigu.Height * 0.6)
                continue;

            if (x.Height > mainChigu.Height * 1.4)
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

    private static EntityData FindSecondOuterPanel(
    DrawingData drawingData,
    EntityData mainChigu,
    double sizeTolerance = 3.0
    )
    {
        EntityData? result = null;
        double bestDistance = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
            {
                continue;
            }

            if (x.ObjectName != "LWPOLYLINE")
            {
                continue;
            }

            if (!x.IsClosed)
            {
                continue;
            }

            // 첫 번째 메인 치구 자기 자신 제외
            if (ReferenceEquals(x, mainChigu))
            {
                continue;
            }

            // 두 번째 패널은 첫 번째보다 오른쪽에 있어야 함
            if (x.CenterBoxX <= mainChigu.MaxX)
            {
                continue;
            }

            // 가로 크기가 거의 같아야 함
            bool sameWidth =
                Math.Abs(x.Width - mainChigu.Width)
                <= sizeTolerance;

            // 세로 크기가 거의 같아야 함
            bool sameHeight =
                Math.Abs(x.Height - mainChigu.Height)
                <= sizeTolerance;

            if (!sameWidth || !sameHeight)
            {
                continue;
            }

            // 첫 번째 메인 치구와 세로 중심도 비슷해야 함
            double yDistance =
                Math.Abs(
                    x.CenterBoxY -
                    mainChigu.CenterBoxY
                );

            // 첫 번째 오른쪽 경계와 가장 가까운 후보 우선
            double xDistance =
                x.MinX -
                mainChigu.MaxX;

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
                "두 번째 겉면을 찾지 못했습니다."
            );
        }

        return result;
    }

    private static List<EntityData> FindSecondPanelLargeCircles(
    DrawingData drawingData,
    EntityData secondOuterPanel,
    double wallDistance = 12.0,
    double tolerance = 2.0
    )
    {
        List<EntityData> result = new();

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
            {
                continue;
            }

            if (x.ObjectName != "CIRCLE")
            {
                continue;
            }

            // 원의 중심이 두 번째 겉면 안에 있어야 함
            bool centerInside =
                x.CenterX >= secondOuterPanel.MinX &&
                x.CenterX <= secondOuterPanel.MaxX &&
                x.CenterY >= secondOuterPanel.MinY &&
                x.CenterY <= secondOuterPanel.MaxY;

            if (!centerInside)
            {
                continue;
            }

            // 겉면 벽과 원의 바깥쪽 끝 사이 거리
            double leftGap =
                (x.CenterX - x.Radius) -
                secondOuterPanel.MinX;

            double rightGap =
                secondOuterPanel.MaxX -
                (x.CenterX + x.Radius);

            double bottomGap =
                (x.CenterY - x.Radius) -
                secondOuterPanel.MinY;

            double topGap =
                secondOuterPanel.MaxY -
                (x.CenterY + x.Radius);

            bool nearLeft =
                Math.Abs(leftGap - wallDistance) <= tolerance;

            bool nearRight =
                Math.Abs(rightGap - wallDistance) <= tolerance;

            bool nearBottom =
                Math.Abs(bottomGap - wallDistance) <= tolerance;

            bool nearTop =
                Math.Abs(topGap - wallDistance) <= tolerance;

            // 좌우 벽 중 하나와 가깝고,
            // 위아래 벽 중 하나와 가까워야 모서리 원
            bool isCornerCircle =
                (nearLeft || nearRight) &&
                (nearTop || nearBottom);

            if (!isCornerCircle)
            {
                continue;
            }

            result.Add(x);
        }

        // 위쪽부터, 같은 줄에서는 왼쪽부터
        result = result
            .OrderByDescending(x => x.CenterY)
            .ThenBy(x => x.CenterX)
            .ToList();

        return result;
    }

    private static List<BoltHolePair> FindSecondPanelBoltHoles(
    DrawingData drawingData,
    EntityData secondPanel,
    double centerTolerance = 0.001
    )
    {
        List<EntityData> circles = new();

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "볼트 구멍")
                continue;

            if (x.ObjectName != "CIRCLE")
                continue;

            bool inside =
                x.CenterX >= secondPanel.MinX &&
                x.CenterX <= secondPanel.MaxX &&
                x.CenterY >= secondPanel.MinY &&
                x.CenterY <= secondPanel.MaxY;

            if (!inside)
                continue;

            circles.Add(x);
        }

        List<BoltHolePair> result = new();
        HashSet<string> used = new();

        foreach (EntityData outer in circles)
        {
            if (used.Contains(outer.Handle))
                continue;

            EntityData? inner = null;

            foreach (EntityData other in circles)
            {
                if (ReferenceEquals(outer, other))
                    continue;

                bool sameCenter =
                    Math.Abs(outer.CenterX - other.CenterX) <= centerTolerance &&
                    Math.Abs(outer.CenterY - other.CenterY) <= centerTolerance;

                if (!sameCenter)
                    continue;

                if (other.Radius >= outer.Radius)
                    continue;

                inner = other;
                break;
            }

            if (inner == null)
                continue;

            result.Add(new BoltHolePair
            {
                OuterCircle = outer,
                InnerCircle = inner
            });

            used.Add(outer.Handle);
            used.Add(inner.Handle);
        }

        // 위 → 아래
        // 같은 줄에서는 왼쪽 → 오른쪽
        result = result
            .OrderByDescending(x => x.CenterY)
            .ThenBy(x => x.CenterX)
            .ToList();

        return result;
    }

    private static EntityData FindSecondPanelCenterBox(
    DrawingData drawingData,
    EntityData secondPanel
    )
    {
        EntityData? result = null;
        double bestDistance = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            // 볼트 구멍 레이어 또는 도면층이 비어 있는 객체.
            // AutoCAD 기본 도면층 "0"도 빈 도면층처럼 취급한다.
            bool allowedLayer =
                x.LayerName == "볼트 구멍" ||
                string.IsNullOrWhiteSpace(x.LayerName) ||
                x.LayerName == "0";

            if (!allowedLayer)
            {
                continue;
            }

            if (x.ObjectName != "LWPOLYLINE")
            {
                continue;
            }

            if (!x.IsClosed)
            {
                continue;
            }

            if (x.Vertices.Count != 4)
            {
                continue;
            }

            if (ReferenceEquals(x, secondPanel))
            {
                continue;
            }

            // 패널 외곽보다 작은 도형만 허용
            if (x.Width >= secondPanel.Width ||
                x.Height >= secondPanel.Height)
            {
                continue;
            }

            // 박스 전체가 두 번째 패널 내부에 있어야 함
            bool completelyInside =
                x.MinX >= secondPanel.MinX &&
                x.MaxX <= secondPanel.MaxX &&
                x.MinY >= secondPanel.MinY &&
                x.MaxY <= secondPanel.MaxY;

            if (!completelyInside)
            {
                continue;
            }

            // 패널 중앙에 가장 가까운 사각형 선택
            double dx =
                x.CenterBoxX - secondPanel.CenterBoxX;

            double dy =
                x.CenterBoxY - secondPanel.CenterBoxY;

            double distance =
                Math.Sqrt(dx * dx + dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                result = x;
            }
        }

        if (result == null)
        {
            throw new Exception(
                "두 번째 패널 중앙 박스를 찾지 못했습니다."
            );
        }

        return result;
    }

    private static EntityData FindThirdOuterPanel(
    DrawingData drawingData,
    EntityData mainChigu,
    EntityData secondOuterPanel,
    double sizeTolerance = 3.0
    )
    {
        EntityData? result = null;
        double bestDistance = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
            {
                continue;
            }

            if (x.ObjectName != "LWPOLYLINE")
            {
                continue;
            }

            if (!x.IsClosed)
            {
                continue;
            }

            // 첫 번째, 두 번째 패널 제외
            if (ReferenceEquals(x, mainChigu) ||
                ReferenceEquals(x, secondOuterPanel))
            {
                continue;
            }

            // 두 번째 패널보다 오른쪽에 있어야 함
            if (x.CenterBoxX <= secondOuterPanel.MaxX)
            {
                continue;
            }

            // 메인 치구와 크기가 거의 같아야 함
            bool sameWidth =
                Math.Abs(x.Width - mainChigu.Width)
                <= sizeTolerance;

            bool sameHeight =
                Math.Abs(x.Height - mainChigu.Height)
                <= sizeTolerance;

            if (!sameWidth || !sameHeight)
            {
                continue;
            }

            // 세로 중심도 비슷한 후보 우선
            double yDistance =
                Math.Abs(
                    x.CenterBoxY -
                    mainChigu.CenterBoxY
                );

            // 두 번째 패널 바로 다음에 있는 후보 우선
            double xDistance =
                x.MinX -
                secondOuterPanel.MaxX;

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
                "세 번째 겉면을 찾지 못했습니다."
            );
        }

        return result;
    }

    private static EntityData FindThirdPanelInnerBox(
    DrawingData drawingData,
    EntityData thirdOuterPanel
    )
    {
        EntityData? result = null;
        double bestDistance = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
            {
                continue;
            }

            if (x.ObjectName != "LWPOLYLINE")
            {
                continue;
            }

            if (!x.IsClosed)
            {
                continue;
            }

            if (ReferenceEquals(x, thirdOuterPanel))
            {
                continue;
            }

            if (x.Vertices.Count != 4)
            {
                continue;
            }

            // 외곽보다 작아야 함
            if (x.Width >= thirdOuterPanel.Width ||
                x.Height >= thirdOuterPanel.Height)
            {
                continue;
            }

            // 내부 박스가 외곽 안에 완전히 들어가야 함
            bool completelyInside =
                x.MinX >= thirdOuterPanel.MinX &&
                x.MaxX <= thirdOuterPanel.MaxX &&
                x.MinY >= thirdOuterPanel.MinY &&
                x.MaxY <= thirdOuterPanel.MaxY;

            if (!completelyInside)
            {
                continue;
            }

            // 외곽 중심과 가장 가까운 박스 선택
            double dx =
                x.CenterBoxX -
                thirdOuterPanel.CenterBoxX;

            double dy =
                x.CenterBoxY -
                thirdOuterPanel.CenterBoxY;

            double distance =
                Math.Sqrt(dx * dx + dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                result = x;
            }
        }

        if (result == null)
        {
            throw new Exception(
                "세 번째 패널 내부 박스를 찾지 못했습니다."
            );
        }

        return result;
    }

    private static EntityData? FindFourthPanelOptional(
        DrawingData drawingData,
        EntityData thirdOuterPanel
    )
    {
        /*
         * 4번째 굴곡 패널은 메인 치구 다음으로 꼭짓점이 많은
         * 닫힌 치구 LWPOLYLINE이다.
         *
         * 4번째 패널이 없는 도면은 메인 치구를 제외한 외곽들이
         * 대부분 꼭짓점 4개짜리 사각형이므로 null을 반환한다.
         */
        List<EntityData> allCandidates = drawingData.Entities
            .Where(x =>
                x.LayerName == "치구" &&
                x.ObjectName == "LWPOLYLINE" &&
                x.IsClosed
            )
            .ToList();

        List<int> distinctVertexCounts = allCandidates
            .Select(x => x.Vertices.Count)
            .Distinct()
            .OrderByDescending(count => count)
            .ToList();

        // 메인 치구 외에 별도의 복잡한 외곽이 없으면 4번째 패널 없음.
        if (distinctVertexCounts.Count < 2)
        {
            return null;
        }

        int secondLargestVertexCount = distinctVertexCounts[1];

        // 두 번째로 많은 꼭짓점 수가 4라면 일반 사각형뿐이므로
        // 굴곡 패널이 없는 것으로 처리한다.
        if (secondLargestVertexCount <= 4)
        {
            return null;
        }

        return allCandidates
            .Where(x =>
                x.Vertices.Count == secondLargestVertexCount &&
                x.MinX > thirdOuterPanel.MaxX
            )
            .OrderBy(x => x.MinX)
            .FirstOrDefault();
    }

    private static List<BoltHolePair> FindFourthPanelBoltHoles(
    DrawingData drawingData,
    EntityData fourthPanel,
    double centerTolerance = 0.001
    )
    {
        List<EntityData> circles = new();

        // 1. 4번째 패널 안의 볼트 구멍 레이어 원만 수집
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

            bool insidePanel =
                x.CenterX >= fourthPanel.MinX &&
                x.CenterX <= fourthPanel.MaxX &&
                x.CenterY >= fourthPanel.MinY &&
                x.CenterY <= fourthPanel.MaxY;

            if (!insidePanel)
            {
                continue;
            }

            circles.Add(x);
        }

        List<BoltHolePair> result = new();
        HashSet<string> usedHandles = new();

        // 2. 중심이 같고 반지름이 다른 원 2개를 한 쌍으로 묶기
        foreach (EntityData first in circles)
        {
            if (usedHandles.Contains(first.Handle))
            {
                continue;
            }

            EntityData? matchingCircle = null;

            foreach (EntityData second in circles)
            {
                if (ReferenceEquals(first, second))
                {
                    continue;
                }

                if (usedHandles.Contains(second.Handle))
                {
                    continue;
                }

                bool sameCenter =
                    Math.Abs(first.CenterX - second.CenterX)
                        <= centerTolerance &&
                    Math.Abs(first.CenterY - second.CenterY)
                        <= centerTolerance;

                if (!sameCenter)
                {
                    continue;
                }

                // 반지름까지 같으면 겹친 동일 원이므로 제외
                if (Math.Abs(first.Radius - second.Radius)
                    <= 0.000001)
                {
                    continue;
                }

                matchingCircle = second;
                break;
            }

            if (matchingCircle == null)
            {
                continue;
            }

            EntityData outerCircle;
            EntityData innerCircle;

            if (first.Radius > matchingCircle.Radius)
            {
                outerCircle = first;
                innerCircle = matchingCircle;
            }
            else
            {
                outerCircle = matchingCircle;
                innerCircle = first;
            }

            result.Add(new BoltHolePair
            {
                OuterCircle = outerCircle,
                InnerCircle = innerCircle
            });

            usedHandles.Add(outerCircle.Handle);
            usedHandles.Add(innerCircle.Handle);
        }

        // 위쪽부터, 같은 높이면 왼쪽부터
        result = result
            .OrderByDescending(pair => pair.CenterY)
            .ThenBy(pair => pair.CenterX)
            .ToList();

        return result;
    }

    private static EntityData FindFifthOuterPanel(
        DrawingData drawingData,
        EntityData mainChigu,
        EntityData previousPanel,
        double sizeTolerance = 2.0
    )
    {
        EntityData? result = null;
        double bestScore = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
            {
                continue;
            }

            if (x.ObjectName != "LWPOLYLINE")
            {
                continue;
            }

            if (!x.IsClosed)
            {
                continue;
            }

            // 5번째 패널은 바로 앞 패널보다 오른쪽에 있어야 한다.
            // 4번째 굴곡 패널이 없으면 previousPanel은 3번째 패널이다.
            if (x.MinX <= previousPanel.MaxX)
            {
                continue;
            }

            // 메인 패널보다 가로·세로가 각각 2 작은 경우
            double widthDifference2 =
                Math.Abs(x.Width - (mainChigu.Width - 2.0));

            double heightDifference2 =
                Math.Abs(x.Height - (mainChigu.Height - 2.0));

            bool matchesMinus2 =
                widthDifference2 <= sizeTolerance &&
                heightDifference2 <= sizeTolerance;

            // 메인 패널보다 가로·세로가 각각 4 작은 경우
            double widthDifference4 =
                Math.Abs(x.Width - (mainChigu.Width - 4.0));

            double heightDifference4 =
                Math.Abs(x.Height - (mainChigu.Height - 4.0));

            bool matchesMinus4 =
                widthDifference4 <= sizeTolerance &&
                heightDifference4 <= sizeTolerance;

            if (!matchesMinus2 && !matchesMinus4)
            {
                continue;
            }

            double sizeDifference = Math.Min(
                widthDifference2 + heightDifference2,
                widthDifference4 + heightDifference4
            );

            double yDistance =
                Math.Abs(x.CenterBoxY - mainChigu.CenterBoxY);

            double xDistance =
                x.MinX - previousPanel.MaxX;

            double score =
                sizeDifference + yDistance + xDistance;

            if (score < bestScore)
            {
                bestScore = score;
                result = x;
            }
        }

        if (result == null)
        {
            throw new Exception(
                "5번째 겉면을 찾지 못했습니다. " +
                "메인보다 가로·세로가 각각 2 또는 4 작은 " +
                "치구 LWPOLYLINE 후보가 없습니다."
            );
        }

        return result;
    }

    private static List<EntityData> FindFifthPanelLargeCircles(
    DrawingData drawingData,
    EntityData fifthOuterPanel,
    double wallDistance = 10.0,
    double tolerance = 2.0
    )
    {
        List<EntityData> result = new();

        foreach (EntityData x in drawingData.Entities)
        {
            // 치구 레이어의 원만 검사
            if (x.LayerName != "치구")
            {
                continue;
            }

            if (x.ObjectName != "CIRCLE")
            {
                continue;
            }

            // 원의 중심이 5번째 패널 안에 있어야 함
            bool centerInside =
                x.CenterX >= fifthOuterPanel.MinX &&
                x.CenterX <= fifthOuterPanel.MaxX &&
                x.CenterY >= fifthOuterPanel.MinY &&
                x.CenterY <= fifthOuterPanel.MaxY;

            if (!centerInside)
            {
                continue;
            }

            // 패널 벽에서 원 외곽까지의 거리
            double leftGap =
                (x.CenterX - x.Radius) -
                fifthOuterPanel.MinX;

            double rightGap =
                fifthOuterPanel.MaxX -
                (x.CenterX + x.Radius);

            double bottomGap =
                (x.CenterY - x.Radius) -
                fifthOuterPanel.MinY;

            double topGap =
                fifthOuterPanel.MaxY -
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

            // 좌우 중 한 벽, 위아래 중 한 벽과 가까워야 함
            bool isCornerCircle =
                (nearLeft || nearRight) &&
                (nearTop || nearBottom);

            if (!isCornerCircle)
            {
                continue;
            }

            result.Add(x);
        }

        // 위쪽부터, 같은 높이에서는 왼쪽부터
        result = result
            .OrderByDescending(x => x.CenterY)
            .ThenBy(x => x.CenterX)
            .ToList();

        return result;
    }

    private static List<BoltHolePair> FindFifthPanelBoltHoles(
    DrawingData drawingData,
    EntityData fifthOuterPanel,
    double centerTolerance = 0.001
    )
    {
        List<EntityData> circles = new();

        // 1. 5번째 패널 내부의 볼트 구멍 레이어 원만 수집
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

            bool insidePanel =
                x.CenterX >= fifthOuterPanel.MinX &&
                x.CenterX <= fifthOuterPanel.MaxX &&
                x.CenterY >= fifthOuterPanel.MinY &&
                x.CenterY <= fifthOuterPanel.MaxY;

            if (!insidePanel)
            {
                continue;
            }

            circles.Add(x);
        }

        List<BoltHolePair> result = new();
        HashSet<string> usedHandles = new();

        // 2. 중심이 같고 반지름이 다른 원 두 개를 한 쌍으로 묶기
        foreach (EntityData first in circles)
        {
            if (usedHandles.Contains(first.Handle))
            {
                continue;
            }

            EntityData? matchingCircle = null;

            foreach (EntityData second in circles)
            {
                if (ReferenceEquals(first, second))
                {
                    continue;
                }

                if (usedHandles.Contains(second.Handle))
                {
                    continue;
                }

                bool sameCenter =
                    Math.Abs(first.CenterX - second.CenterX)
                        <= centerTolerance &&
                    Math.Abs(first.CenterY - second.CenterY)
                        <= centerTolerance;

                if (!sameCenter)
                {
                    continue;
                }

                // 반지름까지 같으면 볼트 구멍 한 쌍이 아님
                if (Math.Abs(first.Radius - second.Radius)
                    <= 0.000001)
                {
                    continue;
                }

                matchingCircle = second;
                break;
            }

            if (matchingCircle == null)
            {
                continue;
            }

            EntityData outerCircle;
            EntityData innerCircle;

            if (first.Radius > matchingCircle.Radius)
            {
                outerCircle = first;
                innerCircle = matchingCircle;
            }
            else
            {
                outerCircle = matchingCircle;
                innerCircle = first;
            }

            result.Add(new BoltHolePair
            {
                OuterCircle = outerCircle,
                InnerCircle = innerCircle
            });

            usedHandles.Add(outerCircle.Handle);
            usedHandles.Add(innerCircle.Handle);
        }

        // 위쪽부터, 같은 높이면 왼쪽부터 정렬
        result = result
            .OrderByDescending(pair => pair.CenterY)
            .ThenBy(pair => pair.CenterX)
            .ToList();

        return result;
    }

    private static EntityData FindSixthOuterPanel(
    DrawingData drawingData,
    EntityData mainChigu,
    EntityData fifthOuterPanel,
    double sizeTolerance = 2.0
    )
    {
        EntityData? result = null;
        double bestScore = double.MaxValue;

        foreach (EntityData x in drawingData.Entities)
        {
            if (x.LayerName != "치구")
                continue;

            if (x.ObjectName != "LWPOLYLINE")
                continue;

            if (!x.IsClosed)
                continue;

            // 5번째보다 오른쪽
            if (x.CenterBoxX <= fifthOuterPanel.MaxX)
                continue;

            // 메인과 같은 크기
            if (Math.Abs(x.Width - mainChigu.Width) > sizeTolerance)
                continue;

            if (Math.Abs(x.Height - mainChigu.Height) > sizeTolerance)
                continue;

            double score =
                Math.Abs(x.CenterBoxY - mainChigu.CenterBoxY) +
                (x.MinX - fifthOuterPanel.MaxX);

            if (score < bestScore)
            {
                bestScore = score;
                result = x;
            }
        }

        if (result == null)
        {
            throw new Exception("6번째 겉면을 찾지 못했습니다.");
        }

        return result;
    }

    private static List<BoltHolePair> FindSixthPanelBoltHoles(
    DrawingData drawingData,
    EntityData sixthOuterPanel,
    double centerTolerance = 0.001
    )
    {
        List<EntityData> circles = new();

        // 1. 6번째 패널 내부의 볼트 구멍 원만 수집
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

            bool insidePanel =
                x.CenterX >= sixthOuterPanel.MinX &&
                x.CenterX <= sixthOuterPanel.MaxX &&
                x.CenterY >= sixthOuterPanel.MinY &&
                x.CenterY <= sixthOuterPanel.MaxY;

            if (!insidePanel)
            {
                continue;
            }

            circles.Add(x);
        }

        List<BoltHolePair> result = new();
        HashSet<string> usedHandles = new();

        // 2. 중심이 같고 반지름이 다른 원 두 개를 한 쌍으로 묶기
        foreach (EntityData first in circles)
        {
            if (usedHandles.Contains(first.Handle))
            {
                continue;
            }

            EntityData? matchingCircle = null;

            foreach (EntityData second in circles)
            {
                if (ReferenceEquals(first, second))
                {
                    continue;
                }

                if (usedHandles.Contains(second.Handle))
                {
                    continue;
                }

                bool sameCenter =
                    Math.Abs(first.CenterX - second.CenterX)
                        <= centerTolerance &&
                    Math.Abs(first.CenterY - second.CenterY)
                        <= centerTolerance;

                if (!sameCenter)
                {
                    continue;
                }

                // 반지름까지 같으면 제외
                if (Math.Abs(first.Radius - second.Radius)
                    <= 0.000001)
                {
                    continue;
                }

                matchingCircle = second;
                break;
            }

            if (matchingCircle == null)
            {
                continue;
            }

            EntityData outerCircle;
            EntityData innerCircle;

            if (first.Radius > matchingCircle.Radius)
            {
                outerCircle = first;
                innerCircle = matchingCircle;
            }
            else
            {
                outerCircle = matchingCircle;
                innerCircle = first;
            }

            result.Add(new BoltHolePair
            {
                OuterCircle = outerCircle,
                InnerCircle = innerCircle
            });

            usedHandles.Add(outerCircle.Handle);
            usedHandles.Add(innerCircle.Handle);
        }

        // 위쪽부터, 같은 높이면 왼쪽부터 정렬
        result = result
            .OrderByDescending(pair => pair.CenterY)
            .ThenBy(pair => pair.CenterX)
            .ToList();

        return result;
    }


    /// <summary>
    /// LWPOLYLINE을 기존 중심 기준으로 확대/축소한다.
    /// 가로 50 증가 시 왼쪽 꼭짓점은 -25, 오른쪽 꼭짓점은 +25 이동한다.
    /// </summary>
    private static void ResizePolylineFromCenter(
        EntityData data,
        double widthDelta,
        double heightDelta
    )
    {
        if (data.Entity is not LwPolyline polyline)
        {
            throw new Exception(
                $"Handle={data.Handle} 객체가 LWPOLYLINE이 아닙니다."
            );
        }

        double halfWidthDelta = widthDelta / 2.0;
        double halfHeightDelta = heightDelta / 2.0;

        double centerX = data.CenterBoxX;
        double centerY = data.CenterBoxY;

        const double tolerance = 0.000001;

        foreach (var vertex in polyline.Vertices)
        {
            double x = vertex.Location.X;
            double y = vertex.Location.Y;

            if (x < centerX - tolerance)
            {
                x -= halfWidthDelta;
            }
            else if (x > centerX + tolerance)
            {
                x += halfWidthDelta;
            }

            if (y < centerY - tolerance)
            {
                y -= halfHeightDelta;
            }
            else if (y > centerY + tolerance)
            {
                y += halfHeightDelta;
            }

            vertex.Location = new XY(
                x,
                y
            );
        }
    }

    /// <summary>
    /// 패널 모서리의 치구 레이어 큰 원은 크기를 유지하고
    /// 패널 확대량의 절반만큼 바깥쪽으로 이동한다.
    /// 볼트 구멍 레이어 원은 이 함수에 전달하지 않는다.
    /// </summary>
    private static void MoveCornerCircles(
        List<EntityData> circles,
        EntityData originalPanel,
        double widthDelta,
        double heightDelta
    )
    {
        double halfWidthDelta = widthDelta / 2.0;
        double halfHeightDelta = heightDelta / 2.0;

        foreach (EntityData circleData in circles)
        {
            if (circleData.Entity is not Circle circle)
            {
                continue;
            }

            double x = circle.Center.X;
            double y = circle.Center.Y;

            if (circleData.CenterX < originalPanel.CenterBoxX)
            {
                x -= halfWidthDelta;
            }
            else if (circleData.CenterX > originalPanel.CenterBoxX)
            {
                x += halfWidthDelta;
            }

            if (circleData.CenterY < originalPanel.CenterBoxY)
            {
                y -= halfHeightDelta;
            }
            else if (circleData.CenterY > originalPanel.CenterBoxY)
            {
                y += halfHeightDelta;
            }

            circle.Center = new XYZ(
                x,
                y,
                circle.Center.Z
            );
        }
    }

    /// <summary>
    /// 위/아래 긴 판 안의 작은 블록을 두께 방향으로 확대하고,
    /// 메인 치구 가로 변화량의 절반만큼 좌우로 이동한다.
    /// </summary>
    private static void ResizePlateBlocks(
        List<EntityData> blocks,
        double mainCenterX,
        double widthDelta,
        double depthDelta
    )
    {
        double halfWidthDelta = widthDelta / 2.0;

        foreach (EntityData block in blocks)
        {
            ResizePolylineFromCenter(
                block,
                0.0,
                depthDelta
            );

            if (block.Entity is not LwPolyline polyline)
            {
                continue;
            }

            double moveX = 0.0;

            if (block.CenterBoxX < mainCenterX)
            {
                moveX = -halfWidthDelta;
            }
            else if (block.CenterBoxX > mainCenterX)
            {
                moveX = halfWidthDelta;
            }

            foreach (var vertex in polyline.Vertices)
            {
                vertex.Location = new XY(
                    vertex.Location.X + moveX,
                    vertex.Location.Y
                );
            }
        }
    }

    /// <summary>
    /// 볼트 구멍 쌍 목록을 실제 원 객체 목록으로 펼친다.
    /// </summary>
    private static IEnumerable<EntityData> GetBoltHoleEntities(
        IEnumerable<BoltHolePair> boltHoles
    )
    {
        foreach (BoltHolePair pair in boltHoles)
        {
            yield return pair.OuterCircle;
            yield return pair.InnerCircle;
        }
    }

    /// <summary>
    /// 패널의 수정 전 외곽 범위 안에 중심이 있는 모든 객체를 하나의 묶음으로 만든다.
    /// 외곽, 볼트 구멍, 원, 내부 박스, TEXT 등이 함께 포함된다.
    /// </summary>
    private static List<EntityData> GetEntitiesInsidePanel(
        DrawingData drawingData,
        EntityData panel,
        double tolerance = 0.001
    )
    {
        List<EntityData> result = new();

        foreach (EntityData entity in drawingData.Entities)
        {
            if (ReferenceEquals(entity, panel))
            {
                result.Add(entity);
                continue;
            }

            double centerX = entity.CenterBoxX;
            double centerY = entity.CenterBoxY;

            // 원은 CenterBox 값 대신 실제 원 중심값이 더 확실하다.
            if (entity.ObjectName == "CIRCLE")
            {
                centerX = entity.CenterX;
                centerY = entity.CenterY;
            }
            else if (entity.ObjectName == "TEXT" ||
                     entity.ObjectName == "MTEXT")
            {
                centerX = entity.InsertX;
                centerY = entity.InsertY;
            }

            bool inside =
                centerX >= panel.MinX - tolerance &&
                centerX <= panel.MaxX + tolerance &&
                centerY >= panel.MinY - tolerance &&
                centerY <= panel.MaxY + tolerance;

            if (inside)
            {
                result.Add(entity);
            }
        }

        return result
            .GroupBy(x => x.Handle)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>
    /// 객체 하나를 지정한 거리만큼 이동한다.
    /// 크기나 반지름은 바꾸지 않는다.
    /// </summary>
    private static void MoveEntity(
        EntityData data,
        double moveX,
        double moveY
    )
    {
        switch (data.Entity)
        {
            case LwPolyline polyline:
                foreach (var vertex in polyline.Vertices)
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

    /// <summary>
    /// 패널에 포함된 모든 객체를 같은 거리만큼 이동한다.
    /// Handle이 중복된 객체는 한 번만 이동한다.
    /// </summary>
    private static void MoveEntityGroup(
        IEnumerable<EntityData> entities,
        double moveX,
        double moveY
    )
    {
        foreach (EntityData entity in entities
            .GroupBy(x => x.Handle)
            .Select(group => group.First()))
        {
            MoveEntity(entity, moveX, moveY);
        }
    }

    /// <summary>
    /// 현재 수정된 실제 CAD 객체에서 묶음 전체의 바운딩 박스를 다시 계산한다.
    /// EntityData의 MinX/MaxX는 수정 전 값이므로 사용하지 않는다.
    /// </summary>
    private static (
        double MinX,
        double MinY,
        double MaxX,
        double MaxY
    ) GetGroupBounds(
        IEnumerable<EntityData> entities
    )
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        bool found = false;

        foreach (EntityData entity in entities
            .GroupBy(x => x.Handle)
            .Select(group => group.First()))
        {
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
                // 바운딩 박스를 지원하지 않는 객체는 간격 계산에서 제외한다.
            }
        }

        if (!found)
        {
            throw new Exception(
                "패널 묶음의 현재 범위를 계산하지 못했습니다."
            );
        }

        return (minX, minY, maxX, maxY);
    }

    /// <summary>
    /// 첫 번째 패널 묶음은 고정하고 나머지 패널 묶음을 오른쪽으로 재배치한다.
    /// 이전 묶음의 오른쪽 끝과 다음 묶음의 왼쪽 끝 사이가 정확히 gap이 된다.
    /// </summary>
    private static void ArrangePanelGroupsWithGap(
        List<List<EntityData>> panelGroups,
        double gap
    )
    {
        if (panelGroups.Count < 2)
        {
            return;
        }

        var previousBounds = GetGroupBounds(panelGroups[0]);

        for (int i = 1; i < panelGroups.Count; i++)
        {
            var currentBounds = GetGroupBounds(panelGroups[i]);

            double targetMinX = previousBounds.MaxX + gap;
            double moveX = targetMinX - currentBounds.MinX;

            MoveEntityGroup(
                panelGroups[i],
                moveX,
                0.0
            );

            previousBounds = GetGroupBounds(panelGroups[i]);
        }
    }

    /// <summary>
    /// 수정된 CadDocument를 새 DWG 파일로 저장한다.
    /// </summary>
    private static void SaveAsNewDwg(
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

    private static ResizeInput? ShowResizeInputDialog(
        EntityData mainChigu,
        double currentDepth
    )
    {
        double baseWidth = mainChigu.Width + 0.7;
        double baseHeight = mainChigu.Height + 0.7;
        double baseDepth = currentDepth + 15.0;

        using Form form = new()
        {
            Text = "수정할 크기 입력",
            Width = 390,
            Height = 310,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        Label currentLabel = new()
        {
            Left = 25,
            Top = 20,
            Width = 330,
            Height = 35,
            Text =
                $"현재 크기: " +
                $"{baseWidth:0.###} x " +
                $"{baseHeight:0.###} x " +
                $"{baseDepth:0.###}"
        };

        Label widthLabel = new()
        {
            Left = 25,
            Top = 75,
            Width = 100,
            Text = "목표 가로"
        };

        NumericUpDown widthInput = CreateSizeInput(
            140,
            70,
            baseWidth
        );

        Label heightLabel = new()
        {
            Left = 25,
            Top = 115,
            Width = 100,
            Text = "목표 세로"
        };

        NumericUpDown heightInput = CreateSizeInput(
            140,
            110,
            baseHeight
        );

        Label depthLabel = new()
        {
            Left = 25,
            Top = 155,
            Width = 100,
            Text = "목표 두께"
        };

        NumericUpDown depthInput = CreateSizeInput(
            140,
            150,
            baseDepth
        );

        Button okButton = new()
        {
            Left = 140,
            Top = 215,
            Width = 90,
            Height = 32,
            Text = "확인",
            DialogResult = DialogResult.OK
        };

        Button cancelButton = new()
        {
            Left = 240,
            Top = 215,
            Width = 90,
            Height = 32,
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
            depthLabel,
            depthInput,
            okButton,
            cancelButton
        ]);

        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        if (form.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        double targetWidth =
            (double)widthInput.Value;

        double targetHeight =
            (double)heightInput.Value;

        double targetDepth =
            (double)depthInput.Value;

        return new ResizeInput
        {
            TargetWidth = targetWidth,
            TargetHeight = targetHeight,
            TargetDepth = targetDepth,

            WidthDelta = targetWidth - baseWidth,
            HeightDelta = targetHeight - baseHeight,
            DepthDelta = targetDepth - baseDepth,
        };
    }

    private static NumericUpDown CreateSizeInput(
    int left,
    int top,
    double currentValue
    )
    {
        decimal safeValue =
            (decimal)Math.Clamp(
                currentValue,
                0.001,
                1000000.0
            );

        return new NumericUpDown
        {
            Left = left,
            Top = top,
            Width = 190,
            DecimalPlaces = 3,
            Minimum = 0.001m,
            Maximum = 1000000m,
            Increment = 1m,
            Value = safeValue
        };
    }

    private static void SaveEntityReport(
    DrawingData drawingData,
    string outputPath
    )
    {
        using StreamWriter writer =
            new StreamWriter(outputPath, false);

        writer.WriteLine($"버전: {drawingData.Version}");
        writer.WriteLine($"레이어 수: {drawingData.LayerCount}");
        writer.WriteLine($"블록 수: {drawingData.BlockCount}");
        writer.WriteLine(
            $"Model Space 객체 수: {drawingData.Entities.Count}"
        );
        writer.WriteLine();

        for (int index = 0;
            index < drawingData.Entities.Count;
            index++)
        {
            EntityData x =
                drawingData.Entities[index];

            writer.Write(
                $"[{index}] 종류={x.ObjectName}, " +
                $"Handle={x.Handle}, " +
                $"Layer={x.LayerName}"
            );

            switch (x.ObjectName)
            {
                case "LINE":
                    writer.Write(
                        $", Start=({x.StartX:F6}, " +
                        $"{x.StartY:F6}, {x.StartZ:F6})"
                    );

                    writer.Write(
                        $", End=({x.EndX:F6}, " +
                        $"{x.EndY:F6}, {x.EndZ:F6})"
                    );
                    break;

                case "ARC":
                    writer.Write(
                        $", Center=({x.CenterX:F6}, " +
                        $"{x.CenterY:F6}, {x.CenterZ:F6})"
                    );

                    writer.Write(
                        $", Radius={x.Radius:F6}, " +
                        $"StartAngle={x.StartAngle:F6}, " +
                        $"EndAngle={x.EndAngle:F6}"
                    );
                    break;

                case "CIRCLE":
                    writer.Write(
                        $", Center=({x.CenterX:F6}, " +
                        $"{x.CenterY:F6}, {x.CenterZ:F6})"
                    );

                    writer.Write(
                        $", Radius={x.Radius:F6}, " +
                        $"Diameter={x.Diameter:F6}"
                    );
                    break;

                case "LWPOLYLINE":
                    writer.Write(
                        $", Closed={x.IsClosed}, Vertices="
                    );

                    for (int i = 0;
                        i < x.Vertices.Count;
                        i++)
                    {
                        PointData point =
                            x.Vertices[i];

                        writer.Write(
                            $"({point.X:F6}, {point.Y:F6})"
                        );

                        if (i < x.Vertices.Count - 1)
                        {
                            writer.Write(" | ");
                        }
                    }
                    break;

                case "POLYLINE2D":
                    writer.Write(
                        $", Closed={x.IsClosed}, Vertices="
                    );

                    for (int i = 0;
                        i < x.Vertices.Count;
                        i++)
                    {
                        PointData point =
                            x.Vertices[i];

                        writer.Write(
                            $"({point.X:F6}, " +
                            $"{point.Y:F6}, " +
                            $"{point.Z:F6})"
                        );

                        if (i < x.Vertices.Count - 1)
                        {
                            writer.Write(" | ");
                        }
                    }
                    break;

                case "TEXT":
                case "MTEXT":
                    writer.Write(
                        $", Insert=({x.InsertX:F6}, " +
                        $"{x.InsertY:F6}, " +
                        $"{x.InsertZ:F6}), " +
                        $"Text=\"{x.TextValue}\""
                    );
                    break;

                case "INSERT":
                    writer.Write(
                        $", Block={x.BlockName}, " +
                        $"Insert=({x.InsertX:F6}, " +
                        $"{x.InsertY:F6}, " +
                        $"{x.InsertZ:F6})"
                    );
                    break;

                case "DIMENSION":
                    writer.Write(
                        $", Text=\"{x.TextValue}\", " +
                        $"TextPosition=(" +
                        $"{x.TextPositionX:F6}, " +
                        $"{x.TextPositionY:F6}, " +
                        $"{x.TextPositionZ:F6})"
                    );
                    break;
            }

            writer.WriteLine();
        }
    }

    private static List<BoltHolePair> FindBoltHolesInsidePanel(
        DrawingData drawingData,
        EntityData panel,
        double centerTolerance = 0.001,
        double radiusTolerance = 0.000001
        )
        {
            List<EntityData> circles = new();

            // 패널 안의 볼트 구멍 레이어 원을 모두 수집
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

                bool insidePanel =
                    x.CenterX >= panel.MinX &&
                    x.CenterX <= panel.MaxX &&
                    x.CenterY >= panel.MinY &&
                    x.CenterY <= panel.MaxY;

                if (!insidePanel)
                {
                    continue;
                }

                circles.Add(x);
            }

            List<BoltHolePair> result = new();
            HashSet<string> usedHandles = new();

            // 동일한 중심을 가진 반지름이 다른 원 두 개를 한 쌍으로 묶음
            foreach (EntityData first in circles)
            {
                if (usedHandles.Contains(first.Handle))
                {
                    continue;
                }

                EntityData? matchingCircle = null;

                foreach (EntityData second in circles)
                {
                    if (ReferenceEquals(first, second))
                    {
                        continue;
                    }

                    if (usedHandles.Contains(second.Handle))
                    {
                        continue;
                    }

                    bool sameCenter =
                        Math.Abs(first.CenterX - second.CenterX)
                            <= centerTolerance &&
                        Math.Abs(first.CenterY - second.CenterY)
                            <= centerTolerance;

                    if (!sameCenter)
                    {
                        continue;
                    }

                    bool differentRadius =
                        Math.Abs(first.Radius - second.Radius)
                            > radiusTolerance;

                    if (!differentRadius)
                    {
                        continue;
                    }

                    matchingCircle = second;
                    break;
                }

                // 같은 중심의 다른 크기 원이 없으면 건너뜀
                if (matchingCircle == null)
                {
                    continue;
                }

                EntityData outerCircle;
                EntityData innerCircle;

                if (first.Radius > matchingCircle.Radius)
                {
                    outerCircle = first;
                    innerCircle = matchingCircle;
                }
                else
                {
                    outerCircle = matchingCircle;
                    innerCircle = first;
                }

                result.Add(
                    new BoltHolePair
                    {
                        OuterCircle = outerCircle,
                        InnerCircle = innerCircle
                    }
                );

                usedHandles.Add(outerCircle.Handle);
                usedHandles.Add(innerCircle.Handle);
            }

            // 위에서 아래, 같은 높이면 왼쪽에서 오른쪽
            return result
                .OrderByDescending(pair => pair.CenterY)
                .ThenBy(pair => pair.CenterX)
                .ToList();
        }

        private static void RemoveEntity(
        CadDocument document,
        EntityData entityData
    )
    {
        Entity entity =
            entityData.Entity;

        // 대부분 Model Space에 있으므로 여기서 삭제
        if (document.Entities.Contains(entity))
        {
            document.Entities.Remove(entity);
            return;
        }

        // 혹시 BlockRecord 안에 들어 있으면 그쪽에서도 탐색
        foreach (var blockRecord in document.BlockRecords)
        {
            if (blockRecord.Entities.Contains(entity))
            {
                blockRecord.Entities.Remove(entity);
                return;
            }
        }
    }

    private static List<BoltHolePair> SelectCenterBoltHolesByWidth(
        CadDocument document,
        List<BoltHolePair> boltHoles,
        EntityData mainChigu,
        double targetWidth
    )
    {
        // 정확히 4쌍인 경우에만 선택 삭제
        if (boltHoles.Count != 4)
        {
            return boltHoles;
        }

        double panelCenterX =
            mainChigu.CenterBoxX;

        // 패널 중심에서 가까운 순서로 정렬
        List<BoltHolePair> sortedByCenterDistance =
            boltHoles
                .OrderBy(pair =>
                    Math.Abs(pair.CenterX - panelCenterX)
                )
                .ToList();

        List<BoltHolePair> keepPairs;

        if (targetWidth <= 120.0)
        {
            // 가로 120 이하:
            // 중심에 가까운 안쪽 볼트 구멍 2쌍 유지
            keepPairs = sortedByCenterDistance
                .Take(2)
                .ToList();
        }
        else
        {
            // 가로 120 초과:
            // 중심에서 먼 바깥쪽 볼트 구멍 2쌍 유지
            keepPairs = sortedByCenterDistance
                .TakeLast(2)
                .ToList();
        }

        HashSet<string> keepHandles = new();

        foreach (BoltHolePair pair in keepPairs)
        {
            keepHandles.Add(
                pair.OuterCircle.Handle
            );

            keepHandles.Add(
                pair.InnerCircle.Handle
            );
        }

        // 유지 대상이 아닌 볼트 구멍 쌍 삭제
        foreach (BoltHolePair pair in boltHoles)
        {
            bool keepPair =
                keepHandles.Contains(
                    pair.OuterCircle.Handle
                ) &&
                keepHandles.Contains(
                    pair.InnerCircle.Handle
                );

            if (keepPair)
            {
                continue;
            }

            RemoveEntity(
                document,
                pair.OuterCircle
            );

            RemoveEntity(
                document,
                pair.InnerCircle
            );
        }

        // 이후 그룹 구성에도 남은 두 쌍만 사용하도록 반환
        return keepPairs
            .OrderBy(pair => pair.CenterX)
            .ToList();
    }

    private static void ResizeAndMoveCornerCircles(
        IEnumerable<EntityData> circles,
        EntityData originalPanel,
        double originalWidth,
        double originalHeight,
        double targetWidth,
        double targetHeight
    )
    {
        if (originalWidth <= 0.0 || originalHeight <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                "기존 패널 크기는 0보다 커야 합니다."
            );
        }

        double widthDelta =
            targetWidth - originalWidth;

        double heightDelta =
            targetHeight - originalHeight;

        double halfWidthDelta =
            widthDelta / 2.0;

        double halfHeightDelta =
            heightDelta / 2.0;

        double widthScale =
            targetWidth / originalWidth;

        double heightScale =
            targetHeight / originalHeight;

        double averageScale =
            (widthScale + heightScale) / 2.0;

        foreach (EntityData circleData in circles)
        {
            if (circleData.Entity is not Circle circle)
            {
                continue;
            }

            double oldRadius = circle.Radius;

            // ★ 버림(소수점 제거)
            double newRadius =
                Math.Floor(oldRadius * averageScale / 5.0) * 5.0;

            double radiusDifference =
                newRadius - oldRadius;

            bool isLeft =
                circleData.CenterX <
                originalPanel.CenterBoxX;

            bool isRight =
                circleData.CenterX >
                originalPanel.CenterBoxX;

            bool isTop =
                circleData.CenterY >
                originalPanel.CenterBoxY;

            bool isBottom =
                circleData.CenterY <
                originalPanel.CenterBoxY;

            double moveX = 0.0;
            double moveY = 0.0;

            // 패널 크기 변화에 따른 이동
            if (isLeft)
            {
                moveX -= halfWidthDelta;
            }
            else if (isRight)
            {
                moveX += halfWidthDelta;
            }

            if (isTop)
            {
                moveY += halfHeightDelta;
            }
            else if (isBottom)
            {
                moveY -= halfHeightDelta;
            }

            // 반지름 증가/감소에 따른 안쪽 보정
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
        }
    }
}