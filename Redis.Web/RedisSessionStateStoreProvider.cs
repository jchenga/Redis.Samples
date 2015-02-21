using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using System.Web.SessionState;
using Newtonsoft.Json;
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
        public int Flags { get; set; }
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
            return GetSessionStoreItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            var cached = Database.StringGet(id);
            var item = DeserializeSessionItem(cached);

            item.Locked = false;
            item.Expires = DateTime.UtcNow.AddMinutes(_configSection.Timeout.TotalMinutes);
            cached = SerializeSessionItem(item);

            Database.StringSet(id, cached);
            Database.KeyExpire(id, item.Expires);
        }

       

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            var items = Serialize((SessionStateItemCollection) item.Items);
            string cached = string.Empty;
            SessionItem sessionItem = null;

            if (newItem)
            {
               var utcNow = DateTime.UtcNow;

               sessionItem = new SessionItem
                {
                    SessionId = id,
                    Created = utcNow,
                    Expires = utcNow,
                    LockDate = utcNow,
                    LockId = 0,
                    Timeout = item.Timeout,
                    SessionItems = string.Empty,
                    Flags = (int)SessionStateActions.None
                };
            }
            else
            {
                cached = Database.StringGet(id);
                sessionItem = DeserializeSessionItem(cached);
                sessionItem.Expires = DateTime.UtcNow.AddMinutes(item.Timeout);
                sessionItem.Locked = false;
                sessionItem.SessionItems = items;
                sessionItem.LockId = (int) lockId;
               
            }

            cached = SerializeSessionItem(sessionItem);
            Database.StringSet(id, cached);
            Database.KeyExpire(id, sessionItem.Expires);
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            
            var cached = Database.StringGet(id);
            if (cached.IsNullOrEmpty)
                return;
            var sessionItem = DeserializeSessionItem(cached);
            if (sessionItem.LockId == (int) lockId)
                Database.KeyDelete(id);
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            if (!Database.KeyExists(id))
                return;

            var cached = Database.StringGet(id);
            var sessionItem = DeserializeSessionItem(cached);

            sessionItem.Expires = DateTime.UtcNow.AddMinutes(_configSection.Timeout.TotalMinutes);
            cached = SerializeSessionItem(sessionItem);

            Database.StringSet(id, cached);
            Database.KeyExpire(id, sessionItem.Expires);
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(new SessionStateItemCollection(), 
                SessionStateUtility.GetSessionStaticObjects(context),
                timeout);
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            var utcNow = DateTime.UtcNow;

            var item = new SessionItem
            {
                SessionId = id,
                Created = utcNow,
                Expires = utcNow,
                LockDate = utcNow,
                LockId = 0,
                Timeout = timeout,
                SessionItems = string.Empty,
                Flags = (int) SessionStateActions.None
               
            };
            var serialized = SerializeSessionItem(item);
            Database.StringSet(id, serialized);
            Database.KeyExpire(id, utcNow.AddMinutes(timeout));
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
            SessionItem cachedItem = null;
            
            SessionStateStoreData item = null;
            string sessionItems = null;
            int timeout = 0;

            lockAge = TimeSpan.Zero;
            lockId = null;
            locked = false;
            actionFlags = SessionStateActions.None;
            string cached = Database.StringGet(id);
            var deleteData = false;

            if (string.IsNullOrEmpty(cached))
            {
                locked = false;
                return item;
            }

            cachedItem = JsonConvert.DeserializeObject<SessionItem>(cached);

            if (lockRecord)
            {
                

                if (cachedItem.Locked != true)
                {
                    cachedItem.Locked = true;
                    cachedItem.LockDate = DateTime.UtcNow;

                    cached = JsonConvert.SerializeObject(cachedItem);
                    Database.StringSet(id, cached);
                }

                locked = true;
            }

            var expires = cachedItem.Expires;
            if (expires < DateTime.UtcNow)
            {
                locked = false;
            }

            sessionItems = cachedItem.SessionItems;
            lockId = cachedItem.LockId;
            lockAge = DateTime.UtcNow.Subtract(cachedItem.LockDate);
            actionFlags = (SessionStateActions) cachedItem.Flags;
            timeout = cachedItem.Timeout;

            if (!locked)
            {
                lockId = (int) lockId + 1;
                cachedItem.LockId = (int) lockId;
                cachedItem.Flags = (int) SessionStateActions.None;
                cached = JsonConvert.SerializeObject(cachedItem);
                Database.StringSet(id, cached);

                if (actionFlags == SessionStateActions.InitializeItem)
                    item = CreateNewStoreData(context, (int) _configSection.Timeout.TotalMinutes);
                else
                    item = Deserialize(context, sessionItems, timeout);
            }
            return item;
        }

        private string Serialize(SessionStateItemCollection items)
        {
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(ms))
                {
                    if (items != null)
                        items.Serialize(writer);
                    return Convert.ToBase64String(ms.ToArray());
                }
        }

        private SessionStateStoreData Deserialize(HttpContext context,
            string serializedItems, int timeout)
        {
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(serializedItems)))
            {
                SessionStateItemCollection sessionItems = new SessionStateItemCollection();

                if (ms.Length > 0)
                {
                    using (BinaryReader reader = new BinaryReader(ms))
                    {
                        sessionItems = SessionStateItemCollection.Deserialize(reader);
                    }
                }

               return new SessionStateStoreData(sessionItems, SessionStateUtility.GetSessionStaticObjects(context),
               timeout);
            }
        }

        private static SessionItem DeserializeSessionItem(RedisValue cached)
        {
            return JsonConvert.DeserializeObject<SessionItem>(cached);
        }

        private static string SerializeSessionItem(SessionItem item)
        {
            return JsonConvert.SerializeObject(item);
        }
    }
}