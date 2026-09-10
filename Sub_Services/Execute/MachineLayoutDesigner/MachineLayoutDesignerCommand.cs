using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

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
        ///   3. Xóa (soft delete Status=0) các ProductLayoutItem không còn trong danh sách gửi lên.
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
                    removed.Status     = 0;
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
        /// Soft-delete một ProductLayoutItem theo Id (đặt Status = 0).
        /// Không xóa ProductionMachine để giữ lịch sử.
        /// </summary>
        public async Task<bool> DeleteLayoutItem(Guid itemId)
        {
            var item = await _context.ProductLayoutItems
                .FirstOrDefaultAsync(i => i.Id == itemId);

            if (item == null) return false;

            item.Status     = 0;
            item.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
