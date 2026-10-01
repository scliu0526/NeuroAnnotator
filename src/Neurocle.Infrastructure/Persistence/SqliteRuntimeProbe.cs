using Microsoft.Data.Sqlite;

namespace Neurocle.Infrastructure.Persistence
{
    public static class SqliteRuntimeProbe
    {
        public static bool CanOpenInMemoryDatabase()
        {
            using (var connection = new SqliteConnection("Data Source=:memory:"))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT 1;";
                    return (long)command.ExecuteScalar() == 1L;
                }
            }
        }
    }
}
