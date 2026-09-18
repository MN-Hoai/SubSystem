namespace SNP_SubSystem.Services
{
    public class BackupConfig
    {
        public string FolderPath              { get; set; } = "wwwroot/backups";
        public bool   AutoBackupEnabled       { get; set; } = false;
        public int    AutoBackupIntervalHours { get; set; } = 24;
        public int    MaxKeepFiles            { get; set; } = 30;
    }
}
