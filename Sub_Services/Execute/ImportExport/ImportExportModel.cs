using System;
using System.Collections.Generic;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  DTOs — ImportExport
        // =====================================================================

        /// <summary>Kết quả import từ file Excel sản lượng.</summary>
        public class ImportOutput_Result
        {
            public bool   Success      { get; set; }
            public string Message      { get; set; }
            public int    TotalRows    { get; set; }
            public int    ImportedRows { get; set; } // = InsertedRows + UpdatedRows
            public int    InsertedRows { get; set; } // ghi mới DailyOutput
            public int    UpdatedRows  { get; set; } // cập nhật DailyOutput cùng giờ+phút
            public int    SkippedRows  { get; set; }
            public List<string> Errors { get; set; } = new();
            public List<string> Logs   { get; set; } = new();
        }

        /// <summary>Một dòng dữ liệu parse được từ Excel.</summary>
        public class ImportOutput_Row
        {
            public string Date          { get; set; }   // yyyy-MM-dd
            public string Time          { get; set; }   // HH:mm
            public string MachineName   { get; set; }
            public string Line          { get; set; }   // thêm mới — để tra ProductionInfo chính xác
            public string Style         { get; set; }   // thêm mới
            public string Spmain        { get; set; }
            public int?   OrderQty      { get; set; }   // cột 7 — Order Qty (số lượng đơn hàng)
            public string Color         { get; set; }   // cột 8
            public string DetailName    { get; set; }   // cột 9
            public int    OutputNumber  { get; set; }   // cột 10
            public int    RowIndex      { get; set; }   // for error reporting
        }

        /// <summary>
        /// Tuỳ chọn nâng cao khi import sản lượng từ Excel.
        /// </summary>
        public class ImportOutput_Options
        {
            /// <summary>
            /// Option 1 — Tự động tạo mã hàng (ProductionInfo) nếu chưa tồn tại.
            /// Thông tin tạo mới: Line, Style, Spmain, Color, OrderQty lấy từ dòng Excel.
            /// </summary>
            public bool AutoCreateProductionInfo { get; set; } = false;

            /// <summary>
            /// Option 2 — Tự động bật trạng thái mã hàng (Status = 1) khi ghi thành công.
            /// </summary>
            public bool AutoActivateStatus { get; set; } = false;

            /// <summary>
            /// Option 3 — Bỏ qua cập nhật khi trùng giờ+phút.
            /// Nếu true: DailyOutput cùng giờ+phút đã tồn tại → bỏ qua, không ghi đè.
            /// Nếu false (mặc định): cập nhật sản lượng vào bản ghi đã có.
            /// </summary>
            public bool SkipOnDuplicate { get; set; } = false;

            /// <summary>
            /// Option 4 — Tự động tạo StyleInfo + StyleDetail nếu chưa tồn tại.
            /// Nếu không tìm thấy StyleInfo khớp (StyleCode + Keyword==SP): tạo mới StyleInfo.
            /// Nếu StyleInfo có nhưng không có StyleDetail khớp tên công đoạn: tạo mới StyleDetail.
            /// Sau đó ghi sản lượng bình thường.
            /// </summary>
            public bool AutoCreateStyleDetail { get; set; } = false;
        }


        // =====================================================================
        //  DTOs — Import Mã Hàng (ProductionInfo) từ Excel
        // =====================================================================

        /// <summary>Kết quả import mã hàng từ file Excel.</summary>
        public class ImportProductionInfo_Result
        {
            public bool   Success      { get; set; }
            public string Message      { get; set; }
            public int    TotalRows    { get; set; }
            public int    ImportedRows { get; set; } // Tạo mới
            public int    UpdatedRows  { get; set; } // Cập nhật
            public int    SkippedRows  { get; set; }
            public List<string> Errors { get; set; } = new();
        }

        /// <summary>Một dòng dữ liệu mã hàng parse được từ Excel.</summary>
        public class ImportProductionInfo_Row
        {
            public string Line              { get; set; }
            public string Style             { get; set; }
            public string Spmain            { get; set; }
            public int?   TotalQty          { get; set; }
            public string Color             { get; set; }
            public int?   Target            { get; set; }
            public string InlineLine        { get; set; }   // yyyy-MM-dd hoặc null
            public string InlineDepartment  { get; set; }   // yyyy-MM-dd hoặc null
            public int    RowIndex          { get; set; }   // for error reporting
        }
    }
}
