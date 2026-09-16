using System;
using System.Collections.Generic;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  LINE PROGRESS: DTO classes cho trang thống kê tiến độ theo Line
        // =====================================================================

        /// <summary>Response trả về cho API GetLineProgressData.</summary>
        public class LineProgress_Response
        {
            /// <summary>Ngày bắt đầu thống kê (yyyy-MM-dd).</summary>
            public string FromDate { get; set; }

            /// <summary>Ngày kết thúc thống kê (yyyy-MM-dd).</summary>
            public string ToDate { get; set; }

            /// <summary>Danh sách bộ phận kèm dữ liệu tổng hợp.</summary>
            public List<LineProgress_DepartmentDto> Departments { get; set; } = new();
        }

        /// <summary>Dữ liệu tổng hợp của một bộ phận.</summary>
        public class LineProgress_DepartmentDto
        {
            public Guid   DepartmentId   { get; set; }
            public string DepartmentName { get; set; }

            /// <summary>Danh sách các tổ (Line) có Target > 0 trong ngày.</summary>
            public List<LineProgress_LineDto> Lines { get; set; } = new();

            /// <summary>Tổng Target của tất cả tổ trong ngày.</summary>
            public int TotalTarget { get; set; }

            /// <summary>Tổng sản lượng ngày (effective) của tất cả tổ.</summary>
            public int TotalTodayOutput { get; set; }

            /// <summary>Tổng sản lượng tích lũy đến ngày đó.</summary>
            public int TotalTotalOutput { get; set; }

            /// <summary>Tổng TotalQty mã Status==1 (dùng cho target kỳ).</summary>
            public int TotalTotalQty { get; set; }

            /// <summary>
            /// Tổng TotalQty cố định (Status >= 0) — không đổi theo kỳ lọc.
            /// Dùng để tính % hoàn thành: TotalOutput / TotalQtyFixed.
            /// </summary>
            public int TotalQtyFixed { get; set; }

            /// <summary>% sản lượng ngày / tổng target.</summary>
            public double TodayOutputPct { get; set; }

            /// <summary>% trung bình target của các tổ.</summary>
            public double AvgTargetPct { get; set; }
        }

        /// <summary>Dữ liệu sản lượng của một tổ (Line) trong ngày.</summary>
        public class LineProgress_LineDto
        {
            /// <summary>Tên tổ (Line number).</summary>
            public string Line { get; set; }

            /// <summary>Tổng Target kỳ (target ngày × số ngày, chỉ Status==1).</summary>
            public int Target { get; set; }

            /// <summary>Tổng sản lượng kỳ (effective).</summary>
            public int TodayOutput { get; set; }

            /// <summary>Tổng sản lượng tích lũy đến ngày.</summary>
            public int TotalOutput { get; set; }

            /// <summary>Tổng TotalQty các mã hàng Status==1 trong tổ.</summary>
            public int TotalQty { get; set; }

            /// <summary>
            /// Tổng TotalQty cố định (Status >= 0: đang SX + chờ + hoàn thành).
            /// Không đổi khi thay đổi khoảng ngày lọc.
            /// </summary>
            public int TotalQtyFixed { get; set; }

            /// <summary>% sản lượng kỳ / target kỳ.</summary>
            public double TodayOutputPct { get; set; }

            /// <summary>% hoàn thành = totalOutput / TotalQtyFixed.</summary>
            public double CompletionPct { get; set; }
        }
    }
}
