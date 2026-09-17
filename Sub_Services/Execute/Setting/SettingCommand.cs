using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        public async Task<string> GetSettingValueAsync(string key, string defaultValue = "")
        {
            var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key && s.Status == 1);
            return setting?.Value ?? defaultValue;
        }

        public async Task<Dictionary<string, string>> GetSystemSettingsAsync()
        {
            var keys = new[] { "SystemTitle", "SystemLogo", "LoginBackground", "HomeBackground" };
            var result = new Dictionary<string, string>();
            try
            {
                var settings = await _context.Settings
                    .Where(s => keys.Contains(s.Key) && s.Status == 1)
                    .ToListAsync();

                foreach (var key in keys)
                {
                    var val = settings.FirstOrDefault(s => s.Key == key)?.Value ?? "";
                    result[key] = val;
                }
            }
            catch
            {
                foreach (var key in keys)
                {
                    result[key] = "";
                }
            }
            return result;
        }

        public async Task UpdateSettingAsync(string key, string value, Guid? userId)
        {
            var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting != null)
            {
                setting.Value = value;
                setting.UpdateDate = DateTime.Now;
                setting.UpdateBy = userId;
                setting.Status = 1;
            }
            else
            {
                setting = new Setting
                {
                  
                    Key = key,
                    Value = value,
                    Status = 1,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    CreateBy = userId,
                    UpdateBy = userId
                };
                _context.Settings.Add(setting);
            }
            await _context.SaveChangesAsync();
        }
    }
}
