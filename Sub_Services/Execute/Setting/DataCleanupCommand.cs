using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // ─── Danh sách bảng có soft-delete, theo thứ tự dọn (child trước parent) ───
        private static readonly List<CleanupTableInfo> _cleanupTables = new()
        {
            new("DailyOutputDetail",          "Chi tiết sản lượng ngày",   "fa-list-check",      1),
            new("DailyOutput",                "Sản lượng ngày",             "fa-chart-bar",       2),
            new("ProductLayoutItem",          "Item layout sản xuất",       "fa-th-large",        3),
            new("StyleDetail",                "Công đoạn mã hàng",          "fa-layer-group",     4),
            new("StyleInfo",                  "Thông tin mã hàng",          "fa-tag",             5),
            new("ProductionInfo",             "Mã sản xuất",                "fa-industry",        6),
            new("ProductionMachine",          "Máy sản xuất",               "fa-gear",            7),
            new("ProductionDepartment",       "Bộ phận sản xuất",           "fa-building",        8),
            new("Users",                      "Người dùng",                 "fa-users",           9),
            new("Roles",                      "Vai trò",                    "fa-shield-halved",   10),
        };

        /// <summary>Trả về danh sách bảng kèm số lượng soft-deleted (Status = -2).</summary>
        public async Task<List<CleanupTableSummary>> GetCleanupTableSummariesAsync()
        {
            var result = new List<CleanupTableSummary>();
            var conn = _context.Database.GetConnectionString();

            await using var sqlConn = new SqlConnection(conn);
            await sqlConn.OpenAsync();

            foreach (var t in _cleanupTables)
            {
                int count = 0;
                try
                {
                    // Kiểm tra bảng tồn tại
                    var checkSql = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tbl";
                    await using var chkCmd = new SqlCommand(checkSql, sqlConn);
                    chkCmd.Parameters.AddWithValue("@tbl", t.TableName);
                    var exists = (int)await chkCmd.ExecuteScalarAsync();
                    if (exists == 0) continue;

                    // Kiểm tra cột Status tồn tại
                    var colSql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME=@tbl AND COLUMN_NAME='Status'";
                    await using var colCmd = new SqlCommand(colSql, sqlConn);
                    colCmd.Parameters.AddWithValue("@tbl", t.TableName);
                    var hasStatus = (int)await colCmd.ExecuteScalarAsync();
                    if (hasStatus == 0) continue;

                    var cntSql = $"SELECT COUNT(*) FROM [{t.TableName}] WHERE [Status] = -2";
                    await using var cntCmd = new SqlCommand(cntSql, sqlConn);
                    count = (int)await cntCmd.ExecuteScalarAsync();
                }
                catch { /* bỏ qua nếu lỗi */ }

                result.Add(new CleanupTableSummary
                {
                    TableName    = t.TableName,
                    DisplayName  = t.DisplayName,
                    Icon         = t.Icon,
                    Order        = t.Order,
                    DeletedCount = count
                });
            }
            return result;
        }

        /// <summary>
        /// Lấy danh sách dữ liệu đã xóa mềm của một bảng, phân trang.
        /// Trả về dynamic: mỗi row là Dictionary&lt;string, object&gt;.
        /// </summary>
        public async Task<CleanupPagedResult> GetDeletedRowsAsync(string tableName, int page, int pageSize)
        {
            // Whitelist để tránh SQL injection
            var allowed = _cleanupTables.Select(t => t.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!allowed.Contains(tableName))
                return new CleanupPagedResult();

            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 200);
            int skip = (page - 1) * pageSize;

            var conn = _context.Database.GetConnectionString();
            await using var sqlConn = new SqlConnection(conn);
            await sqlConn.OpenAsync();

            // Lấy tổng số
            int total = 0;
            {
                await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM [{tableName}] WHERE [Status] = -2", sqlConn);
                total = (int)await cmd.ExecuteScalarAsync();
            }

            // Lấy dữ liệu phân trang, sắp xếp theo UpdateDate DESC
            var rows = new List<Dictionary<string, object>>();
            List<string> columns = new();

            var sql = $@"
                SELECT * FROM (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY [UpdateDate] DESC) AS __rn
                    FROM [{tableName}]
                    WHERE [Status] = -2
                ) AS __t
                WHERE __rn > {skip} AND __rn <= {skip + pageSize}";

            await using var dataCmd = new SqlCommand(sql, sqlConn);
            await using var reader  = await dataCmd.ExecuteReaderAsync();

            // Lấy tên cột (bỏ __rn)
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (name != "__rn") columns.Add(name);
            }

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                foreach (var col in columns)
                {
                    var val = reader[col];
                    row[col] = val == DBNull.Value ? null : val;
                }
                rows.Add(row);
            }

            return new CleanupPagedResult
            {
                TableName  = tableName,
                Page       = page,
                PageSize   = pageSize,
                Total      = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                Columns    = columns,
                Rows       = rows
            };
        }

        /// <summary>Khôi phục các row (Status = -2 → 1).</summary>
        public async Task<(int Restored, string Message)> RestoreRowsAsync(string tableName, List<string> ids)
        {
            var allowed = _cleanupTables.Select(t => t.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!allowed.Contains(tableName) || ids == null || ids.Count == 0)
                return (0, "Không có dữ liệu hợp lệ.");

            // Validate GUID format
            var guids = ids.Where(id => Guid.TryParse(id, out _)).ToList();
            if (guids.Count == 0) return (0, "ID không hợp lệ.");

            var inClause = string.Join(",", guids.Select(id => $"'{id}'"));
            var sql = $"UPDATE [{tableName}] SET [Status]=1, [UpdateDate]=GETDATE() WHERE [ID] IN ({inClause}) AND [Status]=-2";

            var conn = _context.Database.GetConnectionString();
            await using var sqlConn = new SqlConnection(conn);
            await sqlConn.OpenAsync();
            await using var cmd = new SqlCommand(sql, sqlConn);
            int affected = await cmd.ExecuteNonQueryAsync();
            return (affected, $"Đã khôi phục {affected} bản ghi.");
        }

        /// <summary>Xóa cứng các row (DELETE thực sự).</summary>
        public async Task<(int Deleted, string Message)> HardDeleteRowsAsync(string tableName, List<string> ids)
        {
            var allowed = _cleanupTables.Select(t => t.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!allowed.Contains(tableName) || ids == null || ids.Count == 0)
                return (0, "Không có dữ liệu hợp lệ.");

            var guids = ids.Where(id => Guid.TryParse(id, out _)).ToList();
            if (guids.Count == 0) return (0, "ID không hợp lệ.");

            var inClause = string.Join(",", guids.Select(id => $"'{id}'"));
            var sql = $"DELETE FROM [{tableName}] WHERE [ID] IN ({inClause}) AND [Status]=-2";

            var conn = _context.Database.GetConnectionString();
            await using var sqlConn = new SqlConnection(conn);
            await sqlConn.OpenAsync();
            await using var cmd = new SqlCommand(sql, sqlConn);
            int affected = await cmd.ExecuteNonQueryAsync();
            return (affected, $"Đã xóa vĩnh viễn {affected} bản ghi.");
        }

        /// <summary>Xóa cứng toàn bộ soft-deleted của một bảng.</summary>
        public async Task<(int Deleted, string Message)> PurgeTableAsync(string tableName)
        {
            var allowed = _cleanupTables.Select(t => t.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!allowed.Contains(tableName))
                return (0, "Bảng không hợp lệ.");

            var sql = $"DELETE FROM [{tableName}] WHERE [Status] = -2";

            var conn = _context.Database.GetConnectionString();
            await using var sqlConn = new SqlConnection(conn);
            await sqlConn.OpenAsync();
            await using var cmd = new SqlCommand(sql, sqlConn);
            int affected = await cmd.ExecuteNonQueryAsync();
            return (affected, $"Đã xóa vĩnh viễn {affected} bản ghi khỏi [{tableName}].");
        }
    }

    // ─── DTOs ─────────────────────────────────────────────────────────────────

    public record CleanupTableInfo(string TableName, string DisplayName, string Icon, int Order);

    public class CleanupTableSummary
    {
        public string TableName   { get; set; }
        public string DisplayName { get; set; }
        public string Icon        { get; set; }
        public int    Order       { get; set; }
        public int    DeletedCount { get; set; }
    }

    public class CleanupPagedResult
    {
        public string               TableName  { get; set; }
        public int                  Page       { get; set; }
        public int                  PageSize   { get; set; }
        public int                  Total      { get; set; }
        public int                  TotalPages { get; set; }
        public List<string>         Columns    { get; set; } = new();
        public List<Dictionary<string, object>> Rows { get; set; } = new();
    }
}
