using Microsoft.Data.Sqlite;

namespace Neurocle.Infrastructure.Persistence
{
    // 確認目前部署環境是否能使用 SQLite 原生執行階段。
    public static class SqliteRuntimeProbe
    {
        /// <summary>以記憶體資料庫執行最小查詢，確認 SQLite 可正常開啟及讀取。</summary>
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
