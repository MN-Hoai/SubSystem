using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  GET ONE: Lấy layout theo Id
        // =====================================================================

        /// <summary>
        /// Trả về một layout theo Id kèm toàn bộ items và thông tin máy.
        /// Trả null nếu không tồn tại.
        /// </summary>
        public async Task<MachineLayout_LayoutDto> GetLayoutById(Guid layoutId)
        {
            var layout = await _context.ProductionDepartmentLayouts
                .AsNoTracking()
                .Where(l => l.Id == layoutId)
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
                        .Select(i => new MachineLayout_ItemDto
                        {
                            ItemId      = i.Id,
                            MachineId   = i.ProductionMachineId,
                            RowIndex    = i.RowIndex ?? 1,
                            ColumnIndex = i.ColumnIndex ?? 1,
                            MachineName = i.ProductionMachine.MachineNumber,
                            Remark      = i.ProductionMachine.Remark,
                            Keyword     = i.Keyword,
                            Status      = i.Status,
                            SortOrder   = i.SortOrder
                        })
                        .OrderBy(i => i.RowIndex).ThenBy(i => i.ColumnIndex)
                        .ToList()
                })
                .FirstOrDefaultAsync();

            return layout;
        }
    }
}
