using System;
using System.Collections.Generic;
using System.Text;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        /// <summary>
        /// Model tra ve danh sach ten bo phan san xuat (dung chung)
        /// </summary>
        public class ProductionDepartment_ListName
        {
            public Guid Id { get; set; }

            public string DepartmentName { get; set; }
        }

        // =====================================================================
        //  REQUEST CLASS: Tham so truy van cho GetProductionProgressList
        // =====================================================================

        /// <summary>
        /// Request class chua toan bo tham so loc va phan trang
        /// cho ham GetProductionProgressList.
        /// </summary>
        public class ProductionProgress_QueryRequest
        {
            /// <summary>Ngay bat dau khoang loc (bao gom). Mac dinh: dau thang hien tai.</summary>
            public DateOnly FromDate { get; set; } = DateOnly.FromDateTime(
                new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));

            /// <summary>Ngay ket thuc khoang loc (bao gom). Mac dinh: hom nay.</summary>
            public DateOnly ToDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

            /// <summary>
            /// Tim kiem toan van tren Style, SPMain, Line, Keyword.
            /// De null/rong de bo qua.
            /// </summary>
            public string? Keyword { get; set; }

            /// <summary>
            /// Loc chinh xac theo gia tri cot Line (vi du "01", "02").
            /// De null/rong de bo qua.
            /// </summary>
            public string? Line { get; set; }

            /// <summary>
            /// Loc theo Guid bo phan san xuat.
            /// De null de lay tat ca bo phan.
            /// </summary>
            public Guid? ProductionDepartmentId { get; set; }

            /// <summary>
            /// Loc theo Status: 1 = hoat dong, 0 = ngung...
            /// De null de mac dinh chi lay Status == 1.
            /// </summary>
            public int? Status { get; set; }

            // ---------- Phan trang ----------

            /// <summary>So trang hien tai (bat dau tu 1). Mac dinh: 1.</summary>
            public int Page { get; set; } = 1;

            /// <summary>So ban ghi moi trang. Mac dinh: 20. Toi da: 100.</summary>
            public int PageSize { get; set; } = 20;

            /// <summary>Dam bao PageSize khong qua lon va Page >= 1.</summary>
            public void Normalize()
            {
                if (Page < 1) Page = 1;
                if (PageSize < 1) PageSize = 20;
                if (PageSize > 100) PageSize = 100;
            }
        }

        // =====================================================================
        //  PAGED RESULT: Wrapper tra ve du lieu co phan trang
        // =====================================================================

        /// <summary>
        /// Wrapper du lieu co phan trang, dung chung cho nhieu query tra ve danh sach.
        /// </summary>
        public class PagedResult<T>
        {
            /// <summary>Danh sach du lieu trang hien tai.</summary>
            public List<T> Items { get; set; } = new();

            /// <summary>Tong so ban ghi thoa man dieu kien (chua phan trang).</summary>
            public int TotalCount { get; set; }

            /// <summary>Trang hien tai.</summary>
            public int Page { get; set; }

            /// <summary>So ban ghi moi trang.</summary>
            public int PageSize { get; set; }

            /// <summary>Tong so trang.</summary>
            public int TotalPages => PageSize > 0
                ? (int)Math.Ceiling((double)TotalCount / PageSize)
                : 0;

            /// <summary>Co trang truoc hay khong.</summary>
            public bool HasPreviousPage => Page > 1;

            /// <summary>Co trang tiep theo hay khong.</summary>
            public bool HasNextPage => Page < TotalPages;
        }

        // =====================================================================
        //  OUTPUT DTO: Thong tin tien do san xuat
        // =====================================================================

        /// <summary>
        /// Model tra ve thong tin tien do san xuat theo ProductionInfo,
        /// bao gom toan bo thong tin ProductionInfo + 2 truong tong hop san luong:
        ///   - TotalOutput  : tong san luong tich luy tu truoc den ngay dieu kien (&lt;=)
        ///   - TodayOutput  : san luong trong khoang fromDate -> toDate
        /// </summary>
        public class ProductionProgress_OutputInfo
        {
            // ---------- Khoa chinh ----------
            public Guid Id { get; set; }

            // ---------- Lien ket bo phan ----------
            public Guid ProductionDepartmentId { get; set; }

            // ---------- Thong tin san xuat ----------
            public string Line { get; set; }
            public string Style { get; set; }
            public string Spmain { get; set; }
            public int? TotalQty { get; set; }
            public int? FinishQty { get; set; }
            public string Color { get; set; }
            public int? Target { get; set; }
            public DateOnly? InlineLine { get; set; }
            public DateOnly? InlineDepartment { get; set; }
            public string Remark { get; set; }
            public int? Machine { get; set; }
            public string Keyword { get; set; }
            public int Status { get; set; }
            public DateTime CreateDate { get; set; }
            public DateTime UpdateDate { get; set; }
            public Guid? CreateBy { get; set; }
            public Guid? UpdateBy { get; set; }

            // ---------- 2 truong tong hop san luong ----------

            /// <summary>
            /// Tong san luong tich luy: Sum(DailyOutputDetail.OutputNumber)
            /// cua tat ca DailyOutput co CreateDate &lt;= cuoi toDate.
            /// </summary>
            public int TotalOutput { get; set; }

            /// <summary>
            /// San luong trong khoang: Sum(DailyOutputDetail.OutputNumber)
            /// cua tat ca DailyOutput co CreateDate trong [fromDate, toDate].
            /// </summary>
            public int TodayOutput { get; set; }
        }
    }
}
