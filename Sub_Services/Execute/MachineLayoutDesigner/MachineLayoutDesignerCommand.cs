using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  COMMAND: Lưu layout + items (upsert)
        // =====================================================================

        /// <summary>
        /// Upsert toàn bộ layout và danh sách máy trong các ô:
        ///   1. Upsert ProductionDepartmentLayout (insert nếu LayoutId null, update nếu có).
        ///   2. Với mỗi item trong request:
        ///      - Upsert ProductionMachine (insert nếu MachineId null, update nếu có).
        ///      - Upsert ProductLayoutItem.
        ///   3. Xóa (soft delete Status=-2) các ProductLayoutItem không còn trong danh sách gửi lên.
        /// </summary>
        public async Task<MachineLayout_SaveResult> SaveLayout(MachineLayout_SaveRequest req)
        {
            try
            {
                var now = DateTime.Now;

                // ----------------------------------------------------------------
                // 1. Upsert ProductionDepartmentLayout
                // ----------------------------------------------------------------
                ProductionDepartmentLayout layout;

                if (req.LayoutId.HasValue)
                {
                    layout = await _context.ProductionDepartmentLayouts
                        .FirstOrDefaultAsync(l => l.Id == req.LayoutId.Value);

                    if (layout == null)
                        return new MachineLayout_SaveResult { Success = false, Message = "Không tìm thấy layout." };

                    layout.Name                  = req.Name ?? layout.Name;
                    layout.RowCount              = req.RowCount;
                    layout.ColumnCount           = req.ColumnCount;
                    layout.Gap                   = req.Gap;
                    layout.CellWidth             = req.CellWidth;
                    layout.CellHeight            = req.CellHeight;
                    layout.ProductionDepartmentId = req.DepartmentId;
                    layout.UpdateDate            = now;
                }
                else
                {
                    layout = new ProductionDepartmentLayout
                    {
                        Id                      = Guid.NewGuid(),
                        ProductionDepartmentId  = req.DepartmentId,
                        Name                    = req.Name ?? "Layout",
                        RowCount                = req.RowCount,
                        ColumnCount             = req.ColumnCount,
                        Gap                     = req.Gap,
                        CellWidth               = req.CellWidth,
                        CellHeight              = req.CellHeight,
                        Status                  = 1,
                        CreateDate              = now,
                        UpdateDate              = now
                    };
                    _context.ProductionDepartmentLayouts.Add(layout);
                }

                await _context.SaveChangesAsync();

                // ----------------------------------------------------------------
                // 2. Xử lý từng item gửi lên
                // ----------------------------------------------------------------
                var incomingItemIds = req.Items
                    .Where(i => i.ItemId.HasValue)
                    .Select(i => i.ItemId!.Value)
                    .ToHashSet();

                foreach (var itemReq in req.Items)
                {
                    // --- 2a. Upsert ProductionMachine ---
                    ProductionMachine machine;

                    if (itemReq.MachineId.HasValue)
                    {
                        machine = await _context.ProductionMachines
                            .FirstOrDefaultAsync(m => m.Id == itemReq.MachineId.Value);

                        if (machine == null)
                            return new MachineLayout_SaveResult { Success = false, Message = $"Không tìm thấy máy Id={itemReq.MachineId}" };

                        machine.MachineNumber            = itemReq.MachineName ?? machine.MachineNumber;
                        machine.Remark                   = itemReq.Remark;
                        machine.Keyword                  = itemReq.Keyword;
                        machine.Status                   = itemReq.Status;
                        machine.ProductionDepartmentId   = req.DepartmentId;
                        machine.UpdateDate               = now;
                    }
                    else
                    {
                        machine = new ProductionMachine
                        {
                            Id                     = Guid.NewGuid(),
                            ProductionDepartmentId = req.DepartmentId,
                            MachineNumber          = itemReq.MachineName ?? "Máy mới",
                            Remark                 = itemReq.Remark,
                            Keyword                = itemReq.Keyword,
                            Status                 = itemReq.Status,
                            CreateDate             = now,
                            UpdateDate             = now
                        };
                        _context.ProductionMachines.Add(machine);
                        await _context.SaveChangesAsync(); // cần Id để gán cho item
                        itemReq.MachineId = machine.Id;
                    }

                    // --- 2b. Upsert ProductLayoutItem ---
                    ProductLayoutItem item;

                    if (itemReq.ItemId.HasValue)
                    {
                        item = await _context.ProductLayoutItems
                            .FirstOrDefaultAsync(i => i.Id == itemReq.ItemId.Value);

                        if (item == null)
                            return new MachineLayout_SaveResult { Success = false, Message = $"Không tìm thấy item Id={itemReq.ItemId}" };

                        item.RowIndex               = itemReq.RowIndex;
                        item.ColumnIndex            = itemReq.ColumnIndex;
                        item.ProductionMachineId    = machine.Id;
                        item.Keyword                = itemReq.Keyword;
                        item.Status                 = 1;
                        item.SortOrder              = itemReq.SortOrder;
                        item.UpdateDate             = now;
                    }
                    else
                    {
                        item = new ProductLayoutItem
                        {
                            Id                             = Guid.NewGuid(),
                            ProductionDepartmentLayoutId   = layout.Id,
                            ProductionMachineId            = machine.Id,
                            RowIndex                       = itemReq.RowIndex,
                            ColumnIndex                    = itemReq.ColumnIndex,
                            Keyword                        = itemReq.Keyword,
                            Status                         = 1,
                            SortOrder                      = itemReq.SortOrder,
                            CreateDate                     = now,
                            UpdateDate                     = now
                        };
                        _context.ProductLayoutItems.Add(item);
                        incomingItemIds.Add(item.Id);
                    }
                }

                await _context.SaveChangesAsync();

                // ----------------------------------------------------------------
                // 3. Soft-delete các items không còn trong danh sách (ô bị xóa máy)
                // ----------------------------------------------------------------
                var removedItems = await _context.ProductLayoutItems
                    .Where(i => i.ProductionDepartmentLayoutId == layout.Id
                                && i.Status == 1
                                && !incomingItemIds.Contains(i.Id))
                    .ToListAsync();

                foreach (var removed in removedItems)
                {
                    removed.Status     = -2;
                    removed.UpdateDate = now;
                }

                if (removedItems.Count > 0)
                    await _context.SaveChangesAsync();

                return new MachineLayout_SaveResult
                {
                    Success  = true,
                    Message  = "Lưu layout thành công.",
                    LayoutId = layout.Id
                };
            }
            catch (Exception ex)
            {
                return new MachineLayout_SaveResult
                {
                    Success = false,
                    Message = $"Lỗi lưu layout: {ex.Message}"
                };
            }
        }

        // =====================================================================
        //  COMMAND: Xóa một item khỏi layout (soft delete)
        // =====================================================================

        /// <summary>
        /// Soft-delete một ProductLayoutItem theo Id (đặt Status = -2).
        /// Không xóa ProductionMachine để giữ lịch sử.
        /// </summary>
        public async Task<bool> DeleteLayoutItem(Guid itemId)
        {
            var item = await _context.ProductLayoutItems
                .FirstOrDefaultAsync(i => i.Id == itemId);

            if (item == null) return false;

            item.Status     = -2;
            item.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return true;
        }

        // =====================================================================
        //  COMMAND: Xóa layout
        // =====================================================================

        /// <summary>
        /// Xóa layout (soft delete Status = -2).
        /// </summary>
        public async Task<bool> DeleteLayout(Guid layoutId)
        {
            var layout = await _context.ProductionDepartmentLayouts
                .FirstOrDefaultAsync(l => l.Id == layoutId);

            if (layout == null) return false;

            layout.Status     = -2;
            layout.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return true;
        }

        // =====================================================================
        //  COMMAND: Cập nhật trạng thái máy trực tiếp (không cần lưu layout)
        // =====================================================================

        /// <summary>
        /// Cập nhật Status của một ProductionMachine.
        /// Status: 1=Hoạt động, 0=Ngừng, -1=Bảo trì, -2=Xóa mềm
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateMachineStatus(Guid machineId, int status)
        {
            var machine = await _context.ProductionMachines
                .FirstOrDefaultAsync(m => m.Id == machineId);
            if (machine == null)
                return (false, "Không tìm thấy máy.");

            machine.Status     = status;
            machine.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Đã cập nhật trạng thái máy.");
        }

        /// <summary>
        /// Cập nhật thông tin máy: tên, ghi chú, trạng thái.
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateMachineInfo(Guid machineId, string machineName, string remark, int status)
        {
            var machine = await _context.ProductionMachines
                .FirstOrDefaultAsync(m => m.Id == machineId);
            if (machine == null)
                return (false, "Không tìm thấy máy.");

            if (!string.IsNullOrWhiteSpace(machineName))
                machine.MachineNumber = machineName.Trim();

            machine.Remark     = remark?.Trim();
            machine.Status     = status;
            machine.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Đã cập nhật thông tin máy.");
        }

        /// <summary>
        /// Cập nhật Status hàng loạt cho nhiều máy.
        /// </summary>
        public async Task<(bool Success, string Message)> BulkUpdateMachineStatus(List<Guid> machineIds, int status)
        {
            if (machineIds == null || !machineIds.Any())
                return (false, "Không có máy nào được chọn.");

            var machines = await _context.ProductionMachines
                .Where(m => machineIds.Contains(m.Id))
                .ToListAsync();

            if (!machines.Any())
                return (false, "Không tìm thấy máy hợp lệ.");

            var now = DateTime.Now;
            foreach (var m in machines)
            {
                m.Status     = status;
                m.UpdateDate = now;
            }

            // Nếu xóa máy (status=-2), soft-delete luôn các ProductLayoutItem tương ứng
            if (status == -2)
            {
                var layoutItems = await _context.ProductLayoutItems
                    .Where(i => machineIds.Contains(i.ProductionMachineId) && i.Status == 1)
                    .ToListAsync();
                foreach (var li in layoutItems)
                {
                    li.Status     = -2;
                    li.UpdateDate = now;
                }
            }

            await _context.SaveChangesAsync();
            return (true, $"Đã cập nhật {machines.Count} máy.");
        }
    }
}
