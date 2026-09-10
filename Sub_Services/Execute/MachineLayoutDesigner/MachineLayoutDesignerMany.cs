using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  GET MANY: Lấy danh sách bộ phận (dùng cho dropdown)
        // =====================================================================

        /// <summary>
        /// Trả về danh sách bộ phận đang hoạt động (Status == 1) để hiện trong dropdown.
        /// </summary>
        public async Task<List<MachineLayout_DepartmentItem>> GetDepartmentsForLayout()
        {
            return await _context.ProductionDepartments
                .Where(d => d.Status == 1)
                .OrderBy(d => d.DepartmentName)
                .AsNoTracking()
                .Select(d => new MachineLayout_DepartmentItem
                {
                    Id             = d.Id,
                    DepartmentName = d.DepartmentName
                })
                .ToListAsync();
        }

        // =====================================================================
        //  GET MANY: Lấy danh sách máy theo bộ phận (cho picker "máy có sẵn")
        // =====================================================================

        /// <summary>
        /// Trả về tất cả máy của một bộ phận (Status != 0),
        /// sắp xếp theo MachineNumber để dễ tìm.
        /// </summary>
        public async Task<List<MachineLayout_MachinePickerItem>> GetMachinesByDepartment(Guid departmentId)
        {
            return await _context.ProductionMachines
                .Where(m => m.ProductionDepartmentId == departmentId && m.Status != 0)
                .OrderBy(m => m.MachineNumber)
                .AsNoTracking()
                .Select(m => new MachineLayout_MachinePickerItem
                {
                    Id            = m.Id,
                    MachineNumber = m.MachineNumber,
                    Remark        = m.Remark,
                    Status        = m.Status ?? 1
                })
                .ToListAsync();
        }

        // =====================================================================

        //  GET MANY: Lấy layout theo bộ phận (layout đầu tiên Status==1)
        // =====================================================================

        /// <summary>
        /// Lấy layout đang hoạt động (Status == 1) của một bộ phận,
        /// kèm toàn bộ ProductLayoutItems và thông tin ProductionMachine.
        /// Trả null nếu bộ phận chưa có layout.
        /// </summary>
        public async Task<MachineLayout_LayoutDto> GetLayoutByDepartment(Guid departmentId)
        {
            var layout = await _context.ProductionDepartmentLayouts
                .AsNoTracking()
                .Where(l => l.ProductionDepartmentId == departmentId && l.Status == 1)
                .OrderByDescending(l => l.CreateDate)
                .Select(l => new MachineLayout_LayoutDto
                {
                    Id             = l.Id,
                    DepartmentId   = l.ProductionDepartmentId,
                    DepartmentName = l.ProductionDepartment.DepartmentName,
                    Name           = l.Name,
                    RowCount       = l.RowCount,
                    ColumnCount    = l.ColumnCount,
                    Gap            = l.Gap ?? 12,
                    CellWidth      = l.CellWidth,
                    CellHeight     = l.CellHeight,
                    Status         = l.Status,
                    Items          = l.ProductLayoutItems
                        .Where(i => i.Status == 1)
                        .Select(i => new MachineLayout_ItemDto
                        {
                            ItemId      = i.Id,
                            MachineId   = i.ProductionMachineId,
                            RowIndex    = i.RowIndex ?? 1,
                            ColumnIndex = i.ColumnIndex ?? 1,
                            MachineName = i.ProductionMachine.MachineNumber,
                            Remark      = i.ProductionMachine.Remark,
                            Keyword     = i.Keyword,
                            Status      = i.ProductionMachine.Status ?? 1,  // trạng thái thực của máy
                            SortOrder   = i.SortOrder
                        })
                        .OrderBy(i => i.RowIndex).ThenBy(i => i.ColumnIndex)
                        .ToList()
                })
                .FirstOrDefaultAsync();

            return layout;
        }

        // =====================================================================
        //  GET MANY: Tất cả layout (cho trang ProductionDepartment)
        // =====================================================================

        /// <summary>
        /// Lấy tất cả layout Status==1, kèm items, tùy chọn lọc theo departmentId.
        /// Status của item lấy từ ProductionMachine.Status (trạng thái thực của máy).
        /// </summary>
        public async Task<List<MachineLayout_LayoutDto>> GetAllLayouts(Guid? departmentId = null)
        {
            var query = _context.ProductionDepartmentLayouts
                .AsNoTracking()
                .Where(l => l.Status == 1);

            if (departmentId.HasValue && departmentId != Guid.Empty)
                query = query.Where(l => l.ProductionDepartmentId == departmentId.Value);

            return await query
                .OrderBy(l => l.ProductionDepartment.DepartmentName)
                .ThenByDescending(l => l.CreateDate)
                .Select(l => new MachineLayout_LayoutDto
                {
                    Id             = l.Id,
                    DepartmentId   = l.ProductionDepartmentId,
                    DepartmentName = l.ProductionDepartment.DepartmentName,
                    Name           = l.Name,
                    RowCount       = l.RowCount,
                    ColumnCount    = l.ColumnCount,
                    Gap            = l.Gap ?? 12,
                    CellWidth      = l.CellWidth,
                    CellHeight     = l.CellHeight,
                    Status         = l.Status,
                    Items          = l.ProductLayoutItems
                        .Where(i => i.Status == 1)
                        .Select(i => new MachineLayout_ItemDto
                        {
                            ItemId      = i.Id,
                            MachineId   = i.ProductionMachineId,
                            RowIndex    = i.RowIndex    ?? 1,
                            ColumnIndex = i.ColumnIndex ?? 1,
                            MachineName = i.ProductionMachine.MachineNumber,
                            Remark      = i.ProductionMachine.Remark,
                            Keyword     = i.Keyword,
                            Status      = i.ProductionMachine.Status ?? 1,
                            SortOrder   = i.SortOrder,
                            // Các mã sản xuất đang chạy trên máy (distinct theo ProductionInfoId)
                            ProductionCodes = i.ProductionMachine.DailyOutputs
                                .Where(d => d.Status == 1)
                                .Select(d => d.ProductionInfo)
                                .Where(p => p != null && p.Status == 1)
                                .Select(p => new MachineLayout_ProductionCodeBrief
                                {
                                    Spmain = p.Spmain,
                                    Style  = p.Style,
                                    Line   = p.Line,
                                    Color  = p.Color
                                })
                                .Distinct()
                                .ToList()
                        })
                        .OrderBy(i => i.RowIndex).ThenBy(i => i.ColumnIndex)
                        .ToList()
                })
                .ToListAsync();
        }
    }
}
