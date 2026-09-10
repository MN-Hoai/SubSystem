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
            public int    ImportedRows { get; set; }
            public int    SkippedRows  { get; set; }
            public List<string> Errors { get; set; } = new();
        }

        /// <summary>Một dòng dữ liệu parse được từ Excel.</summary>
        public class ImportOutput_Row
        {
            public string Date          { get; set; }   // yyyy-MM-dd
            public string Time          { get; set; }   // HH:mm
            public string MachineName   { get; set; }
            public string Spmain        { get; set; }
            public string DetailName    { get; set; }
            public int    OutputNumber  { get; set; }
            public int    RowIndex      { get; set; }   // for error reporting
        }
    }
}
