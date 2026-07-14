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

            /*
             * 이후부터는 drawingData를 사용하면 된다.
             *
             * 예:
             * drawingData.Version
             * drawingData.LayerCount
             * drawingData.BlockCount
             * drawingData.Entities
             *
             * 콘솔 출력과 TXT 저장은 하지 않는다.
             */
            EntityData? Center_Compression_Chigu = null;
            int maxVertexCount = -1;

             foreach (EntityData x in drawingData.Entities)
            {
                if (x.LayerName == "치구")
                {
                    if (x.ObjectName == "LWPOLYLINE")
                    {
                        if (maxVertexCount<x.Vertices.Count)
                        {
                            maxVertexCount = x.Vertices.Count;
                            Center_Compression_Chigu = x;
                        }
                    }
                }
            }
            Console.WriteLine(Center_Compression_Chigu);
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
}