using Microsoft.Data.SqlClient;

namespace NexaApi.Data;

public interface IDbConnectionFactory
{
    SqlConnection CreateConnection();
}

