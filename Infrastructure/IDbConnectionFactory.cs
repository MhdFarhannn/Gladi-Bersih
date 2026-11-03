using MySqlConnector;

namespace Gladi_Bersih.Infrastructure;

public interface IDbConnectionFactory
{
    MySqlConnection CreateConnection();
}
