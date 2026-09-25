namespace Sub_Services.Execute.Excel.Models;

/// <summary>
/// Hằng số ChangeType cho ExcelChangeLog
/// </summary>
public static class ExcelChangeType
{
    /// <summary>Dòng mới xuất hiện trong Snapshot mới nhưng không có trong Snapshot cũ</summary>
    public const string RowAdded = "ROW_ADDED";

    /// <summary>Dòng biến mất khỏi Snapshot mới nhưng có trong Snapshot cũ</summary>
    public const string RowDeleted = "ROW_DELETED";

    /// <summary>Giá trị một cột thay đổi (không phải công thức)</summary>
    public const string ValueChanged = "VALUE_CHANGED";

    /// <summary>Công thức của một cột thay đổi</summary>
    public const string FormulaChanged = "FORMULA_CHANGED";

    /// <summary>Cùng RowKey nhưng GroupName thay đổi (dòng chuyển tổ)</summary>
    public const string GroupChanged = "GROUP_CHANGED";
}

/// <summary>
/// Kết quả so sánh của một sự kiện thay đổi đơn lẻ
/// </summary>
public class ExcelChangeItem
{
    public string SheetName      { get; set; } = string.Empty;
    public int? ExcelRowNumber   { get; set; }
    public string RowKey         { get; set; } = string.Empty;
    public string GroupName      { get; set; } = string.Empty;
    public string ChangeType     { get; set; } = string.Empty;
    public string? ColumnName    { get; set; }
    public string? OldValue      { get; set; }
    public string? NewValue      { get; set; }
    public string? OldFormula    { get; set; }
    public string? NewFormula    { get; set; }
    public string? OldGroupName  { get; set; }
    public string? NewGroupName  { get; set; }
}

/// <summary>
/// Thống kê số dòng của một Group/Tổ
/// </summary>
public class GroupStats
{
    public string GroupName { get; set; } = string.Empty;
    public int OldCount     { get; set; }
    public int NewCount     { get; set; }
    public int Delta        => NewCount - OldCount;
}

/// <summary>
/// Kết quả tổng hợp sau khi Compare một sheet
/// </summary>
public class ExcelCompareResult
{
    public string SheetName         { get; set; } = string.Empty;
    public List<ExcelChangeItem> Changes { get; set; } = new();
    public List<GroupStats> GroupStats   { get; set; } = new();
    public bool HasChanges               => Changes.Count > 0;
}
