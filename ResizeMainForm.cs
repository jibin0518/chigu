using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace DwgAutoResize;

/// <summary>
/// DWG 일괄 크기 수정용 UI 화면과 사용자 입력 흐름을 담당한다.
/// 실제 DWG 분석/변환은 Program_V2가 전달하는 함수로 실행한다.
/// </summary>
internal sealed class ResizeMainForm : Form
{
    private static readonly Color WindowBackground = Color.FromArgb(245, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(205, 211, 220);
    private static readonly Color PrimaryColor = Color.FromArgb(42, 103, 209);
    private static readonly Color SecondaryTextColor = Color.FromArgb(92, 101, 116);
    private string _manualOutputPath = string.Empty;

    private static string UiStateSettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DwgAutoResize",
        "ui-state.json"
    );

    public TextBox InputPathTextBox { get; }
    public Button InputPathBrowseButton { get; }
    public TextBox InputFileSearchTextBox { get; }
    public Button RefreshInputFilesButton { get; }
    public TextBox OutputPathTextBox { get; }
    public Button OutputPathBrowseButton { get; }
    public CheckBox AutoOutputPathCheckBox { get; }

    public TextBox CurrentWidthTextBox { get; }
    public TextBox CurrentHeightTextBox { get; }
    public TextBox CurrentThicknessTextBox { get; }

    public TextBox TargetWidthTextBox { get; }
    public TextBox TargetHeightTextBox { get; }
    public TextBox TargetThicknessTextBox { get; }

    public TextBox ConversionWidthTextBox { get; }
    public TextBox ConversionHeightTextBox { get; }
    public TextBox ConversionThicknessTextBox { get; }

    public DataGridView InputFilesGrid { get; }
    public DataGridView TargetFilesGrid { get; }
    public Button AddFolderButton { get; }
    public Button AddFilesButton { get; }
    public Button MoveSelectedButton { get; }
    public Button SaveCurrentStateButton { get; }
    public Label ConvertLabel { get; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, DwgFileSizeSnapshot>? AnalyzeDwgFile { get; init; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<DwgConversionRequest, string>? ConvertDwgFile { get; init; }

#if UI_PREVIEW
    /// <summary>
    /// UI 화면만 단독으로 확인할 때 사용하는 미리보기 진입점이다.
    /// UI_PREVIEW가 정의된 전용 프로젝트에서만 컴파일되므로
    /// 기존 DWG 프로그램의 Main 메서드와 충돌하지 않는다.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ResizeMainForm());
    }
#endif

    public ResizeMainForm()
    {
        Text = "DWG 자동 크기 수정";
        System.Drawing.Icon? executableIcon =
            System.Drawing.Icon.ExtractAssociatedIcon(
                Application.ExecutablePath
            );

        if (executableIcon != null)
        {
            Icon = executableIcon;
        }

        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        ClientSize = new Size(1280, 760);
        BackColor = WindowBackground;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            BackColor = WindowBackground,
            Padding = new Padding(22),
            ColumnCount = 3,
            RowCount = 1
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Panel leftCard = CreateCard();
        Panel rightCard = CreateCard();

        TableLayoutPanel leftLayout = CreateSectionLayout(86F);
        TableLayoutPanel rightLayout = CreateSectionLayout(86F);

        Label inputTitle = CreateSectionTitle("입력 파일");
        Label outputTitle = CreateSectionTitle("수정 파일");

        Panel inputPathPanel = CreatePathPanel(
            "파일 목록 경로",
            out TextBox inputPath,
            out Button inputBrowse
        );
        InputPathTextBox = inputPath;
        InputPathBrowseButton = inputBrowse;

        InputFileSearchTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "파일명 검색",
            // 제목 글자보다 아래로 처져 보이지 않도록 위 여백을 없애고,
            // 아래 테두리가 다음 행에 붙지 않도록 아래 여백을 확보한다.
            Margin = new Padding(8, 0, 8, 7)
        };

        RefreshInputFilesButton = new Button
        {
            Text = "새로고침",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(238, 242, 247),
            ForeColor = Color.FromArgb(31, 73, 135),
            Font = new Font(
                "Segoe UI",
                9F,
                FontStyle.Bold,
                GraphicsUnit.Point
            ),
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 0, 0, 7)
        };
        RefreshInputFilesButton.FlatAppearance.BorderColor = BorderColor;

        Panel outputPathPanel = CreateOutputPathPanel(
            out TextBox outputPath,
            out Button outputBrowse,
            out CheckBox autoOutputPath
        );
        OutputPathTextBox = outputPath;
        OutputPathBrowseButton = outputBrowse;
        AutoOutputPathCheckBox = autoOutputPath;

        Panel currentSizePanel = CreateSizePanel(
            "현재 사이즈",
            true,
            out TextBox currentWidth,
            out TextBox currentHeight,
            out TextBox currentThickness
        );
        CurrentWidthTextBox = currentWidth;
        CurrentHeightTextBox = currentHeight;
        CurrentThicknessTextBox = currentThickness;

        Panel targetSizePanel = CreateSizePanel(
            "목표 사이즈",
            true,
            out TextBox targetWidth,
            out TextBox targetHeight,
            out TextBox targetThickness
        );
        TargetWidthTextBox = targetWidth;
        TargetHeightTextBox = targetHeight;
        TargetThicknessTextBox = targetThickness;

        InputFilesGrid = CreateFileGrid(
            "현재 가로",
            "현재 세로",
            "현재 두께",
            false
        );

        TargetFilesGrid = CreateFileGrid(
            "목표 가로",
            "목표 세로",
            "목표 두께",
            false
        );
        TargetFilesGrid.Columns.Add(
            CreateGridColumn(
                "ModifiedTime",
                "수정 시간",
                125F,
                true
            )
        );

        // 결과 목록은 숫자/시간 열을 필요한 크기로 고정하고,
        // 남은 공간을 파일명 열이 전부 사용하게 한다.
        // FillWeight만 조정하면 전체 열이 다시 압축되어 긴 파일명이
        // 계속 잘리므로 오른쪽 목록에만 고정 폭을 적용한다.
        ConfigureTargetFileGridColumns(TargetFilesGrid);

        AddFolderButton = CreatePrimaryButton("목록 DWG 폴더 추가");
        AddFilesButton = CreatePrimaryButton("＋ DWG 추가");

