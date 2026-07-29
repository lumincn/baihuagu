using TaskRunner.Contracts.Backup;

namespace TaskRunner.Controllers
{
    public class FullBackupRequest : TaskRunner.Contracts.Backup.FullBackupRequest { }

    public class FullBackupResponse : TaskRunner.Contracts.Backup.FullBackupResponse { }

    public class FullRestoreRequest : TaskRunner.Contracts.Backup.FullRestoreRequest { }

    public class FullRestoreResponse : TaskRunner.Contracts.Backup.FullRestoreResponse { }

    public class ValidateBackupRequest : TaskRunner.Contracts.Backup.ValidateBackupRequest { }

    public class ValidateBackupResponse : TaskRunner.Contracts.Backup.ValidateBackupResponse { }

    public class BackupListResponse : TaskRunner.Contracts.Backup.BackupListResponse { }
}