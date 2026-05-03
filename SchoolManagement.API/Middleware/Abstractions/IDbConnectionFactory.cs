using System.Data;
using System.Threading.Tasks;

namespace SchoolManagement.Application.Abstractions;

public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateConnectionAsync();
}

