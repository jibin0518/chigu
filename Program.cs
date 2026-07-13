using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ACadSharp;
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

            Console.WriteLine($"선택 파일: {dwgPath}");
            Console.WriteLine("DWG 읽는 중...");

            CadDocument document = DwgReader.Read(dwgPath);

            Console.WriteLine("DWG 읽기 완료.");

            PartDetector.DetectedParts parts =
                PartDetector.Analyze(document);

            PartDetector.PrintAnalysis(parts);

            string directory = Path.GetDirectoryName(dwgPath)!;
            string fileName = Path.GetFileNameWithoutExtension(dwgPath);

            string analysisPath = Path.Combine(
                directory,
                $"{fileName}_분류결과.txt"
            );

            PartDetector.SaveAnalysisReport(
                parts,
                analysisPath
            );

            DrawingModifier.TargetSize? target =
                ShowTargetSizeDialog(parts);

            if (target == null)
            {
                Console.WriteLine("크기 입력이 취소되었습니다.");
                Console.WriteLine($"분류 결과는 저장되었습니다: {analysisPath}");
                Console.ReadKey();
                return;
            }

            DrawingModifier.Apply(
                parts,
                target
            );

            string outputPath = Path.Combine(
                directory,
                $"{fileName}_{target}.dwg"
            );

            DrawingModifier.SaveAsNewDwg(
                document,
                outputPath
            );

            Console.WriteLine();
            Console.WriteLine("완료");
            Console.WriteLine($"분류 결과: {analysisPath}");
            Console.WriteLine($"수정 DWG: {outputPath}");
            Console.WriteLine();
            Console.WriteLine("아무 키나 누르면 종료됩니다.");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("오류 발생:");
            Console.WriteLine(ex);
            Console.WriteLine();
            Console.WriteLine("아무 키나 누르면 종료됩니다.");
            Console.ReadKey();
        }
    }

    private static string? SelectDwgFile()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "수정할 DWG 파일 선택",
            Filter = "AutoCAD DWG 파일 (*.dwg)|*.dwg|모든 파일 (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private static DrawingModifier.TargetSize? ShowTargetSizeDialog(
        PartDetector.DetectedParts parts
    )
    {
        using Form form = new()
        {
            Text = "수정할 크기 입력",
            Width = 390,
            Height = 300,
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
            Height = 30,
            Text =
                $"현재 감지 크기: " +
                $"{parts.CurrentWidth:0.###} x " +
                $"{parts.CurrentHeight:0.###} x " +
                $"{parts.CurrentDepth:0.###}"
        };

        Label widthLabel = new()
        {
            Left = 25,
            Top = 70,
            Width = 100,
            Text = "가로"
        };

        NumericUpDown widthInput = CreateNumberInput(
            140,
            65,
            parts.CurrentWidth
        );

        Label heightLabel = new()
        {
            Left = 25,
            Top = 110,
            Width = 100,
            Text = "세로"
        };

        NumericUpDown heightInput = CreateNumberInput(
            140,
            105,
            parts.CurrentHeight
        );

        Label depthLabel = new()
        {
            Left = 25,
            Top = 150,
            Width = 100,
            Text = "두께"
        };

        NumericUpDown depthInput = CreateNumberInput(
            140,
            145,
            parts.CurrentDepth
        );

        Button okButton = new()
        {
            Left = 140,
            Top = 205,
            Width = 90,
            Height = 32,
            Text = "수정",
            DialogResult = DialogResult.OK
        };

        Button cancelButton = new()
        {
            Left = 240,
            Top = 205,
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

        return new DrawingModifier.TargetSize
        {
            Width = (double)widthInput.Value,
            Height = (double)heightInput.Value,
            Depth = (double)depthInput.Value
        };
    }

    private static NumericUpDown CreateNumberInput(
        int left,
        int top,
        double value
    )
    {
        decimal safeValue = (decimal)Math.Clamp(
            value,
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
}
