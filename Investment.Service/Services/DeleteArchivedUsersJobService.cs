using Invest.Core.Settings;
using Investment.Service.Interfaces;
using Microsoft.Data.SqlClient;

namespace Investment.Service.Services
{
    public class DeleteArchivedUsersJobService : IDeleteArchivedUsersJobService
    {
        private readonly AppSecrets _appSecrets;

        public DeleteArchivedUsersJobService(AppSecrets appSecrets)
        {
            _appSecrets = appSecrets;
        }

        public async Task RunCleanupAsync(CancellationToken cancellationToken = default)
        {
            var connectionString = _appSecrets.SqlConnection;

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand("dbo.sp_DailyCleanup", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
                CommandTimeout = 600  // 10 minutes
            };

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
