using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Web;
using System.Web.Configuration;
using System.Web.SessionState;
using StackExchange.Redis;

namespace Redis.Web
{
    public class SessionItem
    {
        public string SessionId { get; set; }
        public DateTime Created { get; set; }
        public DateTime Expires { get; set; }
        public DateTime LockDate { get; set; }
        public int LockId { get; set; }
        public int Timeout { get; set; }
        public string SessionItems { get; set; }
        public bool Locked { get; set; }
    }

    public class RedisSessionStateStoreProvider : SessionStateStoreProviderBase
    {
        private RedisConnectionProvider _redisConnectionProvider;
        private string _connectionString;
        private SessionStateSection _configSection;

        public override void Initialize(string name, NameValueCollection config)
        {
           
            if (string.IsNullOrEmpty(name))
                name = "RedisSessionStateStore";

            if (String.IsNullOrEmpty(config["description"]))
            {
                config.Remove("description");
                config.Add("description", "Redis State Store Provider");

            }
            base.Initialize(name, config);
            _configSection = (SessionStateSection) ConfigurationManager.GetSection("system.web/sessionState");
            _connectionString = ConfigurationManager.ConnectionStrings["Redi.SessionState.Store"].ConnectionString;
            _redisConnectionProvider = new RedisConnectionProvider();
        }

        private IDatabase Database {get { return _redisConnectionProvider.GetConnection(_connectionString).GetDatabase(); }}
        
        public override void Dispose()
        {
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        public override void InitializeRequest(HttpContext context)
        {
           
        }

        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            throw new NotImplementedException();
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            throw new NotImplementedException();
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            throw new NotImplementedException();
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            throw new NotImplementedException();
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            throw new NotImplementedException();
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            throw new NotImplementedException();
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            throw new NotImplementedException();
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
           
            Database.KeyExpire(id, DateTime.UtcNow.AddMinutes(timeout));
        }

        public override void EndRequest(HttpContext context)
        {
           
        }

        private SessionStateStoreData GetSessionStoreItem(bool lockRecord, HttpContext context,
            string id,
            out bool locked,
            out TimeSpan lockAge,
            out object lockId,
            out SessionStateActions actionFlags)
        {
            SessionStateStoreData item = null;
            lockAge = TimeSpan.Zero;
            lockId = null;
            locked = false;
            actionFlags = SessionStateActions.None;


            return item;
        }
    }
}