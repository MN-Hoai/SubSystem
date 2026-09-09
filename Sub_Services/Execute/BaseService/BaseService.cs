using Sub_Entities.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        private readonly SNP_SubSystemDBContext _context;
        public SubSystemService(SNP_SubSystemDBContext context)
        {
            _context = context;
        }
    }
}