        MoveSelectedButton = new Button
        {
            Text = "→",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = PrimaryColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Point),
            Cursor = Cursors.Hand,
            Margin = new Padding(73, 4, 73, 4),
            TabStop = true
        };
        MoveSelectedButton.FlatAppearance.BorderSize = 0;

        ConvertLabel = new Label
        {
            Text = "변환",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 41, 55)
        };

        Panel convertPanel = CreateConvertPanel(
            MoveSelectedButton,
            ConvertLabel,
            out TextBox conversionWidth,
            out TextBox conversionHeight,
            out TextBox conversionThickness,
            out Button saveCurrentState
        );
        ConversionWidthTextBox = conversionWidth;
        ConversionHeightTextBox = conversionHeight;
        ConversionThicknessTextBox = conversionThickness;
        SaveCurrentStateButton = saveCurrentState;

        Panel inputGridPanel = CreateGridPanel(
            "파일 목록",
            "추가된 DWG 파일과 읽어 온 현재 치수를 표시합니다.",
            InputFilesGrid,
            AddFolderButton,
            AddFilesButton,
            InputFileSearchTextBox,
            RefreshInputFilesButton
        );

        Panel targetGridPanel = CreateGridPanel(
            "수정된 파일 목록",
            "변환에 성공해 생성된 DWG 파일과 적용된 목표 사이즈입니다.",
            TargetFilesGrid,
            null,
            null,
            null,
            null
        );

        leftLayout.Controls.Add(inputTitle, 0, 0);
        leftLayout.Controls.Add(inputPathPanel, 0, 1);
        leftLayout.Controls.Add(currentSizePanel, 0, 2);
        leftLayout.Controls.Add(inputGridPanel, 0, 3);

        rightLayout.Controls.Add(outputTitle, 0, 0);
        rightLayout.Controls.Add(outputPathPanel, 0, 1);
        rightLayout.Controls.Add(targetSizePanel, 0, 2);
        rightLayout.Controls.Add(targetGridPanel, 0, 3);

        leftCard.Controls.Add(leftLayout);
        rightCard.Controls.Add(rightLayout);

        root.Controls.Add(leftCard, 0, 0);
        root.Controls.Add(convertPanel, 1, 0);
        root.Controls.Add(rightCard, 2, 0);

        Controls.Add(root);

        InputPathBrowseButton.Click += InputPathBrowseButton_Click;
        InputFileSearchTextBox.TextChanged +=
            InputFileSearchTextBox_TextChanged;
        RefreshInputFilesButton.Click += RefreshInputFilesButton_Click;
        OutputPathBrowseButton.Click += OutputPathBrowseButton_Click;
        AutoOutputPathCheckBox.CheckedChanged +=
            AutoOutputPathCheckBox_CheckedChanged;
        AddFolderButton.Click += AddFolderButton_Click;
        AddFilesButton.Click += AddFilesButton_Click;
        MoveSelectedButton.Click += MoveSelectedButton_Click;
        SaveCurrentStateButton.Click += SaveCurrentStateButton_Click;
        InputFilesGrid.SelectionChanged += InputFilesGrid_SelectionChanged;
        TargetFilesGrid.SelectionChanged += TargetFilesGrid_SelectionChanged;
        TargetFilesGrid.CellMouseDoubleClick +=
            TargetFilesGrid_CellMouseDoubleClick;
        InputFilesGrid.CellClick += InputFilesGrid_CellClick;
        InputFilesGrid.KeyDown += FileGrid_KeyDown;
        TargetFilesGrid.KeyDown += FileGrid_KeyDown;

        RegisterEnterAsTab(
            InputPathTextBox,
            InputFileSearchTextBox,
            OutputPathTextBox,
            ConversionWidthTextBox,
            ConversionHeightTextBox,
            ConversionThicknessTextBox
        );

        AutoOutputPathCheckBox.Checked = false;
        UpdateOutputPathMode();
        SetConversionThicknessInputEnabled(false);
        LoadSavedUiState();
    }

    /// <summary>
    /// 한 줄 입력칸에서 Enter를 누르면 Tab을 누른 것처럼
    /// 다음 컨트롤로 입력 포커스를 이동한다.
    /// </summary>
    private void RegisterEnterAsTab(params TextBox[] textBoxes)
    {
        foreach (TextBox textBox in textBoxes)
        {
            textBox.KeyDown += InputTextBox_KeyDown;
        }
    }

    private void InputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        if (sender is Control currentControl)
        {
            SelectNextControl(
                currentControl,
                forward: true,
                tabStopOnly: true,
                nested: true,
                wrap: true
            );
        }
    }

    private static Panel CreateConvertPanel(
        Button convertButton,
        Label convertLabel,
        out TextBox widthTextBox,
        out TextBox heightTextBox,
        out TextBox thicknessTextBox,
        out Button saveCurrentStateButton
    )
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = WindowBackground,
            Margin = new Padding(0)
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = WindowBackground
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        Panel sizeInputPanel = CreateConversionSizeInputPanel(
            out widthTextBox,
            out heightTextBox,
            out thicknessTextBox
        );

        saveCurrentStateButton = CreatePrimaryButton("현재값 저장");
        saveCurrentStateButton.Dock = DockStyle.Fill;
        saveCurrentStateButton.Margin = new Padding(14, 8, 14, 8);

        layout.Controls.Add(saveCurrentStateButton, 0, 1);
        layout.Controls.Add(sizeInputPanel, 0, 2);
        layout.Controls.Add(convertButton, 0, 3);
        layout.Controls.Add(convertLabel, 0, 4);
        panel.Controls.Add(layout);
        return panel;
    }

    private static Panel CreateConversionSizeInputPanel(
        out TextBox widthTextBox,
        out TextBox heightTextBox,
        out TextBox thicknessTextBox
    )
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(6, 4, 6, 4)
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));

        Label title = new()
        {
            Text = "목표값 입력",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point)
        };

        widthTextBox = CreateSizeTextBox(false);
        heightTextBox = CreateSizeTextBox(false);
        thicknessTextBox = CreateSizeTextBox(false);

        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);
        layout.Controls.Add(CreateValueLabel("가로"), 0, 1);
        layout.Controls.Add(widthTextBox, 1, 1);
        layout.Controls.Add(CreateValueLabel("세로"), 0, 2);
        layout.Controls.Add(heightTextBox, 1, 2);
        layout.Controls.Add(CreateValueLabel("두께"), 0, 3);
        layout.Controls.Add(thicknessTextBox, 1, 3);

        panel.Controls.Add(layout);
        return panel;
    }

    private void InputPathBrowseButton_Click(
        object? sender,
        EventArgs e
    )
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "DWG 파일이 들어 있는 폴더를 선택하세요",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(InputPathTextBox.Text)
                ? InputPathTextBox.Text
                : string.Empty
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        InputPathTextBox.Text = dialog.SelectedPath;
    }

    private void InputFileSearchTextBox_TextChanged(
        object? sender,
        EventArgs e
    )
    {
        ApplyInputFileSearch();
    }

    private void ApplyInputFileSearch(string? preferredPath = null)
    {
        string query = InputFileSearchTextBox.Text.Trim();
        bool hasQuery = !string.IsNullOrWhiteSpace(query);

        preferredPath ??= InputFilesGrid.CurrentRow?.Tag as string;

        InputFilesGrid.ClearSelection();
        InputFilesGrid.CurrentCell = null;

        List<DataGridViewRow> folderRows = InputFilesGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.Tag is DwgFolderHeader)
            .ToList();

        HashSet<DataGridViewRow> groupedRows = new();

        foreach (DataGridViewRow folderRow in folderRows)
        {
            if (folderRow.Tag is not DwgFolderHeader folderHeader)
            {
                continue;
            }

            List<DataGridViewRow> childRows = GetFolderChildRows(
                InputFilesGrid,
                folderRow,
                folderHeader.FolderPath
            );

            foreach (DataGridViewRow childRow in childRows)
            {
                groupedRows.Add(childRow);
            }

            bool folderNameMatches = hasQuery &&
                GetFolderDisplayName(folderHeader.FolderPath).Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase
                );

            bool anyChildMatches = childRows.Any(childRow =>
                childRow.Tag is string filePath &&
                FileNameContains(filePath, query)
            );

            folderRow.Visible =
                !hasQuery || folderNameMatches || anyChildMatches;

            foreach (DataGridViewRow childRow in childRows)
            {
                bool fileNameMatches =
                    childRow.Tag is string filePath &&
                    FileNameContains(filePath, query);

                childRow.Visible = hasQuery
                    ? folderNameMatches || fileNameMatches
                    : !folderHeader.IsCollapsed;
            }
        }

        foreach (DataGridViewRow row in InputFilesGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row =>
                row.Tag is string &&
                !groupedRows.Contains(row)
            ))
        {
            if (row.Tag is not string filePath)
            {
                continue;
            }

            row.Visible = !hasQuery ||
                FileNameContains(filePath, query);
        }

        RestoreGridSelection(InputFilesGrid, preferredPath);
        InputFilesGrid_SelectionChanged(
            InputFilesGrid,
            EventArgs.Empty
        );
    }

    private static bool FileNameContains(
        string filePath,
        string query
    )
    {
        string fileName = Path.GetFileName(filePath) ?? string.Empty;
        return fileName.Contains(
            query,
            StringComparison.CurrentCultureIgnoreCase
        );
    }

    private void RefreshInputFilesButton_Click(
        object? sender,
        EventArgs e
    )
    {
        if (AnalyzeDwgFile == null)
        {
            MessageBox.Show(
                this,
                "현재 UI 미리보기 모드라서 DWG 분석 코드가 연결되어 있지 않습니다.",
                "미리보기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        string? selectedPath = InputFilesGrid.CurrentRow?.Tag as string;
        int addedCount = 0;
        int updatedCount = 0;
        int removedCount = 0;
        List<string> errors = new();

        RefreshInputFilesButton.Enabled = false;
        UseWaitCursor = true;

        try
        {
            List<DataGridViewRow> folderRows = InputFilesGrid.Rows
                .Cast<DataGridViewRow>()
                .Where(row => row.Tag is DwgFolderHeader)
                .ToList();

            HashSet<DataGridViewRow> groupedRows = folderRows
                .SelectMany(folderRow =>
                {
                    if (folderRow.Tag is not DwgFolderHeader folderHeader)
                    {
                        return Enumerable.Empty<DataGridViewRow>();
                    }

                    return GetFolderChildRows(
                        InputFilesGrid,
                        folderRow,
                        folderHeader.FolderPath
                    );
                })
                .ToHashSet();

            List<DataGridViewRow> standaloneRows = InputFilesGrid.Rows
                .Cast<DataGridViewRow>()
                .Where(row =>
                    row.Tag is string &&
                    !groupedRows.Contains(row)
                )
                .ToList();

            foreach (DataGridViewRow row in standaloneRows)
            {
                if (row.Tag is not string filePath)
                {
                    continue;
                }

                if (!File.Exists(filePath))
                {
                    InputFilesGrid.Rows.Remove(row);
                    removedCount++;
                    continue;
                }

                try
                {
                    ApplySizeToGridRow(row, AnalyzeDwgFile(filePath));
                    updatedCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            foreach (DataGridViewRow folderRow in folderRows
                .Where(row => row.DataGridView != null))
            {
                if (folderRow.Tag is not DwgFolderHeader folderHeader)
                {
                    continue;
                }

                if (!Directory.Exists(folderHeader.FolderPath))
                {
                    errors.Add(
                        $"{GetFolderDisplayName(folderHeader.FolderPath)}: " +
                        "폴더를 찾을 수 없습니다."
                    );
                    continue;
                }

                string[] diskFiles;

                try
                {
                    diskFiles = Directory
                        .EnumerateFiles(
                            folderHeader.FolderPath,
                            "*",
                            SearchOption.TopDirectoryOnly
                        )
                        .Where(path => string.Equals(
                            Path.GetExtension(path),
                            ".dwg",
                            StringComparison.OrdinalIgnoreCase
                        ))
                        .Select(Path.GetFullPath)
                        .OrderBy(
                            path => Path.GetFileName(path),
                            StringComparer.CurrentCultureIgnoreCase
                        )
                        .ToArray();
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"{GetFolderDisplayName(folderHeader.FolderPath)}: " +
                        ex.Message
                    );
                    continue;
                }

                HashSet<string> diskFileSet = diskFiles.ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

                List<DataGridViewRow> childRows = GetFolderChildRows(
                    InputFilesGrid,
                    folderRow,
                    folderHeader.FolderPath
                );

                foreach (DataGridViewRow childRow in childRows)
                {
                    if (childRow.Tag is not string filePath)
                    {
                        continue;
                    }

                    if (!diskFileSet.Contains(Path.GetFullPath(filePath)))
                    {
                        InputFilesGrid.Rows.Remove(childRow);
                        removedCount++;
                        continue;
                    }

                    ApplyFolderChildDisplay(childRow, filePath);

                    try
                    {
                        ApplySizeToGridRow(
                            childRow,
                            AnalyzeDwgFile(filePath)
                        );
                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(
                            $"{Path.GetFileName(filePath)}: {ex.Message}"
                        );
                    }
                }

                HashSet<string> listedPaths = InputFilesGrid.Rows
                    .Cast<DataGridViewRow>()
                    .Select(row => row.Tag as string)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => Path.GetFullPath(path!))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (string filePath in diskFiles
                    .Where(path => !listedPaths.Contains(path)))
                {
                    try
                    {
                        DwgFileSizeSnapshot size = AnalyzeDwgFile(filePath);
                        AddFolderChildRow(
                            folderRow,
                            folderHeader.FolderPath,
                            filePath,
                            size
                        );
                        listedPaths.Add(filePath);
                        addedCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(
                            $"{Path.GetFileName(filePath)}: {ex.Message}"
                        );
                    }
                }
            }

            ApplyInputFileSearch(selectedPath);
            SaveCurrentUiState(showSuccessMessage: false);

            string resultMessage =
                $"파일 목록을 새로고침했습니다.\n\n" +
                $"추가: {addedCount}개\n" +
                $"갱신: {updatedCount}개\n" +
                $"제거: {removedCount}개";

            if (errors.Count > 0)
            {
                resultMessage +=
                    "\n\n확인하지 못한 항목:\n" +
                    string.Join("\n", errors);
            }

            MessageBox.Show(
                this,
                resultMessage,
                "파일 목록 새로고침",
                MessageBoxButtons.OK,
                errors.Count == 0
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"파일 목록을 새로고침하지 못했습니다.\n\n{ex.Message}",
                "새로고침 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
        finally
        {
            UseWaitCursor = false;
            RefreshInputFilesButton.Enabled = true;
        }
    }

    private static void ApplySizeToGridRow(
        DataGridViewRow row,
        DwgFileSizeSnapshot size
    )
    {
        row.Cells["Width"].Value = FormatValue(size.Width);
        row.Cells["Height"].Value = FormatValue(size.Height);
        row.Cells["Thickness"].Value =
            FormatNullableValue(size.Thickness);
    }

    private void SaveCurrentStateButton_Click(
        object? sender,
        EventArgs e
    )
    {
        SaveCurrentUiState(showSuccessMessage: true);
    }

    private void SaveCurrentUiState(bool showSuccessMessage)
    {
        try
        {
            string settingsDirectory = Path.GetDirectoryName(
                UiStateSettingsFile
            )!;

            string manualOutputPath = AutoOutputPathCheckBox.Checked
                ? _manualOutputPath
                : OutputPathTextBox.Text.Trim();

            UiSavedState state = new()
            {
                FileListPath = InputPathTextBox.Text.Trim(),
                FileSearchText = InputFileSearchTextBox.Text,
                ManualOutputPath = manualOutputPath,
                UseAutomaticOutputPath = AutoOutputPathCheckBox.Checked,
                TargetWidth = ConversionWidthTextBox.Text.Trim(),
                TargetHeight = ConversionHeightTextBox.Text.Trim(),
                TargetThickness = ConversionThicknessTextBox.Text.Trim(),
                InputFiles = CaptureGridRows(InputFilesGrid),
                ConvertedFiles = CaptureGridRows(TargetFilesGrid),
                SelectedInputPath = InputFilesGrid.CurrentRow?.Tag as string,
                SelectedConvertedPath = TargetFilesGrid.CurrentRow?.Tag as string
            };

            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(
                UiStateSettingsFile,
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );

            if (showSuccessMessage)
            {
                MessageBox.Show(
                    this,
                    "현재 UI 값을 저장했습니다.",
                    "저장 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"현재 UI 값을 저장하지 못했습니다.\n\n{ex.Message}",
                "저장 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
    }

    private void LoadSavedUiState()
    {
        try
        {
            if (!File.Exists(UiStateSettingsFile))
            {
                return;
            }

            UiSavedState? state = JsonSerializer.Deserialize<UiSavedState>(
                File.ReadAllText(UiStateSettingsFile)
            );

            if (state == null)
            {
                return;
            }

            InputPathTextBox.Text = state.FileListPath ?? string.Empty;
            _manualOutputPath = state.ManualOutputPath ?? string.Empty;

            RestoreGridRows(InputFilesGrid, state.InputFiles);
            RestoreGridRows(TargetFilesGrid, state.ConvertedFiles);
            InputFileSearchTextBox.Text =
                state.FileSearchText ?? string.Empty;
            RestoreGridSelection(InputFilesGrid, state.SelectedInputPath);
            RestoreGridSelection(
                TargetFilesGrid,
                state.SelectedConvertedPath
            );

            ConversionWidthTextBox.Text = state.TargetWidth ?? string.Empty;
            ConversionHeightTextBox.Text = state.TargetHeight ?? string.Empty;
            ConversionThicknessTextBox.Text =
                state.TargetThickness ?? string.Empty;

            DataGridViewRow? selectedInputRow =
                GetSelectedRow(InputFilesGrid);

            if (selectedInputRow == null)
            {
                SetConversionThicknessInputEnabled(false);
            }
            else if (string.IsNullOrWhiteSpace(
                GetCellText(selectedInputRow, "Thickness")
            ))
            {
                SetConversionThicknessInputEnabled(false);
            }
            else
            {
                SetConversionThicknessInputEnabled(true);
            }

            AutoOutputPathCheckBox.Checked = state.UseAutomaticOutputPath;
            UpdateOutputPathMode();
        }
        catch
        {
            // 설정 파일을 읽지 못해도 프로그램 실행과 DWG 처리는 계속한다.
        }
    }

    private static List<UiSavedFileRow> CaptureGridRows(DataGridView grid)
    {
        List<UiSavedFileRow> result = new();
        string? currentFolderPath = null;

        foreach (DataGridViewRow row in grid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow))
        {
            if (row.Tag is DwgFolderHeader folderHeader)
            {
                currentFolderPath = folderHeader.FolderPath;
                result.Add(new UiSavedFileRow
                {
                    IsFolderHeader = true,
                    IsFolderCollapsed = folderHeader.IsCollapsed,
                    FolderPath = folderHeader.FolderPath,
                    FileName = GetCellText(row, "FileName")
                });
                continue;
            }

            if (currentFolderPath != null &&
                !IsFolderChildRow(row, currentFolderPath))
            {
                currentFolderPath = null;
            }

            result.Add(new UiSavedFileRow
            {
                FilePath = row.Tag as string ?? string.Empty,
                FolderPath = currentFolderPath,
                FileName = GetCellText(row, "FileName"),
                Width = GetCellText(row, "Width"),
                Height = GetCellText(row, "Height"),
                Thickness = GetCellText(row, "Thickness"),
                ModifiedTime = grid.Columns.Contains("ModifiedTime")
                    ? GetCellText(row, "ModifiedTime")
                    : string.Empty
            });
        }

        return result;
    }

    private static void RestoreGridRows(
        DataGridView grid,
        IEnumerable<UiSavedFileRow>? savedRows
    )
    {
        grid.Rows.Clear();
        DwgFolderHeader? currentFolderHeader = null;

        foreach (UiSavedFileRow savedRow in savedRows ??
            Enumerable.Empty<UiSavedFileRow>())
        {
            if (savedRow.IsFolderHeader &&
                !string.IsNullOrWhiteSpace(savedRow.FolderPath))
            {
                int headerIndex = grid.Rows.Add(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty
                );

                DataGridViewRow headerRow = grid.Rows[headerIndex];
                currentFolderHeader = new DwgFolderHeader
                {
                    FolderPath = savedRow.FolderPath,
                    IsCollapsed = savedRow.IsFolderCollapsed
                };
                headerRow.Tag = currentFolderHeader;
                UpdateFolderHeaderText(headerRow, currentFolderHeader);
                ApplyFolderHeaderStyle(headerRow);
                continue;
            }

            int rowIndex = grid.Rows.Add(
                savedRow.FileName,
                savedRow.Width,
                savedRow.Height,
                savedRow.Thickness
            );
            DataGridViewRow restoredRow = grid.Rows[rowIndex];
            restoredRow.Tag = savedRow.FilePath;

            if (grid.Columns.Contains("ModifiedTime"))
            {
                string modifiedTime = savedRow.ModifiedTime;

                if (string.IsNullOrWhiteSpace(modifiedTime) &&
                    File.Exists(savedRow.FilePath))
                {
                    modifiedTime =
                        GetFileModifiedTimeText(savedRow.FilePath);
                }

                restoredRow.Cells["ModifiedTime"].Value =
                    modifiedTime;
            }

            bool belongsToCurrentFolder =
                currentFolderHeader != null &&
                string.Equals(
                    savedRow.FolderPath,
                    currentFolderHeader.FolderPath,
                    StringComparison.OrdinalIgnoreCase
                );

            if (belongsToCurrentFolder)
            {
                ApplyFolderChildDisplay(
                    restoredRow,
                    savedRow.FilePath
                );
                restoredRow.Visible = !currentFolderHeader!.IsCollapsed;
            }
            else
            {
                currentFolderHeader = null;
            }
        }
    }

    private static void RestoreGridSelection(
        DataGridView grid,
        string? selectedPath
    )
    {
        grid.ClearSelection();

        DataGridViewRow? selectedRow = grid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row =>
                row.Visible &&
                string.Equals(
                    row.Tag as string,
                    selectedPath,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        selectedRow ??= grid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row =>
                row.Visible &&
                row.Tag is string path &&
                !string.IsNullOrWhiteSpace(path)
            );

        if (selectedRow != null)
        {
            selectedRow.Selected = true;
            grid.CurrentCell = selectedRow.Cells["FileName"];
        }
    }

    private void OutputPathBrowseButton_Click(
        object? sender,
        EventArgs e
    )
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "수정된 DWG를 저장할 폴더를 선택하세요",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(OutputPathTextBox.Text)
                ? OutputPathTextBox.Text
                : string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            OutputPathTextBox.Text = dialog.SelectedPath;
            _manualOutputPath = dialog.SelectedPath;
        }
    }

    private void AutoOutputPathCheckBox_CheckedChanged(
        object? sender,
        EventArgs e
    )
    {
        UpdateOutputPathMode();
    }

    private void UpdateOutputPathMode()
    {
        bool useAutomaticPath = AutoOutputPathCheckBox.Checked;

        if (useAutomaticPath)
        {
            if (!OutputPathTextBox.ReadOnly &&
                !string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
            {
                _manualOutputPath = OutputPathTextBox.Text.Trim();
            }

            OutputPathTextBox.Text = "자동: 각 DWG 폴더\\수정된 파일";
            OutputPathTextBox.ReadOnly = true;
            OutputPathBrowseButton.Enabled = false;
            return;
        }

        OutputPathTextBox.ReadOnly = false;
        OutputPathTextBox.Text = _manualOutputPath;
        OutputPathBrowseButton.Enabled = true;
    }

    private void AddFilesButton_Click(
        object? sender,
        EventArgs e
    )
    {
        SelectAndAddDwgFile();
    }

    private void AddFolderButton_Click(
        object? sender,
        EventArgs e
    )
    {
        SelectAndAddDwgFolder();
    }

    private void SelectAndAddDwgFolder()
    {
        string listDirectory = InputPathTextBox.Text.Trim();

        using FolderBrowserDialog dialog = new()
        {
            Description = "목록에 추가할 DWG 폴더를 선택하세요",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(listDirectory)
                ? listDirectory
                : string.Empty
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string selectedFolder = Path.GetFullPath(dialog.SelectedPath);
        string[] dwgFiles = Directory
            .EnumerateFiles(selectedFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".dwg",
                StringComparison.OrdinalIgnoreCase
            ))
            .OrderBy(
                path => Path.GetFileName(path),
                StringComparer.CurrentCultureIgnoreCase
            )
            .ToArray();

        if (dwgFiles.Length == 0)
        {
            MessageBox.Show(
                this,
                "선택한 폴더에 DWG 파일이 없습니다.",
                "DWG 폴더 추가",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        InputPathTextBox.Text = selectedFolder;
        AddDwgFiles(dwgFiles, selectedFolder);
    }

    private void SelectAndAddDwgFile()
    {
        string listDirectory = InputPathTextBox.Text.Trim();

        using OpenFileDialog dialog = new()
        {
            Title = "추가할 DWG 파일 하나 선택",
            Filter = "AutoCAD DWG 파일 (*.dwg)|*.dwg",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true,
            RestoreDirectory = true,
            InitialDirectory = Directory.Exists(listDirectory)
                ? listDirectory
                : string.Empty
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string? selectedDirectory = Path.GetDirectoryName(dialog.FileName);

        if (string.IsNullOrWhiteSpace(InputPathTextBox.Text) &&
            !string.IsNullOrWhiteSpace(selectedDirectory))
        {
            InputPathTextBox.Text = selectedDirectory;
        }

        AddDwgFiles(new[] { dialog.FileName });
    }

    private void AddDwgFiles(
        IEnumerable<string> filePaths,
        string? folderGroupPath = null
    )
    {
        if (AnalyzeDwgFile == null)
        {
            MessageBox.Show(
                this,
                "현재 UI 미리보기 모드라서 DWG 분석 코드가 연결되어 있지 않습니다.",
                "미리보기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        List<string> errors = new();
        string? normalizedFolderGroupPath =
            string.IsNullOrWhiteSpace(folderGroupPath)
                ? null
                : Path.GetFullPath(folderGroupPath);

        DataGridViewRow? folderHeaderRow =
            normalizedFolderGroupPath == null
                ? null
                : FindFolderHeaderRow(normalizedFolderGroupPath);

        bool hadAnyFileRows = InputFilesGrid.Rows
            .Cast<DataGridViewRow>()
            .Any(row => row.Tag is string path &&
                        !string.IsNullOrWhiteSpace(path));
        bool addedAnyFile = false;

        foreach (string filePath in filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool alreadyAdded = InputFilesGrid.Rows
                .Cast<DataGridViewRow>()
                .Any(row => string.Equals(
                    row.Tag as string,
                    filePath,
                    StringComparison.OrdinalIgnoreCase
                ));

            if (alreadyAdded)
            {
                continue;
            }

            try
            {
                DwgFileSizeSnapshot size = AnalyzeDwgFile(filePath);

                DataGridViewRow inputRow;

                if (normalizedFolderGroupPath == null)
                {
                    int inputRowIndex = InputFilesGrid.Rows.Add(
                        Path.GetFileName(filePath),
                        FormatValue(size.Width),
                        FormatValue(size.Height),
                        FormatNullableValue(size.Thickness)
                    );
                    inputRow = InputFilesGrid.Rows[inputRowIndex];
                    inputRow.Tag = filePath;
                }
                else
                {
                    folderHeaderRow ??=
                        AddFolderHeaderRow(normalizedFolderGroupPath);

                    inputRow = AddFolderChildRow(
                        folderHeaderRow,
                        normalizedFolderGroupPath,
                        filePath,
                        size
                    );
                }

                addedAnyFile = true;

                if (!hadAnyFileRows)
                {
                    SetCurrentSizeBoxes(size);
                    SetConversionSizeBoxes(inputRow);
                    InputFilesGrid.ClearSelection();
                    inputRow.Selected = true;
                    InputFilesGrid.CurrentCell =
                        inputRow.Cells["FileName"];
                    hadAnyFileRows = true;
                }

                string? directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    if (string.IsNullOrWhiteSpace(InputPathTextBox.Text))
                    {
                        InputPathTextBox.Text = directory;
                    }

                    if (!AutoOutputPathCheckBox.Checked &&
                        string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
                    {
                        OutputPathTextBox.Text = directory;
                        _manualOutputPath = directory;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    $"{Path.GetFileName(filePath)}: {ex.Message}"
                );
            }
        }

        ApplyInputFileSearch();

        if (errors.Count > 0)
        {
            MessageBox.Show(
                this,
                "일부 파일의 현재 치수를 읽지 못했습니다.\n\n" +
                string.Join("\n", errors),
                "파일 추가 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }

        if (addedAnyFile)
        {
            SaveCurrentUiState(showSuccessMessage: false);
        }
    }

    private DataGridViewRow? FindFolderHeaderRow(string folderPath)
    {
        return InputFilesGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row =>
                row.Tag is DwgFolderHeader folderHeader &&
                string.Equals(
                    folderHeader.FolderPath,
                    folderPath,
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    private DataGridViewRow AddFolderHeaderRow(string folderPath)
    {
        int rowIndex = InputFilesGrid.Rows.Add(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty
        );

        DataGridViewRow row = InputFilesGrid.Rows[rowIndex];
        row.Tag = new DwgFolderHeader
        {
            FolderPath = folderPath
        };
        UpdateFolderHeaderText(row, (DwgFolderHeader)row.Tag);
        ApplyFolderHeaderStyle(row);
        return row;
    }

    private DataGridViewRow AddFolderChildRow(
        DataGridViewRow folderHeaderRow,
        string folderPath,
        string filePath,
        DwgFileSizeSnapshot size
    )
    {
        int insertIndex = folderHeaderRow.Index + 1;

        while (insertIndex < InputFilesGrid.Rows.Count &&
               IsFolderChildRow(
                   InputFilesGrid.Rows[insertIndex],
                   folderPath
               ))
        {
            insertIndex++;
        }

        InputFilesGrid.Rows.Insert(
            insertIndex,
            $"└ {Path.GetFileName(filePath)}",
            FormatValue(size.Width),
            FormatValue(size.Height),
            FormatNullableValue(size.Thickness)
        );

        DataGridViewRow row = InputFilesGrid.Rows[insertIndex];
        row.Tag = filePath;
        ApplyFolderChildDisplay(row, filePath);
        row.Visible =
            folderHeaderRow.Tag is not DwgFolderHeader folderHeader ||
            !folderHeader.IsCollapsed;
        return row;
    }

    private static void ApplyFolderChildDisplay(
        DataGridViewRow row,
        string filePath
    )
    {
        row.Cells["FileName"].Value =
            $"└ {Path.GetFileName(filePath)}";
        row.Cells["FileName"].Style.Padding =
            new Padding(18, 0, 0, 0);
    }

    private void InputFilesGrid_CellClick(
        object? sender,
        DataGridViewCellEventArgs e
    )
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
            InputFilesGrid.Columns[e.ColumnIndex].Name != "FileName")
        {
            return;
        }

        DataGridViewRow folderRow = InputFilesGrid.Rows[e.RowIndex];

        if (folderRow.Tag is not DwgFolderHeader folderHeader)
        {
            return;
        }

        folderHeader.IsCollapsed = !folderHeader.IsCollapsed;
        InputFilesGrid.CurrentCell = folderRow.Cells["FileName"];

        foreach (DataGridViewRow childRow in GetFolderChildRows(
            InputFilesGrid,
            folderRow,
            folderHeader.FolderPath
        ))
        {
            childRow.Selected = false;
            childRow.Visible = !folderHeader.IsCollapsed;
        }

        UpdateFolderHeaderText(folderRow, folderHeader);
        ApplyInputFileSearch();
        InputFilesGrid.Invalidate();
    }

    private static void UpdateFolderHeaderText(
        DataGridViewRow row,
        DwgFolderHeader folderHeader
    )
    {
        string indicator = folderHeader.IsCollapsed ? "▶" : "▼";
        row.Cells["FileName"].Value =
            $"{indicator} 📁 {GetFolderDisplayName(folderHeader.FolderPath)}";
    }

    private static void ApplyFolderHeaderStyle(DataGridViewRow row)
    {
        row.ReadOnly = true;
        row.Height = 32;
        row.DefaultCellStyle.BackColor = Color.FromArgb(229, 235, 244);
        row.DefaultCellStyle.ForeColor = Color.FromArgb(31, 41, 55);
        row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 218, 238);
        row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 45, 82);
        row.DefaultCellStyle.Font = new Font(
            "Segoe UI",
            9.5F,
            FontStyle.Bold,
            GraphicsUnit.Point
        );
    }

    private static bool IsFolderChildRow(
        DataGridViewRow row,
        string folderPath
    )
    {
        _ = folderPath;

        // 저장 상태에서 복원된 행은 앞쪽 공백 개수가 달라질 수 있다.
        // 공백 수는 무시하고 폴더 하위 파일 표시인 '└'로 판단한다.
        string displayedFileName = GetCellText(row, "FileName")
            .TrimStart();

        return row.Tag is string filePath &&
               !string.IsNullOrWhiteSpace(filePath) &&
               displayedFileName.StartsWith(
                   "└",
                   StringComparison.Ordinal
               );
    }

    private static string GetFolderDisplayName(string folderPath)
    {
        string trimmedPath = folderPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );
        string name = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(name)
            ? folderPath
            : name;
    }

    private void InputFilesGrid_SelectionChanged(
        object? sender,
        EventArgs e
    )
    {
        DataGridViewRow? row = GetSelectedRow(InputFilesGrid);

        if (row == null)
        {
            ClearCurrentSizeBoxes();
            ClearConversionSizeBoxes();
            return;
        }

        CurrentWidthTextBox.Text = GetCellText(row, "Width");
        CurrentHeightTextBox.Text = GetCellText(row, "Height");
        CurrentThicknessTextBox.Text = GetCellText(row, "Thickness");
        SetConversionSizeBoxes(row);
    }

    private void TargetFilesGrid_SelectionChanged(
        object? sender,
        EventArgs e
    )
    {
        DataGridViewRow? row = GetSelectedRow(TargetFilesGrid);

        if (row != null)
        {
            SetTargetSizeBoxes(row);
            return;
        }

        ClearTargetDisplayBoxes();
    }

    private void TargetFilesGrid_CellMouseDoubleClick(
        object? sender,
        DataGridViewCellMouseEventArgs e
    )
    {
        if (e.RowIndex < 0 ||
            e.RowIndex >= TargetFilesGrid.Rows.Count)
        {
            return;
        }

        DataGridViewRow row = TargetFilesGrid.Rows[e.RowIndex];
        string? filePath = ResolveTargetFilePath(row);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            MessageBox.Show(
                this,
                "이 항목의 DWG 파일 경로를 찾을 수 없습니다.",
                "파일 열기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        if (!File.Exists(filePath))
        {
            MessageBox.Show(
                this,
                "수정된 DWG 파일을 찾을 수 없습니다.\n\n" +
                filePath,
                "파일 열기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                WorkingDirectory =
                    Path.GetDirectoryName(filePath) ?? string.Empty,
                Verb = "open",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "DWG 파일을 실행하지 못했습니다.\n\n" +
                ex.Message,
                "파일 열기 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
    }

    private string? ResolveTargetFilePath(DataGridViewRow row)
    {
        string? taggedPath = row.Tag as string;

        if (!string.IsNullOrWhiteSpace(taggedPath) &&
            File.Exists(taggedPath))
        {
            return taggedPath;
        }

        string fileName = GetCellText(row, "FileName").Trim();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return taggedPath;
        }

        if (!AutoOutputPathCheckBox.Checked)
        {
            string manualDirectory = OutputPathTextBox.Text.Trim();

            if (Directory.Exists(manualDirectory))
            {
                string manualCandidate = Path.Combine(
                    manualDirectory,
                    fileName
                );

                if (File.Exists(manualCandidate))
                {
                    row.Tag = manualCandidate;
                    return manualCandidate;
                }
            }
        }

        foreach (string inputPath in InputFilesGrid.Rows
            .Cast<DataGridViewRow>()
            .Select(inputRow => inputRow.Tag as string)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? inputDirectory = Path.GetDirectoryName(inputPath);

            if (string.IsNullOrWhiteSpace(inputDirectory))
            {
                continue;
            }

            string automaticCandidate = Path.Combine(
                inputDirectory,
                "수정된 파일",
                fileName
            );

            if (File.Exists(automaticCandidate))
            {
                row.Tag = automaticCandidate;
                return automaticCandidate;
            }
        }

        return taggedPath;
    }

    private void FileGrid_KeyDown(
        object? sender,
        KeyEventArgs e
    )
    {
        if (e.KeyCode != Keys.Delete ||
            sender is not DataGridView sourceGrid)
        {
            return;
        }

        List<DataGridViewRow> selectedRows = sourceGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .ToList();

        if (selectedRows.Count == 0 && sourceGrid.CurrentRow != null)
        {
            selectedRows.Add(sourceGrid.CurrentRow);
        }

        HashSet<string> selectedPaths = selectedRows
            .Select(row => row.Tag as string)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<DataGridViewRow> selectedFolderRows = selectedRows
            .Where(row => row.Tag is DwgFolderHeader)
            .ToList();

        foreach (DataGridViewRow folderRow in selectedFolderRows)
        {
            if (folderRow.Tag is not DwgFolderHeader folderHeader)
            {
                continue;
            }

            foreach (DataGridViewRow childRow in GetFolderChildRows(
                sourceGrid,
                folderRow,
                folderHeader.FolderPath
            ))
            {
                if (childRow.Tag is string childPath)
                {
                    selectedPaths.Add(childPath);
                }
            }
        }

        if (selectedPaths.Count == 0 &&
            selectedFolderRows.Count == 0)
        {
            return;
        }

        RemoveRowsByPath(sourceGrid, selectedPaths);

        foreach (DataGridViewRow folderRow in selectedFolderRows
            .Where(row => row.DataGridView != null)
            .ToList())
        {
            sourceGrid.Rows.Remove(folderRow);
        }

        if (ReferenceEquals(sourceGrid, InputFilesGrid))
        {
            ApplyInputFileSearch();
        }
        else
        {
            if (TargetFilesGrid.CurrentRow == null)
            {
                ClearTargetDisplayBoxes();
            }
            else
            {
                TargetFilesGrid_SelectionChanged(TargetFilesGrid, EventArgs.Empty);
            }
        }

        SaveCurrentUiState(showSuccessMessage: false);

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private static void RemoveRowsByPath(
        DataGridView grid,
        IReadOnlySet<string> paths
    )
    {
        foreach (DataGridViewRow row in grid.Rows
            .Cast<DataGridViewRow>()
            .Where(row =>
                row.Tag is string path &&
                paths.Contains(path)
            )
            .ToList())
        {
            grid.Rows.Remove(row);
        }
    }

    private static List<DataGridViewRow> GetFolderChildRows(
        DataGridView grid,
        DataGridViewRow folderRow,
        string folderPath
    )
    {
        List<DataGridViewRow> result = new();

        for (int index = folderRow.Index + 1;
             index < grid.Rows.Count;
             index++)
        {
            DataGridViewRow row = grid.Rows[index];

            if (row.Tag is DwgFolderHeader)
            {
                break;
            }

            if (!IsFolderChildRow(row, folderPath))
            {
                break;
            }

            result.Add(row);
        }

        return result;
    }

    private List<DataGridViewRow> GetSelectedInputFileRows()
    {
        List<DataGridViewRow> selectedRows = InputFilesGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .ToList();

        if (selectedRows.Count == 0 && InputFilesGrid.CurrentRow != null)
        {
            selectedRows.Add(InputFilesGrid.CurrentRow);
        }

        List<DataGridViewRow> fileRows = new();

        foreach (DataGridViewRow selectedRow in selectedRows)
        {
            if (selectedRow.Tag is string filePath &&
                !string.IsNullOrWhiteSpace(filePath))
            {
                fileRows.Add(selectedRow);
                continue;
            }

            if (selectedRow.Tag is DwgFolderHeader folderHeader)
            {
                fileRows.AddRange(GetFolderChildRows(
                    InputFilesGrid,
                    selectedRow,
                    folderHeader.FolderPath
                ));
            }
        }

        return fileRows
            .Distinct()
            .OrderBy(row => row.Index)
            .ToList();
    }

    private void MoveSelectedButton_Click(
        object? sender,
        EventArgs e
    )
    {
        if (ConvertDwgFile == null)
        {
            MessageBox.Show(
                this,
                "현재 UI 미리보기 모드라서 DWG 변환 코드가 연결되어 있지 않습니다.",
                "미리보기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        List<DataGridViewRow> inputRows = GetSelectedInputFileRows();

        if (inputRows.Count == 0)
        {
            MessageBox.Show(
                this,
                "왼쪽 목록에서 변환할 DWG 파일을 선택하세요.",
                "확인",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        bool useAutomaticOutputPath = AutoOutputPathCheckBox.Checked;
        string manualOutputDirectory = OutputPathTextBox.Text.Trim();

        if (!useAutomaticOutputPath &&
            string.IsNullOrWhiteSpace(manualOutputDirectory))
        {
            MessageBox.Show(
                this,
                "수정 파일 출력 경로를 선택하세요.",
                "확인",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        double targetWidth;
        double targetHeight;
        double? targetThickness;

        try
        {
            targetWidth = ReadRequiredPositiveValue(
                ConversionWidthTextBox.Text.Trim(),
                "목표 가로"
            );
            targetHeight = ReadRequiredPositiveValue(
                ConversionHeightTextBox.Text.Trim(),
                "목표 세로"
            );
            targetThickness = ReadOptionalPositiveValue(
                ConversionThicknessTextBox.Text.Trim(),
                "목표 두께"
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "목표값 확인",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        List<string> completedFiles = new();
        List<string> errors = new();

        Enabled = false;
        UseWaitCursor = true;

        try
        {
            foreach (DataGridViewRow row in inputRows)
            {
                string? inputPath = row.Tag as string;

                if (string.IsNullOrWhiteSpace(inputPath))
                {
                    continue;
                }

                try
                {
                    string outputDirectory;

                    if (useAutomaticOutputPath)
                    {
                        string inputDirectory = Path.GetDirectoryName(inputPath)
                            ?? throw new Exception(
                                "원본 DWG 파일의 폴더를 찾지 못했습니다."
                            );

                        outputDirectory = Path.Combine(
                            inputDirectory,
                            "수정된 파일"
                        );
                    }
                    else
                    {
                        outputDirectory = manualOutputDirectory;
                    }

                    Directory.CreateDirectory(outputDirectory);

                    string outputPath = ConvertDwgFile(
                        new DwgConversionRequest
                        {
                            InputPath = inputPath,
                            OutputDirectory = outputDirectory,
                            TargetWidth = targetWidth,
                            TargetHeight = targetHeight,
                            TargetThickness = targetThickness
                        }
                    );

                    completedFiles.Add(outputPath);
                    AddOrUpdateConvertedFile(
                        outputPath,
                        targetWidth,
                        targetHeight,
                        targetThickness
                    );
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"{Path.GetFileName(inputPath)}: {ex.Message}"
                    );
                }
            }
        }
        finally
        {
            UseWaitCursor = false;
            Enabled = true;
        }

        if (completedFiles.Count > 0)
        {
            SaveCurrentUiState(showSuccessMessage: false);
        }

        string message =
            $"변환 완료: {completedFiles.Count}개" +
            (errors.Count > 0
                ? $"\n변환 실패: {errors.Count}개\n\n" +
                  string.Join("\n", errors)
                : useAutomaticOutputPath
                    ? "\n\n저장 경로: 각 원본 DWG 폴더의 '수정된 파일' 폴더"
                    : $"\n\n저장 경로: {manualOutputDirectory}");

        MessageBox.Show(
            this,
            message,
            errors.Count == 0 ? "변환 완료" : "변환 결과",
            MessageBoxButtons.OK,
            errors.Count == 0
                ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning
        );
    }

    private void SetCurrentSizeBoxes(
        DwgFileSizeSnapshot size
    )
    {
        CurrentWidthTextBox.Text = FormatValue(size.Width);
        CurrentHeightTextBox.Text = FormatValue(size.Height);
        CurrentThicknessTextBox.Text = FormatNullableValue(size.Thickness);
    }

    private void AddOrUpdateConvertedFile(
        string outputPath,
        double targetWidth,
        double targetHeight,
        double? targetThickness
    )
    {
        DataGridViewRow? resultRow = TargetFilesGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row => string.Equals(
                row.Tag as string,
                outputPath,
                StringComparison.OrdinalIgnoreCase
            ));

        if (resultRow == null)
        {
            string modifiedTime = GetFileModifiedTimeText(outputPath);
            int rowIndex = TargetFilesGrid.Rows.Add(
                Path.GetFileName(outputPath),
                FormatValue(targetWidth),
                FormatValue(targetHeight),
                FormatNullableValue(targetThickness),
                modifiedTime
            );
            resultRow = TargetFilesGrid.Rows[rowIndex];
            resultRow.Tag = outputPath;
        }
        else
        {
            resultRow.Cells["FileName"].Value = Path.GetFileName(outputPath);
            resultRow.Cells["Width"].Value = FormatValue(targetWidth);
            resultRow.Cells["Height"].Value = FormatValue(targetHeight);
            resultRow.Cells["Thickness"].Value =
                FormatNullableValue(targetThickness);
            resultRow.Cells["ModifiedTime"].Value =
                GetFileModifiedTimeText(outputPath);
        }

        TargetFilesGrid.ClearSelection();
        resultRow.Selected = true;
        TargetFilesGrid.CurrentCell = resultRow.Cells["FileName"];
        SetTargetSizeBoxes(resultRow);
    }

    private static string GetFileModifiedTimeText(string filePath)
    {
        try
        {
            DateTime modifiedTime = File.GetLastWriteTime(filePath);
            return modifiedTime.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture
            );
        }
        catch
        {
            return string.Empty;
        }
    }

    private void SetConversionSizeBoxes(
        DataGridViewRow inputRow
    )
    {
        ConversionWidthTextBox.Text = GetCellText(inputRow, "Width");
        ConversionHeightTextBox.Text = GetCellText(inputRow, "Height");

        string thickness = GetCellText(inputRow, "Thickness");
        ConversionThicknessTextBox.Text = thickness;
        SetConversionThicknessInputEnabled(
            !string.IsNullOrWhiteSpace(thickness)
        );
    }

    private void SetConversionThicknessInputEnabled(bool enabled)
    {
        ConversionThicknessTextBox.Enabled = enabled;

        if (!enabled)
        {
            ConversionThicknessTextBox.Clear();
        }
    }

    private void SetTargetSizeBoxes(
        DataGridViewRow row
    )
    {
        string width = GetCellText(row, "Width");
        string height = GetCellText(row, "Height");
        string thickness = GetCellText(row, "Thickness");

        TargetWidthTextBox.Text = width;
        TargetHeightTextBox.Text = height;
        TargetThicknessTextBox.Text = thickness;
    }

    private void ClearCurrentSizeBoxes()
    {
        CurrentWidthTextBox.Clear();
        CurrentHeightTextBox.Clear();
        CurrentThicknessTextBox.Clear();
    }

    private void ClearTargetDisplayBoxes()
    {
        TargetWidthTextBox.Clear();
        TargetHeightTextBox.Clear();
        TargetThicknessTextBox.Clear();
    }

    private void ClearConversionSizeBoxes()
    {
        ConversionWidthTextBox.Clear();
        ConversionHeightTextBox.Clear();
        ConversionThicknessTextBox.Clear();
        SetConversionThicknessInputEnabled(false);
    }

    private static DataGridViewRow? GetSelectedRow(
        DataGridView grid
    )
    {
        DataGridViewRow? selectedFileRow = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row =>
                row.Tag is string path &&
                !string.IsNullOrWhiteSpace(path)
            );

        if (selectedFileRow != null)
        {
            return selectedFileRow;
        }

        return grid.CurrentRow?.Tag is string currentPath &&
               !string.IsNullOrWhiteSpace(currentPath)
            ? grid.CurrentRow
            : null;
    }

    private static string GetCellText(
        DataGridViewRow row,
        string columnName
    )
    {
        return Convert.ToString(
            row.Cells[columnName].Value,
            CultureInfo.CurrentCulture
        )?.Trim() ?? string.Empty;
    }

    private static string FormatValue(double value)
    {
        return value.ToString(
            "0.###",
            CultureInfo.CurrentCulture
        );
    }

    private static string FormatNullableValue(double? value)
    {
        return value.HasValue
            ? FormatValue(value.Value)
            : string.Empty;
    }

    private static double ReadRequiredPositiveValue(
        string text,
        string fieldName
    )
    {
        if (!TryReadDouble(text, out double value) || value <= 0.0)
        {
            throw new Exception(
                $"{fieldName} 값이 올바르지 않습니다: '{text}'"
            );
        }

        return value;
    }

    private static double? ReadOptionalPositiveValue(
        string text,
        string fieldName
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ReadRequiredPositiveValue(text, fieldName);
    }

    private static bool TryReadDouble(
        string text,
        out double value
    )
    {
        return double.TryParse(
                   text,
                   NumberStyles.Float,
                   CultureInfo.CurrentCulture,
                   out value
               ) ||
               double.TryParse(
                   text,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value
               );
    }

    private static Panel CreateCard()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            Padding = new Padding(20),
            Margin = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static TableLayoutPanel CreateSectionLayout(
        float pathAreaHeight
    )
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = CardBackground
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, pathAreaHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        return layout;
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 41, 55)
        };
    }

    private static Panel CreateOutputPathPanel(
        out TextBox pathTextBox,
        out Button browseButton,
        out CheckBox automaticCheckBox
    )
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            Padding = new Padding(0, 4, 0, 8),
            Margin = new Padding(0)
        };

        TableLayoutPanel headerLayout = new()
        {
            Dock = DockStyle.Top,
            Height = 28,
            ColumnCount = 2,
            BackColor = CardBackground
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145F));

        Label label = new()
        {
            Text = "수정 파일 출력 경로",
            Dock = DockStyle.Fill,
            ForeColor = SecondaryTextColor,
            TextAlign = ContentAlignment.MiddleLeft
        };

        automaticCheckBox = new CheckBox
        {
            Text = "자동 출력",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            CheckAlign = ContentAlignment.MiddleRight,
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };

        TableLayoutPanel pathLayout = new()
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            ColumnCount = 2,
            BackColor = CardBackground
        };
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // 화면 배율이 높아도 "찾아보기" 네 글자가 모두 보이도록
        // 버튼 열에 충분한 고정 폭을 둔다.
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));

        pathTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "출력 경로를 선택하세요",
            Margin = new Padding(0, 3, 8, 3)
        };

        browseButton = new Button
        {
            Text = "찾아보기",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(238, 242, 247),
            ForeColor = Color.FromArgb(31, 41, 55),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 2, 0, 2)
        };
        browseButton.FlatAppearance.BorderColor = BorderColor;

        headerLayout.Controls.Add(label, 0, 0);
        headerLayout.Controls.Add(automaticCheckBox, 1, 0);
        pathLayout.Controls.Add(pathTextBox, 0, 0);
        pathLayout.Controls.Add(browseButton, 1, 0);

        panel.Controls.Add(pathLayout);
        panel.Controls.Add(headerLayout);
        return panel;
    }

    private static Panel CreatePathPanel(
        string title,
        out TextBox pathTextBox,
        out Button browseButton
    )
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            Padding = new Padding(0, 4, 0, 8),
            Margin = new Padding(0)
        };

        Label label = new()
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = SecondaryTextColor
        };

        TableLayoutPanel pathLayout = new()
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            ColumnCount = 2,
            BackColor = CardBackground
        };
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));

        pathTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "경로를 선택하세요",
            Margin = new Padding(0, 3, 8, 3)
        };

        browseButton = new Button
        {
            Text = "찾아보기",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(238, 242, 247),
            ForeColor = Color.FromArgb(31, 41, 55),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 2, 0, 2)
        };
        browseButton.FlatAppearance.BorderColor = BorderColor;

        pathLayout.Controls.Add(pathTextBox, 0, 0);
        pathLayout.Controls.Add(browseButton, 1, 0);

        panel.Controls.Add(pathLayout);
        panel.Controls.Add(label);
        return panel;
    }

    private static Panel CreateSizePanel(
        string title,
        bool readOnly,
        out TextBox widthTextBox,
        out TextBox heightTextBox,
        out TextBox thicknessTextBox
    )
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(249, 250, 252),
            Padding = new Padding(14),
            Margin = new Padding(0, 8, 0, 12),
            BorderStyle = BorderStyle.FixedSingle
        };

        Label titleLabel = new()
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 41, 55)
        };

        TableLayoutPanel container = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        TableLayoutPanel values = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = new Padding(0)
        };
        values.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        for (int index = 0; index < 3; index++)
        {
            values.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
            values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        }

        widthTextBox = CreateSizeTextBox(readOnly);
        heightTextBox = CreateSizeTextBox(readOnly);
        thicknessTextBox = CreateSizeTextBox(readOnly);

        values.Controls.Add(CreateValueLabel("가로"), 0, 0);
        values.Controls.Add(widthTextBox, 1, 0);
        values.Controls.Add(CreateValueLabel("세로"), 2, 0);
        values.Controls.Add(heightTextBox, 3, 0);
        values.Controls.Add(CreateValueLabel("두께"), 4, 0);
        values.Controls.Add(thicknessTextBox, 5, 0);

        container.Controls.Add(titleLabel, 0, 0);
        container.Controls.Add(values, 0, 1);
        panel.Controls.Add(container);
        return panel;
    }

    private static Label CreateValueLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SecondaryTextColor
        };
    }

    private static TextBox CreateSizeTextBox(bool readOnly)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = readOnly,
            TextAlign = HorizontalAlignment.Center,
            BackColor = readOnly
                ? Color.FromArgb(238, 241, 245)
                : Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(2, 4, 6, 4)
        };
    }

    private static DataGridView CreateFileGrid(
        string widthHeader,
        string heightHeader,
        string thicknessHeader,
        bool targetValuesEditable
    )
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            EnableHeadersVisualStyles = false
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 242, 247);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(31, 41, 55);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(
            "Segoe UI",
            9.5F,
            FontStyle.Bold,
            GraphicsUnit.Point
        );
        grid.ColumnHeadersHeight = 38;
        grid.RowTemplate.Height = 34;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 232, 252);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 45, 82);

        grid.Columns.Add(CreateGridColumn("FileName", "파일명", 190F, true));
        grid.Columns.Add(CreateGridColumn("Width", widthHeader, 90F, !targetValuesEditable));
        grid.Columns.Add(CreateGridColumn("Height", heightHeader, 90F, !targetValuesEditable));
        grid.Columns.Add(CreateGridColumn("Thickness", thicknessHeader, 90F, !targetValuesEditable));

        return grid;
    }

    private static DataGridViewTextBoxColumn CreateGridColumn(
        string name,
        string header,
        float fillWeight,
        bool readOnly
    )
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private static void ConfigureTargetFileGridColumns(
        DataGridView grid
    )
    {
        DataGridViewColumn fileNameColumn = GetRequiredGridColumn(
            grid,
            "FileName"
        );
        fileNameColumn.AutoSizeMode =
            DataGridViewAutoSizeColumnMode.Fill;
        fileNameColumn.FillWeight = 100F;
        fileNameColumn.MinimumWidth = 320;

        SetFixedGridColumnWidth(
            GetRequiredGridColumn(grid, "Width"),
            105
        );
        SetFixedGridColumnWidth(
            GetRequiredGridColumn(grid, "Height"),
            105
        );
        SetFixedGridColumnWidth(
            GetRequiredGridColumn(grid, "Thickness"),
            105
        );
        SetFixedGridColumnWidth(
            GetRequiredGridColumn(grid, "ModifiedTime"),
            210
        );
    }

    private static DataGridViewColumn GetRequiredGridColumn(
        DataGridView grid,
        string columnName
    )
    {
        return grid.Columns[columnName]
            ?? throw new InvalidOperationException(
                $"필수 목록 열을 찾지 못했습니다: {columnName}"
            );
    }

    private static void SetFixedGridColumnWidth(
        DataGridViewColumn column,
        int width
    )
    {
        column.AutoSizeMode =
            DataGridViewAutoSizeColumnMode.None;
        column.Width = width;
        column.MinimumWidth = width;
    }

    private static Panel CreateGridPanel(
        string title,
        string description,
        DataGridView grid,
        Button? leftActionButton,
        Button? rightActionButton,
        TextBox? titleSearchTextBox,
        Button? titleActionButton
    )
    {
        bool hasActionButtons =
            leftActionButton != null || rightActionButton != null;

        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            Padding = new Padding(0, 12, 0, 0)
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = hasActionButtons ? 4 : 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // 검색창이 들어가는 제목 행은 TextBox 테두리와 DPI 배율을
        // 고려해 충분한 높이를 준다.
        bool hasTitleTools =
            titleSearchTextBox != null || titleActionButton != null;
        layout.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            hasTitleTools ? 42F : 34F
        ));
        layout.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            hasTitleTools ? 36F : 34F
        ));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Label titleLabel = new()
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 41, 55),
            TextAlign = ContentAlignment.MiddleLeft
        };

        TableLayoutPanel titleLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = titleSearchTextBox == null &&
                          titleActionButton == null
                ? 1
                : 3,
            RowCount = 1,
            Margin = new Padding(0)
        };

        if (titleSearchTextBox == null && titleActionButton == null)
        {
            titleLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F)
            );
        }
        else
        {
            titleLayout.ColumnStyles.Add(
                // "파일 목록"의 마지막 글자가 DPI 배율에서 잘리지 않도록
                // 제목 전용 폭을 넉넉하게 확보한다.
                new ColumnStyle(SizeType.Absolute, 135F)
            );
            titleLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F)
            );
            titleLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 125F)
            );
        }
        titleLayout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100F)
        );
        titleLayout.Controls.Add(titleLabel, 0, 0);

        if (titleSearchTextBox != null)
        {
            titleLayout.Controls.Add(titleSearchTextBox, 1, 0);
        }

        if (titleActionButton != null)
        {
            titleLayout.Controls.Add(titleActionButton, 2, 0);
        }

        Label descriptionLabel = new()
        {
            Text = description,
            Dock = DockStyle.Fill,
            ForeColor = SecondaryTextColor,
            TextAlign = ContentAlignment.TopLeft
        };

        layout.Controls.Add(titleLayout, 0, 0);
        layout.Controls.Add(descriptionLabel, 0, 1);
        layout.Controls.Add(grid, 0, 2);

        if (hasActionButtons)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            Panel buttonHost = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 12, 0, 0)
            };

            TableLayoutPanel buttonLayout = new()
            {
                Dock = DockStyle.Right,
                Width = 460,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            buttonLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 275F)
            );
            buttonLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 185F)
            );
            buttonLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F)
            );

            if (leftActionButton != null)
            {
                leftActionButton.Dock = DockStyle.Fill;
                leftActionButton.Margin = new Padding(0, 0, 8, 0);
                buttonLayout.Controls.Add(leftActionButton, 0, 0);
            }

            if (rightActionButton != null)
            {
                rightActionButton.Dock = DockStyle.Fill;
                rightActionButton.Margin = new Padding(0);
                buttonLayout.Controls.Add(rightActionButton, 1, 0);
            }

            buttonHost.Controls.Add(buttonLayout);
            layout.Controls.Add(buttonHost, 0, 3);
        }

        panel.Controls.Add(layout);
        return panel;
    }

    private static Button CreatePrimaryButton(string text)
    {
        Button button = new()
        {
            Text = text,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = PrimaryColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}

internal sealed class DwgFileSizeSnapshot
{
    public required string FilePath { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double? Thickness { get; init; }
}

internal sealed class DwgConversionRequest
{
    public required string InputPath { get; init; }
    public required string OutputDirectory { get; init; }
    public double TargetWidth { get; init; }
    public double TargetHeight { get; init; }
    public double? TargetThickness { get; init; }
}

internal sealed class UiSavedState
{
    public string? FileListPath { get; init; }
    public string? FileSearchText { get; init; }
    public string? ManualOutputPath { get; init; }
    public bool UseAutomaticOutputPath { get; init; }
    public string? TargetWidth { get; init; }
    public string? TargetHeight { get; init; }
    public string? TargetThickness { get; init; }
    public List<UiSavedFileRow>? InputFiles { get; init; }
    public List<UiSavedFileRow>? ConvertedFiles { get; init; }
    public string? SelectedInputPath { get; init; }
    public string? SelectedConvertedPath { get; init; }
}

internal sealed class UiSavedFileRow
{
    public bool IsFolderHeader { get; init; }
    public bool IsFolderCollapsed { get; init; }
    public string? FolderPath { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Width { get; init; } = string.Empty;
    public string Height { get; init; } = string.Empty;
    public string Thickness { get; init; } = string.Empty;
    public string ModifiedTime { get; init; } = string.Empty;
}

internal sealed class DwgFolderHeader
{
    public required string FolderPath { get; init; }
    public bool IsCollapsed { get; set; }
}
