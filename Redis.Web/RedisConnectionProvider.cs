using System;
using StackExchange.Redis;

namespace Redis.Web
{
    public class RedisConnectionProvider : IDisposable
    {
        private ConnectionMultiplexer _connection;
        private readonly object _locker = new object();

        public ConnectionMultiplexer GetConnection(string connectionString)
        {
            if (_connection == null)
            {
                lock (_locker)
                {
                    if (_connection == null)
                    {
                        _connection = ConnectionMultiplexer.Connect(connectionString);
                    }
                }
            }
    
            return _connection;
        }

        public void Dispose()
        {
            if (_connection != null)
                _connection.Dispose();
        }
    }
}