using Microsoft.Extensions.Logging;
using Sub_Entities.Entities;
using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Compare Engine: so sánh snapshot cũ vs dữ liệu mới theo thứ tự:
/// 1. Group theo Tổ → thống kê số dòng
/// 2. GROUP_CHANGED (cùng RowKey, khác Group)
/// 3. ROW_ADDED (RowKey mới)
/// 4. ROW_DELETED (RowKey biến mất)
/// 5. VALUE_CHANGED (cùng RowKey, cùng Group, khác giá trị cột)
/// 6. FORMULA_CHANGED (công thức thay đổi)
/// </summary>
public class ExcelCompareService : IExcelCompareService
{
    private readonly ILogger<ExcelCompareService> _logger;

    public ExcelCompareService(ILogger<ExcelCompareService> logger)
    {
        _logger = logger;
    }

    public ExcelCompareResult Compare(
        string sheetName,
        Dictionary<string, ExcelSnapshotRow> oldSnapshot,
        List<ExcelRowData> newRows)
    {
        var result = new ExcelCompareResult { SheetName = sheetName };

        // ── Bước 1: Build lookup cho newRows ──────────────────────────────
        // Dùng GroupBy để tránh lỗi ArgumentException nếu Excel có nhiều dòng trùng RowKey
        var newDict = newRows
            .Where(r => !string.IsNullOrEmpty(r.RowKey))
            .GroupBy(r => r.RowKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        // ── Bước 2: Thống kê GroupStats ───────────────────────────────────
        result.GroupStats = ComputeGroupStats(oldSnapshot, newDict);

        // ── Bước 3: Phát hiện GROUP_CHANGED ───────────────────────────────
        // Dòng có RowKey tồn tại ở cả 2 nhưng GroupName khác nhau
        var groupChangedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rowKey, newRow) in newDict)
        {
            if (!oldSnapshot.TryGetValue(rowKey, out var oldRow)) continue;

            var oldGroup = oldRow.GroupName ?? string.Empty;
            var newGroup = newRow.GroupName ?? string.Empty;

            if (!string.Equals(oldGroup, newGroup, StringComparison.Ordinal))
            {
                groupChangedKeys.Add(rowKey);
                result.Changes.Add(new ExcelChangeItem
                {
                    SheetName      = sheetName,
                    ExcelRowNumber = newRow.ExcelRowNumber,
                    RowKey         = rowKey,
                    GroupName      = newGroup,
                    ChangeType     = ExcelChangeType.GroupChanged,
                    OldGroupName   = oldGroup,
                    NewGroupName   = newGroup,
                });
                _logger.LogDebug("[GROUP_CHANGED] {RowKey}: {OldGroup} → {NewGroup}", rowKey, oldGroup, newGroup);
            }
        }

        // ── Bước 4: Phát hiện ROW_ADDED ───────────────────────────────────
        foreach (var (rowKey, newRow) in newDict)
        {
            if (oldSnapshot.ContainsKey(rowKey)) continue; // đã tồn tại → không phải mới

            result.Changes.Add(new ExcelChangeItem
            {
                SheetName      = sheetName,
                ExcelRowNumber = newRow.ExcelRowNumber,
                RowKey         = rowKey,
                GroupName      = newRow.GroupName,
                ChangeType     = ExcelChangeType.RowAdded,
                NewValue       = System.Text.Json.JsonSerializer.Serialize(newRow.Values),
            });
            _logger.LogDebug("[ROW_ADDED] {RowKey} Group={Group}", rowKey, newRow.GroupName);
        }

        // ── Bước 5: Phát hiện ROW_DELETED ─────────────────────────────────
        foreach (var (rowKey, oldRow) in oldSnapshot)
        {
            if (newDict.ContainsKey(rowKey)) continue; // vẫn còn → không phải xóa

            var oldData = ExcelSnapshotService.ParseDataJson(oldRow.DataJson);
            result.Changes.Add(new ExcelChangeItem
            {
                SheetName      = sheetName,
                ExcelRowNumber = oldRow.ExcelRowNumber,
                RowKey         = rowKey,
                GroupName      = oldRow.GroupName ?? string.Empty,
                ChangeType     = ExcelChangeType.RowDeleted,
                OldValue       = oldRow.DataJson,
            });
            _logger.LogDebug("[ROW_DELETED] {RowKey} Group={Group}", rowKey, oldRow.GroupName);
        }

