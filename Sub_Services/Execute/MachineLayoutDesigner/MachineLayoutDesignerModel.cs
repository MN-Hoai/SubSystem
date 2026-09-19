using System;
using System.Collections.Generic;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  REQUEST: Lưu toàn bộ layout + items
        // =====================================================================

        /// <summary>
        /// Request class chứa toàn bộ tham số để upsert một layout và danh sách máy.
        /// </summary>
        public class MachineLayout_SaveRequest
        {
            /// <summary>Id layout đã có. Null = tạo mới.</summary>
            public Guid? LayoutId { get; set; }

            /// <summary>Bộ phận sản xuất (bắt buộc).</summary>
            public Guid DepartmentId { get; set; }

            /// <summary>Tên layout (VD: "Layout chính AT").</summary>
            public string Name { get; set; } = "Layout";

            /// <summary>Số hàng trong lưới.</summary>
            public int RowCount { get; set; } = 3;

            /// <summary>Số cột trong lưới.</summary>
            public int ColumnCount { get; set; } = 4;

            /// <summary>Khoảng cách giữa các ô (px).</summary>
            public decimal Gap { get; set; } = 12;

            /// <summary>Chiều rộng ô cố định (null = auto).</summary>
            public decimal? CellWidth { get; set; }

            /// <summary>Chiều cao ô cố định (null = auto).</summary>
            public decimal? CellHeight { get; set; }

            /// <summary>Danh sách máy trong các ô.</summary>
            public List<MachineLayout_ItemRequest> Items { get; set; } = new();
        }

        /// <summary>
        /// Thông tin một máy tại một ô trong layout.
        /// </summary>
        public class MachineLayout_ItemRequest
        {
            /// <summary>Id ProductLayoutItem đã có. Null = tạo mới.</summary>
            public Guid? ItemId { get; set; }

            /// <summary>Id ProductionMachine đã có. Null = tạo mới.</summary>
            public Guid? MachineId { get; set; }

            /// <summary>Hàng (1-based).</summary>
            public int RowIndex { get; set; }

            /// <summary>Cột (1-based).</summary>
            public int ColumnIndex { get; set; }

            /// <summary>Tên / số hiệu máy → lưu vào ProductionMachine.MachineNumber.</summary>
            public string MachineName { get; set; }

            /// <summary>Ghi chú → lưu vào ProductionMachine.Remark.</summary>
            public string Remark { get; set; }

            /// <summary>Keyword tìm kiếm.</summary>
            public string Keyword { get; set; }

            /// <summary>Trạng thái máy: 1=Hoạt động, 2=Ngừng, 3=Bảo trì.</summary>
            public int Status { get; set; } = 1;

            /// <summary>Thứ tự hiển thị (optional).</summary>
            public int? SortOrder { get; set; }
        }

        // =====================================================================
        //  OUTPUT DTO: Layout + Items
        // =====================================================================

        /// <summary>
        /// DTO trả về một layout kèm toàn bộ danh sách máy đã được đặt vào ô.
        /// </summary>
        public class MachineLayout_LayoutDto
        {
            public Guid Id { get; set; }
            public Guid DepartmentId { get; set; }
            public string DepartmentName { get; set; }
            public string Name { get; set; }
            public int RowCount { get; set; }
            public int ColumnCount { get; set; }
            public decimal Gap { get; set; }
            public decimal? CellWidth { get; set; }
            public decimal? CellHeight { get; set; }
            public int Status { get; set; }
            public List<MachineLayout_ItemDto> Items { get; set; } = new();
        }

        /// <summary>
        /// DTO cho một máy đặt trong ô của layout.
        /// </summary>
        public class MachineLayout_ItemDto
        {
            public Guid ItemId { get; set; }
            public Guid MachineId { get; set; }
            public int RowIndex { get; set; }
            public int ColumnIndex { get; set; }
            public string MachineName { get; set; }   // ProductionMachine.MachineNumber
            public string Remark { get; set; }        // ProductionMachine.Remark
            public string Keyword { get; set; }
            public int Status { get; set; }           // ProductLayoutItem.Status
            public int? SortOrder { get; set; }

            /// <summary>
            /// Các mã sản xuất đang chạy trên máy này (dùng để search theo Style/SP/Tổ/Màu).
            /// </summary>
            public List<MachineLayout_ProductionCodeBrief> ProductionCodes { get; set; } = new();
        }

        /// <summary>Thông tin ngắn gọn về mã đang chạy trên máy để hiển thị và search.</summary>
        public class MachineLayout_ProductionCodeBrief
        {
            public Guid   ProductionInfoId { get; set; }
            public string Spmain          { get; set; }
            public string Style           { get; set; }
            public string Line            { get; set; }
            public string Color           { get; set; }
            /// <summary>Target ngày (từ ProductionInfo.Target).</summary>
            public int?   Target          { get; set; }
            /// <summary>Sản lượng hôm nay: min(tổng từng công đoạn) khi đủ công đoạn, ngược lại = 0.</summary>
            public int    TodayOutput     { get; set; }
            /// <summary>Phần trăm đạt target hôm nay (TodayOutput / Target * 100).</summary>
            public double TodayOutputPct  { get; set; }
        }


        /// <summary>
        /// Kết quả trả về sau khi lưu layout.
        /// </summary>
        public class MachineLayout_SaveResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public Guid? LayoutId { get; set; }
        }

        /// <summary>
        /// DTO tên bộ phận (dùng cho dropdown, reuse từ ProductionProgress nhưng đặt riêng để độc lập).
        /// </summary>
        public class MachineLayout_DepartmentItem
        {
            public Guid Id { get; set; }
            public string DepartmentName { get; set; }
        }

        /// <summary>
        /// DTO cho máy hiển thị trong picker "chọn máy có sẵn".
        /// </summary>
        public class MachineLayout_MachinePickerItem
        {
            public Guid Id { get; set; }
            public string MachineNumber { get; set; }   // Tên / số hiệu máy
            public string Remark { get; set; }          // Ghi chú
            public int Status { get; set; }             // 1=HĐ, 0=Ngừng, -1=Bảo trì, -2=Xóa
        }

    }
}
