using System.Security.Cryptography;
using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Implementation của IExcelReaderService dùng ExcelDataReader.
/// Hỗ trợ: .xlsx, .xlsm, .xlsb (binary Excel).
///
/// Đọc theo cơ chế raw reader.Read() — KHÔNG dùng AsDataSet/FilterRow —
/// để kiểm soát chính xác dòng nào là header khi file có title row phía trên.
/// </summary>
public class ExcelReaderService : IExcelReaderService
{
    private readonly ILogger<ExcelReaderService> _logger;

    public ExcelReaderService(ILogger<ExcelReaderService> logger)
    {
        _logger = logger;
        // BẮT BUỘC: ExcelDataReader yêu cầu đăng ký encoding 1252
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<List<ExcelSheetData>> ReadAsync(
        string filePath,
        string? sheetName,
        string groupColumn,
        List<string> rowKeyColumns,
        int headerRowIndex = 0,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () => ReadInternal(filePath, sheetName, groupColumn, rowKeyColumns, headerRowIndex),
            cancellationToken);
    }

    public async Task<bool> CanReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return stream.Length > 0;
            }
            catch { return false; }
        }, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Core: duyệt từng sheet bằng raw reader.Read()
    // ──────────────────────────────────────────────────────────────────────
    private List<ExcelSheetData> ReadInternal(
        string filePath,
        string? targetSheetName,
        string groupColumn,
        List<string> rowKeyColumns,
        int headerRowIndex = 0)
    {
        var result = new List<ExcelSheetData>();

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = CreateReader(filePath, stream);

        if (reader == null)
        {
            _logger.LogWarning("Không thể tạo reader cho file: {FilePath}", filePath);
            return result;
        }

        // Duyệt qua từng sheet
        do
        {
            var sheetName = reader.Name;

            if (targetSheetName != null &&
                !string.Equals(sheetName, targetSheetName, StringComparison.OrdinalIgnoreCase))
                continue;

            var sheetData = ReadSheet(reader, sheetName, groupColumn, rowKeyColumns, headerRowIndex);
            result.Add(sheetData);

        } while (reader.NextResult());

        return result;
    }

    /// <summary>
    /// Đọc một sheet bằng raw reader.Read().
    ///
    /// Logic dòng (rowIndex là 0-based, đếm MỌI dòng thô):
    ///   rowIndex  &lt; headerRowIndex  → bỏ qua (title/tiêu đề phụ)
    ///   rowIndex == headerRowIndex  → đọc làm tên cột (header)
    ///   rowIndex  &gt; headerRowIndex  → dữ liệu
    ///
    /// ExcelRowNumber (1-based) = rowIndex + 1
    /// </summary>
    private ExcelSheetData ReadSheet(
        IExcelDataReader reader,
        string sheetName,
        string groupColumn,
        List<string> rowKeyColumns,
        int headerRowIndex)
    {
        var sheet   = new ExcelSheetData { SheetName = sheetName };
        var headers = new List<string>();
        int rowIndex = 0; // 0-based, đếm MỌI dòng thô trong sheet

        while (reader.Read())
        {
            // ── Bỏ qua các dòng title trước header ──────────────────────
            if (rowIndex < headerRowIndex)
            {
                rowIndex++;
                continue;
            }

            // ── Dòng header: đọc tên cột ─────────────────────────────────
            if (rowIndex == headerRowIndex)
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val     = reader.GetValue(i);
                    var colName = val?.ToString()?.Trim();
                    // Ô header rỗng → đặt placeholder để không mất cột
                    headers.Add(string.IsNullOrWhiteSpace(colName) ? $"_Col{i}" : colName);
                }
                sheet.Headers.AddRange(headers);
                rowIndex++;
                continue;
            }

            // ── Dòng dữ liệu (rowIndex > headerRowIndex) ─────────────────
            if (headers.Count == 0) { rowIndex++; continue; }

            // Đọc giá trị từng ô
            var values  = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
            bool isEmpty = true;
            for (int i = 0; i < headers.Count; i++)
            {
                var val = i < reader.FieldCount ? reader.GetValue(i) : null;
                var str = val is null or DBNull ? string.Empty : val.ToString()?.Trim() ?? string.Empty;
                values[headers[i]] = str;
                if (!string.IsNullOrWhiteSpace(str)) isEmpty = false;
            }

            // Bỏ qua dòng hoàn toàn trống
            if (isEmpty) { rowIndex++; continue; }

            // ExcelRowNumber: rowIndex 0-based → Excel row 1-based = rowIndex + 1
            var excelRowNumber = rowIndex + 1;

            // GroupName
            var groupName = values.TryGetValue(groupColumn, out var gv) ? gv : string.Empty;

            // RowKey — ghép các cột theo thứ tự cấu hình
            var rowKeyParts = rowKeyColumns
                .Select(c => values.TryGetValue(c, out var rv) ? rv : string.Empty)
                .ToList();
            var rowKey = string.Join("|", rowKeyParts);

            // Bỏ qua dòng không có RowKey (dòng tên tổ gộp, dòng tổng, v.v.)
            if (string.IsNullOrWhiteSpace(rowKey.Replace("|", ""))) { rowIndex++; continue; }

            sheet.Rows.Add(new ExcelRowData
            {
                ExcelRowNumber = excelRowNumber,
                RowKey         = rowKey,
                GroupName      = groupName,
                Values         = values,
                Formulas       = new Dictionary<string, string>(), // ExcelDataReader không expose formula
                RowHash        = ComputeRowHash(values),
            });

            rowIndex++;
        }

        return sheet;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tạo reader phù hợp theo định dạng file
    // ──────────────────────────────────────────────────────────────────────
    private IExcelDataReader? CreateReader(string filePath, Stream stream)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        try
        {
            return ext switch
            {
                // .xls  → BIFF8 (Office 97-2003)
                ".xls"  => ExcelReaderFactory.CreateBinaryReader(stream),

                // .xlsb → BIFF12 / OOXML Binary (Office 2007+)
                //         CreateReader() auto-detect, hỗ trợ xlsb
                ".xlsb" => ExcelReaderFactory.CreateReader(stream),

                // .xlsx, .xlsm → OOXML (zip-based XML)
                _       => ExcelReaderFactory.CreateOpenXmlReader(stream),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tạo Excel reader cho file {FilePath}", filePath);
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Hash row để so sánh snapshot
    // ──────────────────────────────────────────────────────────────────────
    private static string ComputeRowHash(Dictionary<string, string> values)
    {
        var ordered = values.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}");
        var raw     = string.Join(";", ordered);
        var bytes   = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLower();
    }
}