        // ── Bước 6 & 7: VALUE_CHANGED và FORMULA_CHANGED ──────────────────
        foreach (var (rowKey, newRow) in newDict)
        {
            if (!oldSnapshot.TryGetValue(rowKey, out var oldRow)) continue;
            // Nếu GROUP_CHANGED thì vẫn kiểm tra value changes trong cùng dòng đó
            // (không bỏ qua vì dòng chuyển tổ có thể đồng thời thay đổi giá trị)

            // Quick check bằng RowHash — nếu giống nhau thì skip
            if (!string.IsNullOrEmpty(oldRow.RowHash) &&
                string.Equals(oldRow.RowHash, newRow.RowHash, StringComparison.Ordinal))
                continue;

            var oldValues   = ExcelSnapshotService.ParseDataJson(oldRow.DataJson);
            var newValues   = newRow.Values;
            var oldFormulas = ExcelSnapshotService.ParseFormulaJson(oldRow.FormulaJson);
            var newFormulas = newRow.Formulas;

            // Kiểm tra từng cột của newValues
            var allColumns = oldValues.Keys.Union(newValues.Keys).Distinct();
            foreach (var colName in allColumns)
            {
                oldValues.TryGetValue(colName, out var oldVal);
                newValues.TryGetValue(colName, out var newVal);
                oldFormulas.TryGetValue(colName, out var oldFormula);
                newFormulas.TryGetValue(colName, out var newFormula);

                bool valueChanged   = !string.Equals(oldVal   ?? "", newVal   ?? "", StringComparison.Ordinal);
                bool formulaChanged = !string.Equals(oldFormula ?? "", newFormula ?? "", StringComparison.Ordinal);

                if (formulaChanged && (oldFormula != null || newFormula != null))
                {
                    result.Changes.Add(new ExcelChangeItem
                    {
                        SheetName      = sheetName,
                        ExcelRowNumber = newRow.ExcelRowNumber,
                        RowKey         = rowKey,
                        GroupName      = newRow.GroupName,
                        ChangeType     = ExcelChangeType.FormulaChanged,
                        ColumnName     = colName,
                        OldValue       = oldVal,
                        NewValue       = newVal,
                        OldFormula     = oldFormula,
                        NewFormula     = newFormula,
                    });
                    _logger.LogDebug("[FORMULA_CHANGED] {RowKey}.{Col}: {Old} → {New}", rowKey, colName, oldFormula, newFormula);
                }
                else if (valueChanged)
                {
                    result.Changes.Add(new ExcelChangeItem
                    {
                        SheetName      = sheetName,
                        ExcelRowNumber = newRow.ExcelRowNumber,
                        RowKey         = rowKey,
                        GroupName      = newRow.GroupName,
                        ChangeType     = ExcelChangeType.ValueChanged,
                        ColumnName     = colName,
                        OldValue       = oldVal,
                        NewValue       = newVal,
                    });
                    _logger.LogDebug("[VALUE_CHANGED] {RowKey}.{Col}: {Old} → {New}", rowKey, colName, oldVal, newVal);
                }
            }
        }

        // ── Bước 7: Phát hiện ROWKEY_CHANGED ──────────────────────────────
        // Nếu một dòng bị ROW_DELETED và một dòng mới ROW_ADDED có cùng ExcelRowNumber
        // → đó là RowKey thay đổi, không phải dòng mới/xóa thật sự
        var deletedByRow = result.Changes
            .Where(c => c.ChangeType == ExcelChangeType.RowDeleted && c.ExcelRowNumber.HasValue)
            .GroupBy(c => c.ExcelRowNumber!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var addedByRow = result.Changes
            .Where(c => c.ChangeType == ExcelChangeType.RowAdded && c.ExcelRowNumber.HasValue)
            .GroupBy(c => c.ExcelRowNumber!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var rowKeyChangedItems = new List<ExcelChangeItem>();
        var toRemove          = new HashSet<ExcelChangeItem>(ReferenceEqualityComparer.Instance);

        foreach (var (rowNum, deletedItem) in deletedByRow)
        {
            if (!addedByRow.TryGetValue(rowNum, out var addedItem)) continue;

            // Cùng ExcelRowNumber nhưng RowKey khác → RowKey đã bị chỉnh sửa
            if (string.Equals(deletedItem.RowKey, addedItem.RowKey, StringComparison.Ordinal)) continue;

            rowKeyChangedItems.Add(new ExcelChangeItem
            {
                SheetName      = sheetName,
                ExcelRowNumber = rowNum,
                RowKey         = addedItem.RowKey,       // RowKey hiện tại (mới)
                GroupName      = addedItem.GroupName,
                ChangeType     = ExcelChangeType.RowKeyChanged,
                OldRowKey      = deletedItem.RowKey,
                NewRowKey      = addedItem.RowKey,
                OldValue       = deletedItem.OldValue,   // giữ data cũ để tham khảo
                NewValue       = addedItem.NewValue,
            });

            toRemove.Add(deletedItem);
            toRemove.Add(addedItem);

            _logger.LogDebug("[ROWKEY_CHANGED] Row#{Row}: {OldKey} → {NewKey}",
                rowNum, deletedItem.RowKey, addedItem.RowKey);
        }

        // Xóa ROW_DELETED / ROW_ADDED đã được gộp, thêm ROWKEY_CHANGED
        result.Changes.RemoveAll(c => toRemove.Contains(c));
        result.Changes.AddRange(rowKeyChangedItems);

        _logger.LogInformation("Compare [{Sheet}]: {Total} thay đổi ({Added} added, {Deleted} deleted, {RkChg} rowkey, {Group} group, {Value} value)",
            sheetName,
            result.Changes.Count,
            result.Changes.Count(c => c.ChangeType == ExcelChangeType.RowAdded),
            result.Changes.Count(c => c.ChangeType == ExcelChangeType.RowDeleted),
            result.Changes.Count(c => c.ChangeType == ExcelChangeType.RowKeyChanged),
            result.Changes.Count(c => c.ChangeType == ExcelChangeType.GroupChanged),
            result.Changes.Count(c => c.ChangeType == ExcelChangeType.ValueChanged));

        return result;
    }

    private static List<GroupStats> ComputeGroupStats(
        Dictionary<string, ExcelSnapshotRow> oldSnapshot,
        Dictionary<string, ExcelRowData> newDict)
    {
        var oldGroups = oldSnapshot.Values
            .GroupBy(r => r.GroupName ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var newGroups = newDict.Values
            .GroupBy(r => r.GroupName ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var allGroupNames = oldGroups.Keys.Union(newGroups.Keys).Distinct().OrderBy(g => g);

        return allGroupNames.Select(g => new GroupStats
        {
            GroupName = g,
            OldCount  = oldGroups.TryGetValue(g, out var o) ? o : 0,
            NewCount  = newGroups.TryGetValue(g, out var n) ? n : 0,
        }).ToList();
    }
}
