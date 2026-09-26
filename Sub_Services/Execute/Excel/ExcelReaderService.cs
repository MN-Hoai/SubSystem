using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Implementation của IExcelReaderService dùng ExcelDataReader.
/// Hỗ trợ: .xlsx, .xlsm, .xlsb (binary Excel).
/// ExcelDataReader là thư viện duy nhất hỗ trợ cả 3 định dạng trên.
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
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ReadInternal(filePath, sheetName, groupColumn, rowKeyColumns), cancellationToken);
    }

    public async Task<bool> CanReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                // Thử mở với FileShare.ReadWrite để không bị lỗi khi Excel đang mở
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return stream.Length > 0;
            }
            catch
            {
                return false;
            }
        }, cancellationToken);
    }

    private List<ExcelSheetData> ReadInternal(
        string filePath,
        string? targetSheetName,
        string groupColumn,
        List<string> rowKeyColumns)
    {
        var result = new List<ExcelSheetData>();

        // Mở file với FileShare.ReadWrite để đọc kể cả khi Excel đang mở
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = CreateReader(filePath, stream);

        if (reader == null)
        {
            _logger.LogWarning("Không thể tạo reader cho file: {FilePath}", filePath);
            return result;
        }

        // Đọc toàn bộ file vào DataSet
        var config = new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = true,
            }
        };

        var dataSet = reader.AsDataSet(config);

        foreach (System.Data.DataTable table in dataSet.Tables)
        {
            // Lọc theo SheetName nếu được chỉ định
            if (targetSheetName != null &&
                !string.Equals(table.TableName, targetSheetName, StringComparison.OrdinalIgnoreCase))
                continue;

            var sheetData = BuildSheetData(table, table.TableName, groupColumn, rowKeyColumns);
            result.Add(sheetData);
        }

        return result;
    }

    private IExcelDataReader? CreateReader(string filePath, Stream stream)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        try
        {
            return ext switch
            {
                // .xls  → BIFF8 (Office 97-2003), dùng CreateBinaryReader
                ".xls"  => ExcelReaderFactory.CreateBinaryReader(stream),

                // .xlsb → BIFF12 / OOXML Binary (Office 2007+), KHÔNG dùng CreateBinaryReader.
                //         CreateReader() tự động nhận diện định dạng, hỗ trợ cả xlsb.
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

    private ExcelSheetData BuildSheetData(
        System.Data.DataTable table,
        string sheetName,
        string groupColumn,
        List<string> rowKeyColumns)
    {
        var sheet = new ExcelSheetData { SheetName = sheetName };

        // Lấy headers
        foreach (System.Data.DataColumn col in table.Columns)
            sheet.Headers.Add(col.ColumnName);

        // Xác định index của GroupColumn và RowKeyColumns
        var groupColIndex   = FindColumnIndex(table, groupColumn);
        var rowKeyIndices   = rowKeyColumns
            .Select(c => (Name: c, Index: FindColumnIndex(table, c)))
            .ToList();

        // Đọc từng dòng (ExcelDataReader UseHeaderRow nên row index bắt đầu từ 2 trong Excel)
        // DataTable row index = 0-based, Excel row = index + 2 (header ở row 1)
        for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
        {
            var dataRow = table.Rows[rowIdx];

            // Bỏ qua dòng trống hoàn toàn
            if (IsEmptyRow(dataRow)) continue;

            var excelRowNumber = rowIdx + 2; // Header ở row 1, data từ row 2

            // Lấy GroupName
            var groupName = groupColIndex >= 0
                ? GetCellValue(dataRow, groupColIndex)
                : string.Empty;

            // Xây dựng RowKey
            var rowKeyParts = rowKeyIndices
                .Select(rk => rk.Index >= 0 ? GetCellValue(dataRow, rk.Index) : string.Empty)
                .ToList();
            var rowKey = string.Join("|", rowKeyParts);

            // Nếu RowKey rỗng hoàn toàn thì bỏ qua dòng
            if (string.IsNullOrWhiteSpace(rowKey.Replace("|", ""))) continue;

            // Thu thập Values
            var values = new Dictionary<string, string>();
            for (int colIdx = 0; colIdx < table.Columns.Count; colIdx++)
            {
                var colName = table.Columns[colIdx].ColumnName;
                values[colName] = GetCellValue(dataRow, colIdx);
            }

            // ExcelDataReader không expose công thức khi dùng AsDataSet với UseHeaderRow
            // Formulas sẽ rỗng (feature limitation của ExcelDataReader)
            var formulas = new Dictionary<string, string>();

            // Tính RowHash từ Values
            var rowHash = ComputeRowHash(values);

            sheet.Rows.Add(new ExcelRowData
            {
                ExcelRowNumber = excelRowNumber,
                RowKey         = rowKey,
                GroupName      = groupName,
                Values         = values,
                Formulas       = formulas,
                RowHash        = rowHash,
            });
        }

        return sheet;
    }

    private static int FindColumnIndex(System.Data.DataTable table, string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return -1;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (string.Equals(table.Columns[i].ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string GetCellValue(System.Data.DataRow row, int colIndex)
    {
        var val = row[colIndex];
        return val == null || val == DBNull.Value ? string.Empty : val.ToString()?.Trim() ?? string.Empty;
    }

    private static bool IsEmptyRow(System.Data.DataRow row)
    {
        foreach (var item in row.ItemArray)
        {
            if (item != null && item != DBNull.Value && !string.IsNullOrWhiteSpace(item.ToString()))
                return false;
        }
        return true;
    }

    private static string ComputeRowHash(Dictionary<string, string> values)
    {
        // Serialize theo thứ tự key cố định để hash nhất quán
        var ordered = values.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}");
        var raw     = string.Join(";", ordered);
        var bytes   = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLower();
    }
}
