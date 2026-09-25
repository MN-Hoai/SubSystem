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

        // ── Bước 1: Build lookup bằng Unique Key ──────────────────────────────────
        // Cùng một RowKey có thể nằm ở nhiều Tổ khác nhau, nên GroupName + RowKey mới là định danh duy nhất.
        string GetUniqueKey(string? group, string key) => $"{group ?? ""}||{key}";

        var oldDict = oldSnapshot.Values
            .GroupBy(r => GetUniqueKey(r.GroupName, r.RowKey), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var newDict = newRows
            .Where(r => !string.IsNullOrEmpty(r.RowKey))
            .GroupBy(r => GetUniqueKey(r.GroupName, r.RowKey), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        // ── Bước 2: Thống kê GroupStats ───────────────────────────────────────────
        result.GroupStats = ComputeGroupStats(oldDict.Values, newDict.Values);

        // ── Bước 3: Phát hiện ROW_ADDED ───────────────────────────────────────────
        foreach (var (uniqueKey, newRow) in newDict)
        {
            if (oldDict.ContainsKey(uniqueKey)) continue; // đã tồn tại → không phải mới

            result.Changes.Add(new ExcelChangeItem
            {
                SheetName      = sheetName,
                ExcelRowNumber = newRow.ExcelRowNumber,
                RowKey         = newRow.RowKey,
                GroupName      = newRow.GroupName,
                ChangeType     = ExcelChangeType.RowAdded,
                NewValue       = System.Text.Json.JsonSerializer.Serialize(newRow.Values),
            });
            _logger.LogDebug("[ROW_ADDED] {RowKey} Group={Group}", newRow.RowKey, newRow.GroupName);
        }

        // ── Bước 4: Phát hiện ROW_DELETED ─────────────────────────────────────────
        foreach (var (uniqueKey, oldRow) in oldDict)
        {
            if (newDict.ContainsKey(uniqueKey)) continue; // vẫn còn → không phải xóa

            var oldData = ExcelSnapshotService.ParseDataJson(oldRow.DataJson);
            result.Changes.Add(new ExcelChangeItem
            {
                SheetName      = sheetName,
                ExcelRowNumber = oldRow.ExcelRowNumber,
                RowKey         = oldRow.RowKey,
                GroupName      = oldRow.GroupName ?? string.Empty,
                ChangeType     = ExcelChangeType.RowDeleted,
                OldValue       = oldRow.DataJson,
            });
            _logger.LogDebug("[ROW_DELETED] {RowKey} Group={Group}", oldRow.RowKey, oldRow.GroupName);
        }

        // ── Bước 5 & 6: VALUE_CHANGED và FORMULA_CHANGED ──────────────────────────
        foreach (var (uniqueKey, newRow) in newDict)
        {
            if (!oldDict.TryGetValue(uniqueKey, out var oldRow)) continue;

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
                        RowKey         = newRow.RowKey,
                        GroupName      = newRow.GroupName,
                        ChangeType     = ExcelChangeType.FormulaChanged,
                        ColumnName     = colName,
                        OldValue       = oldVal,
                        NewValue       = newVal,
                        OldFormula     = oldFormula,
                        NewFormula     = newFormula,
                    });
                    _logger.LogDebug("[FORMULA_CHANGED] {RowKey}.{Col}: {Old} → {New}", newRow.RowKey, colName, oldFormula, newFormula);
                }
                else if (valueChanged)
                {
                    result.Changes.Add(new ExcelChangeItem
                    {
                        SheetName      = sheetName,
                        ExcelRowNumber = newRow.ExcelRowNumber,
                        RowKey         = newRow.RowKey,
                        GroupName      = newRow.GroupName,
                        ChangeType     = ExcelChangeType.ValueChanged,
                        ColumnName     = colName,
                        OldValue       = oldVal,
                        NewValue       = newVal,
                    });
                    _logger.LogDebug("[VALUE_CHANGED] {RowKey}.{Col}: {Old} → {New}", newRow.RowKey, colName, oldVal, newVal);
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

        // ── Bước 8: Phát hiện GROUP_CHANGED ───────────────────────────────
        // Chuyển tổ xảy ra khi một mã cũ bị ROW_DELETED ở tổ cũ và được ROW_ADDED ở tổ mới (CÙNG RowKey)
        var deletedByRowKey = result.Changes
            .Where(c => c.ChangeType == ExcelChangeType.RowDeleted && !toRemove.Contains(c) && !string.IsNullOrEmpty(c.RowKey))
            .GroupBy(c => c.RowKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var addedByRowKey = result.Changes
            .Where(c => c.ChangeType == ExcelChangeType.RowAdded && !toRemove.Contains(c) && !string.IsNullOrEmpty(c.RowKey))
            .GroupBy(c => c.RowKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var groupChangedItems = new List<ExcelChangeItem>();

        foreach (var (rk, deletedItem) in deletedByRowKey)
        {
            if (!addedByRowKey.TryGetValue(rk, out var addedItem)) continue;

            // Cùng RowKey nhưng khác GroupName → Chuyển tổ
            if (string.Equals(deletedItem.GroupName, addedItem.GroupName, StringComparison.Ordinal)) continue;

            groupChangedItems.Add(new ExcelChangeItem
            {
                SheetName      = sheetName,
                ExcelRowNumber = addedItem.ExcelRowNumber,
                RowKey         = rk,
                GroupName      = addedItem.GroupName,
                ChangeType     = ExcelChangeType.GroupChanged,
                OldGroupName   = deletedItem.GroupName,
                NewGroupName   = addedItem.GroupName,
                OldValue       = deletedItem.OldValue,
                NewValue       = addedItem.NewValue,
            });

            toRemove.Add(deletedItem);
            toRemove.Add(addedItem);

            _logger.LogDebug("[GROUP_CHANGED] {RowKey}: {OldGroup} → {NewGroup}", rk, deletedItem.GroupName, addedItem.GroupName);
        }

        // Xóa ROW_DELETED / ROW_ADDED đã được gộp, thêm ROWKEY_CHANGED và GROUP_CHANGED
        result.Changes.RemoveAll(c => toRemove.Contains(c));
        result.Changes.AddRange(rowKeyChangedItems);
        result.Changes.AddRange(groupChangedItems);

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
        IEnumerable<ExcelSnapshotRow> oldSnapshotValues,
        IEnumerable<ExcelRowData> newDictValues)
    {
        var oldGroups = oldSnapshotValues
            .GroupBy(r => r.GroupName ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var newGroups = newDictValues
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
