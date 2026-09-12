using System;
using System.Collections.Generic;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  DTOs — ProductionDepartment / MachineDetail
        // =====================================================================

        /// <summary>Mã sản xuất (ProductionInfo) gắn với một máy qua DailyOutput.</summary>
        public class MachineDetail_ProductionInfoDto
        {
            public Guid   Id                 { get; set; }
            public string Style              { get; set; }
            public string Spmain             { get; set; }
            public string Line               { get; set; }
            public string Color              { get; set; }
            public int?   TotalQty           { get; set; }
            public int?   FinishQty          { get; set; }
            public int?   Target             { get; set; }
            public string InlineLine         { get; set; }   // DateOnly → string "yyyy-MM-dd"
            public string InlineDepartment   { get; set; }
            public string Remark             { get; set; }
            public string Keyword            { get; set; }
            public int    Status             { get; set; }
            public string CreateDate         { get; set; }
            public int    TotalOutputNumber  { get; set; }   // tổng sản lượng toàn kỳ (min-based)
            public int    TodayOutput        { get; set; }   // sản lượng chỉ ngày hôm nay (min-based)
        }

        /// <summary>Sản lượng một ngày (DailyOutput) của một mã trên máy.</summary>
        public class MachineDetail_DailyOutputDto
        {
            public Guid   Id                { get; set; }
            public string StyleCode         { get; set; }   // StyleInfo.StyleCode
            public string StyleInfoId       { get; set; }
            public int?   TotalOutputNumber { get; set; }
            public string CreateDate        { get; set; }
            public List<MachineDetail_DailyOutputDetailDto> Details { get; set; } = new();
        }

        /// <summary>Chi tiết công đoạn trong một DailyOutput.</summary>
        public class MachineDetail_DailyOutputDetailDto
        {
            public Guid   Id            { get; set; }
            public Guid?  StyleDetailId { get; set; }
            public string DetailName    { get; set; }    // StyleDetail.DetailName
            public int?   OutputNumber  { get; set; }
            public string InputTime     { get; set; }    // TimeOnly → "HH:mm"
        }

        /// <summary>Style để pick khi thêm mã sản xuất.</summary>
        public class MachineDetail_StylePickerItem
        {
            public Guid   Id          { get; set; }
            public string StyleCode   { get; set; }
            public string Keyword     { get; set; }
            public int    DetailCount { get; set; }
        }

        /// <summary>Request thêm/gắn mã sản xuất vào máy.</summary>
        public class MachineDetail_AssignRequest
        {
            public Guid?  ProductionInfoId    { get; set; }   // null = tạo mới
            public string Style               { get; set; }
            public string Spmain              { get; set; }
            public string Line                { get; set; }
            public string Color               { get; set; }
            public int?   TotalQty            { get; set; }
            public int?   Target              { get; set; }
            public string InlineLine          { get; set; }
            public string InlineDepartment    { get; set; }
            public string Remark              { get; set; }
            public Guid   ProductionDeptId    { get; set; }
            public Guid   MachineId           { get; set; }
            public Guid?  StyleInfoId         { get; set; }
        }

        // =====================================================================
        //  DTOs — RecordOutput (Ghi sản lượng)
        // =====================================================================

        /// <summary>Máy để hiển thị trong danh sách chọn máy.</summary>
        public class RecordOutput_MachineDto
        {
            public Guid   Id          { get; set; }
            public string MachineName { get; set; }
            public string Remark      { get; set; }   // mã máy
            public int    Status      { get; set; }
            public string DeptName    { get; set; }
        }

        /// <summary>Mã sản xuất hiển thị ở cột phải khi chọn máy.</summary>
        public class RecordOutput_ProductionCodeDto
        {
            public Guid   ProductionInfoId { get; set; }
            public Guid   MachineId        { get; set; }
            public string Style            { get; set; }
            public string Spmain           { get; set; }
            public string Color            { get; set; }
            public string Line             { get; set; }
            public int    Status           { get; set; }
            public int?   Target           { get; set; }
            public int?   TodayOutput      { get; set; }   // sản lượng hôm nay (sum các lần nhập)
        }

        /// <summary>Chi tiết công đoạn (StyleDetail) của một mã + sản lượng đã nhập hôm nay.</summary>
        public class RecordOutput_StyleDetailDto
        {
            public Guid   StyleDetailId { get; set; }
            public string DetailName    { get; set; }
            public int    TodayTotal    { get; set; }   // tổng đã nhập hôm nay (cộng dồn)
        }

        /// <summary>Request ghi sản lượng một lần nhập.</summary>
        public class RecordOutput_SaveRequest
        {
            public Guid   ProductionInfoId { get; set; }
            public Guid   MachineId        { get; set; }
            public string Date             { get; set; }   // "yyyy-MM-dd"
            public string Time             { get; set; }   // "HH:mm"
            public string Keyword          { get; set; }   // Nguồn dữ liệu (ví dụ: "excel")
            public List<RecordOutput_DetailEntry> Details { get; set; } = new();
        }

        public class RecordOutput_DetailEntry
        {
            public Guid StyleDetailId { get; set; }
            public int  OutputNumber  { get; set; }
        }
    }
}
